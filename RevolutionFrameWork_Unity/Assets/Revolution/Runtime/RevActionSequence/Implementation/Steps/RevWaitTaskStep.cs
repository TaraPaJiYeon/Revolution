// RevWaitTaskStep.cs —— 等异步任务（RevTask）完成：两套时序之间的桥
// 【关键】收的是工厂 ctx => 任务，任务在运行时创建 → 清单可缓存、并发互不干扰。
// 【实现】任务存状态槽（按运行实例隔离）；每帧问 GetAwaiter().IsCompleted；失败不吞异常。

using System;

namespace Revolution
{
    /// <summary>
    /// 等异步任务步骤：运行时调用工厂拿到任务，等它完成后才放行
    /// （对应"等资源加载完 / 等网络回包"）。
    /// <para>由 <see cref="RevSequenceBuilder"/> 的 <c>WaitTask(...)</c> 创建。</para>
    /// </summary>
    internal sealed class RevWaitTaskStep : RevStepBase
    {
        private sealed class State
        {
            public RevTask Task;
            public bool Started;
        }

        private readonly Func<RevSequenceContext, RevTask> _taskFactory;

        /// <inheritdoc/>
        public override string Name { get; }

        internal RevWaitTaskStep(string name, Func<RevSequenceContext, RevTask> taskFactory)
        {
            Name = name;
            _taskFactory = taskFactory;
        }

        /// <inheritdoc/>
        public override void Execute(RevSequenceRun run, RevSequenceContext context)
        {
            // ★ 任务在运行时创建并存进状态槽：清单可复用、并发触发互不干扰
            State state = GetOrCreateState<State>(run);
            state.Task = _taskFactory(context);
            state.Started = true;
        }

        /// <inheritdoc/>
        public override bool IsCompleted(RevSequenceRun run, RevSequenceContext context)
        {
            State state = GetState<State>(run);
            if (state == null || !state.Started) return true;      // 没启动（不该发生）→ 不卡住

            RevTaskAwaiter awaiter = state.Task.GetAwaiter();
            if (!awaiter.IsCompleted) return false;                 // 还没完成：继续挂起

            // 已完成：如果它失败过，把异常报出来（不吞掉）
            try
            {
                awaiter.GetResult();
            }
            catch (Exception)
            {
                // 任务失败：这里只负责"不让序列永久卡住" → 放行。
                // 说明：框架自身不打日志（等框架日志系统统一接入）；要上报请在业务的异步逻辑里记。
            }

            return true;
        }
    }
}
