// RevCompositeSteps.cs —— 组合步骤：并行 / 重复
// 【语义】Parallel 组内全部完成才算这步完成；Repeat 组内步骤重复 N 轮。
// 【要点】子步骤槽位在构建期分配（AssignSlots），并发跑多份也不会互相覆盖。

using System;

namespace Revolution
{
    /// <summary>
    /// 子步骤游标：组合步骤（并行/重复）用它在一帧内推进一组子步骤。
    /// <para>游标本身也是"运行期状态"，必须存在步骤的状态槽里（不能放在共享的步骤实例上）。</para>
    /// </summary>
    internal sealed class RevStepCursor
    {
        private int _index;         // 当前子步骤下标
        private bool _executed;     // 当前子步骤是否已 Execute

        /// <summary>回到起点（重复步骤开始新一轮时调用）</summary>
        public void Reset()
        {
            _index = 0;
            _executed = false;
        }

        /// <summary>
        /// 推进一组子步骤：返回 true = 全部完成；false = 卡在某个阻塞子步骤（下一帧继续）。
        /// <para>语义与主引擎的 Advance 完全一致：立即步骤同帧连放，阻塞步骤挂起。</para>
        /// </summary>
        public bool Advance(RevSequenceRun run, RevSequenceContext context, RevISequenceStep[] steps)
        {
            while (_index < steps.Length)
            {
                RevISequenceStep step = steps[_index];

                if (!_executed)
                {
                    step.Execute(run, context);
                    _executed = true;
                }

                if (!step.IsCompleted(run, context)) return false;   // 阻塞：挂起

                _index++;
                _executed = false;
            }

            return true;
        }
    }

    /// <summary>
    /// 并行步骤：组内每个子步骤<b>各自独立推进</b>，全部完成时本步才完成。
    /// <para>对应"同时做两件事""既等计时又等条件"这类需求。</para>
    /// </summary>
    internal sealed class RevParallelStep : RevStepBase
    {
        private struct ChildState
        {
            public bool Executed;       // 该子步骤是否已 Execute
            public bool Done;           // 该子步骤是否已完成
        }

        private sealed class State
        {
            public ChildState[] Children;   // 每个子步骤一格（随运行实例复用，稳态零分配）
        }

        private readonly RevISequenceStep[] _steps;

        /// <inheritdoc/>
        public override string Name { get; }

        internal RevParallelStep(string name, RevISequenceStep[] steps)
        {
            Name = name;
            _steps = steps;
        }

        /// <inheritdoc/>
        internal override void AssignSlots(ref int next)
        {
            base.AssignSlots(ref next);                     // ① 先占自己的槽

            for (int i = 0; i < _steps.Length; i++)         // ② 再给子步骤分配（互不重叠）
            {
                if (_steps[i] is RevStepBase baseStep) baseStep.AssignSlots(ref next);
                else next++;
            }
        }

        /// <inheritdoc/>
        public override void Execute(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetOrCreateState<State>(run);

            if (state.Children == null || state.Children.Length != _steps.Length)
                state.Children = new ChildState[_steps.Length];

            for (int i = 0; i < state.Children.Length; i++)
                state.Children[i] = default;                // 重置：本 Run 的本次执行从头开始
        }

        /// <inheritdoc/>
        public override bool IsCompleted(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetState<State>(run);
            if (state?.Children == null) return true;       // 状态丢了：放行，避免卡死

            bool allDone = true;

            for (int i = 0; i < _steps.Length; i++)
            {
                if (state.Children[i].Done) continue;

                RevISequenceStep step = _steps[i];

                if (!state.Children[i].Executed)
                {
                    step.Execute(run, context);
                    state.Children[i].Executed = true;
                }

                if (step.IsCompleted(run, context)) state.Children[i].Done = true;
                else allDone = false;                       // 还有分支没完成：本步整体挂起
            }

            return allDone;
        }
    }

    /// <summary>
    /// 重复步骤：把一组子步骤重复执行 N 轮（每轮内部仍按顺序 + 阻塞语义推进）。
    /// <para>提醒：如果组内全是立即步骤且 N 很大，会在<b>同一帧</b>里跑完 N 轮 ——
    /// 想让每轮占一帧，请在组内加一个 <c>WaitFrames(1)</c>。</para>
    /// </summary>
    internal sealed class RevRepeatStep : RevStepBase
    {
        private sealed class State
        {
            public RevStepCursor Cursor;
            public int Remaining;       // 还剩几轮
        }

        private readonly RevISequenceStep[] _steps;
        private readonly int _times;

        /// <inheritdoc/>
        public override string Name { get; }

        internal RevRepeatStep(string name, int times, RevISequenceStep[] steps)
        {
            Name = name;
            _times = times < 1 ? 1 : times;     // 构建期已校验，这里再兜一层
            _steps = steps;
        }

        /// <inheritdoc/>
        internal override void AssignSlots(ref int next)
        {
            base.AssignSlots(ref next);

            for (int i = 0; i < _steps.Length; i++)
            {
                if (_steps[i] is RevStepBase baseStep) baseStep.AssignSlots(ref next);
                else next++;
            }
        }

        /// <inheritdoc/>
        public override void Execute(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetOrCreateState<State>(run);
            state.Cursor ??= new RevStepCursor();
            state.Cursor.Reset();
            state.Remaining = _times;
        }

        /// <inheritdoc/>
        public override bool IsCompleted(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetState<State>(run);
            if (state?.Cursor == null) return true;

            while (true)
            {
                if (!state.Cursor.Advance(run, context, _steps)) return false;   // 本轮还没跑完

                state.Remaining--;
                if (state.Remaining <= 0) return true;                          // 全部轮次完成

                state.Cursor.Reset();                                           // 立刻开始下一轮
                // 继续 while：如果组内都是立即步骤，同帧内跑完多轮（符合"一帧连放"的整体语义）
            }
        }
    }
}
