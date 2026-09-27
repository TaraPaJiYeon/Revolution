// RevWaitSteps.cs —— 三种等待步骤：等秒数 / 等帧数 / 等条件（可带超时保护）
// 【机制】挂起的步骤由引擎每帧调一次 IsCompleted，返回 true 才放行。
// 【为什么要超时】条件万一永不成立会永久卡住（原体系的典型事故）→ 超时 = 放行（静默放行，框架自身不打日志）。

using System;

namespace Revolution
{
    /// <summary>
    /// 等固定秒数（对应原体系"等待几秒"节点）。
    /// <para>用 <c>run.Elapsed</c> 判定，与引擎 Tick 的 dt 保持一致。</para>
    /// </summary>
    internal sealed class RevWaitSecondsStep : RevStepBase
    {
        private sealed class State
        {
            public float EndTime;       // 结束时刻（Run 内累计时间）
        }

        private readonly float _seconds;

        /// <inheritdoc/>
        public override string Name { get; }

        internal RevWaitSecondsStep(string name, float seconds)
        {
            Name = name;
            _seconds = seconds;
        }

        /// <inheritdoc/>
        public override void Execute(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetOrCreateState<State>(run);
            state.EndTime = run.Elapsed + _seconds;     // ★ 相对 Run 起点计时，不读 Time.time
        }

        /// <inheritdoc/>
        public override bool IsCompleted(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetState<State>(run);
            return state == null || run.Elapsed >= state.EndTime;   // 状态丢了就放行，避免卡死
        }
    }

    /// <summary>
    /// 等固定帧数（引擎 Tick 次数）。<c>WaitFrames(1)</c> = 至少再等一个 Tick 才放行。
    /// <para>用 <c>run.Runner.TickCount</c> 判定：帧数语义明确，不受 dt 大小影响。</para>
    /// </summary>
    internal sealed class RevWaitFramesStep : RevStepBase
    {
        private sealed class State
        {
            public long EndTick;        // 放行时刻（引擎 Tick 计数）
        }

        private readonly int _frames;

        /// <inheritdoc/>
        public override string Name { get; }

        internal RevWaitFramesStep(string name, int frames)
        {
            Name = name;
            _frames = frames;
        }

        /// <inheritdoc/>
        public override void Execute(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetOrCreateState<State>(run);
            state.EndTick = run.Runner.TickCount + _frames;     // TickCount 在本次 Tick 开头已 +1
        }

        /// <inheritdoc/>
        public override bool IsCompleted(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetState<State>(run);
            return state == null || run.Runner.TickCount >= state.EndTick;
        }
    }

    /// <summary>
    /// 等条件成立（可选超时保护）——最通用的阻塞步骤。
    /// <para>超时不是"失败"，而是"放行 + 告警"：宁可让流程继续，也不让一条序列永远卡住
    /// （原体系里"序列执行一半停了"最常见的根因就是某个节点永远不返回 true，见 05 文档的常见错误表）。</para>
    /// </summary>
    internal sealed class RevWaitUntilStep : RevStepBase
    {
        private sealed class State
        {
            public float StartTime;     // 本步开始时刻（用于超时判定）
        }

        private readonly Func<RevSequenceContext, bool> _predicate;
        private readonly float _timeoutSeconds;     // <= 0 表示不超时

        /// <inheritdoc/>
        public override string Name { get; }

        internal RevWaitUntilStep(string name, Func<RevSequenceContext, bool> predicate, float timeoutSeconds)
        {
            Name = name;
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            _timeoutSeconds = timeoutSeconds;
        }

        /// <inheritdoc/>
        public override void Execute(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetOrCreateState<State>(run);
            state.StartTime = run.Elapsed;
        }

        /// <inheritdoc/>
        public override bool IsCompleted(RevSequenceRun run, RevSequenceContext context)
        {
            if (_predicate(context)) return true;               // 条件成立：放行

            if (_timeoutSeconds <= 0f) return false;            // 没配超时：一直等

            // 超时 = 放行（避免序列永久卡死）。框架自身不打日志：要感知"某步超时了"，
            // 请在谓词里把业务自己的失败标志一起读出来，或用 .Do(...) 里自行上报。
            State state = GetState<State>(run);
            return state != null && run.Elapsed - state.StartTime >= _timeoutSeconds;
        }
    }
}
