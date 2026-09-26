// RevSequenceRunner.cs —— 序列引擎（框架的心脏）
// 【职责】Play / Stop / StopAll / Tick / 池化 / 并发策略 / 异常边界。
// 【主线】Play 建运行实例 → 每帧 Tick 推进 → 步骤放行即前进 → 完成或取消后走收尾。
// 【性能】只遍历活跃列表；运行实例与上下文池化 → 稳态 Tick 零分配；无 LINQ、无装箱。
// 【约束】时间只来自 Tick(dt)（不读 Time.time）；取消在「步骤边界」生效，不打断正在执行的步骤。
// 【可选】PlayAsync 依赖 RevTask（await 一条序列）；框架自身不打任何日志（等框架日志系统统一接入）。

using System;
using System.Collections.Generic;

namespace Revolution
{
    /// <summary>
    /// 动作序列执行引擎：宿主每帧调 <see cref="Tick"/>，其余交给它。
    /// <para>不是单例 —— 你可以给不同场景/不同系统各建一个（单测里更是要自己 new 一个，注入假服务与假时钟）。</para>
    /// </summary>
    public sealed class RevSequenceRunner : IDisposable
    {
        private readonly List<RevSequenceRun> _active = new List<RevSequenceRun>(16);   // 活跃序列（脏列表）
        private readonly Stack<RevSequenceRun> _pool = new Stack<RevSequenceRun>(16);   // 运行实例池
        private readonly Dictionary<long, RevTaskCompletionSource<bool>> _waiters = new Dictionary<long, RevTaskCompletionSource<bool>>(4);
        private readonly RevSequenceContext _template = new RevSequenceContext();       // "参数包"：读它的 Source/Services/Events
        private long _nextId = 1L;                                                      // 句柄 Id（单调递增，永不复用）
        private bool _disposed;
        /// <summary>默认服务容器（步骤里 <c>context.Get&lt;T&gt;()</c> 取的就是它；你也可以在 Play 时传别的）</summary>
        public RevSequenceServices Services { get; }

        /// <summary>默认事件总线（序列里 Publish 的事件发到这里；Bind 也监听它）</summary>
        public RevSequenceEventBus Events { get; }

        /// <summary>引擎累计 Tick 次数（框架内部："等帧数"步骤靠它判定；每帧 +1）</summary>
        internal long TickCount { get; private set; }

        /// <summary>
        /// 全局钩子：任意序列结束时触发（统计 / 性能采样 / 业务自己记录进度用）。
        /// <para>框架<b>不提供任何查询</b>（没有"现在几条在跑""卡在哪一步"这类 API）——
        /// 要观察序列，就在这个事件或定义的 <c>OnCompleted / OnCancelled</c> 里自己记。</para>
        /// </summary>
        public event Action<RevSequenceRun> RunFinished;

        /// <summary>建一个引擎</summary>
        /// <param name="services">服务容器（不传则自建一个空的）</param>
        /// <param name="events">事件总线（不传则自建一个）</param>
        public RevSequenceRunner(RevSequenceServices services = null, RevSequenceEventBus events = null)
        {
            Services = services ?? new RevSequenceServices();
            Events = events ?? new RevSequenceEventBus();
        }

        // ============================================================
        // 播放
        // ============================================================

        /// <summary>
        /// 播放一条序列（**唯一入口**）。
        /// <para><paramref name="source"/> = 触发者：用于判定并发策略（同源顶替 / 同源拒绝），
        /// 步骤里也能用 <c>ctx.Source</c> 读到它。</para>
        /// </summary>
        public RevSequenceHandle Play(RevSequenceDefinition definition, object source = null)
        {
            _template.Configure(source, Services, Events);
            return PlayInternal(definition);
        }

        /// <summary>
        /// 一步到位：构建并立刻播放（等价于 <c>Play(builder.Build(), source)</c>）。
        /// <para>适合"临时的一次性演出"（一句提示、一次反馈）。<b>高频触发请先构建 Definition 缓存起来</b>
        /// —— 每次 Build 都有分配，而且"同源策略"判定的是同一个定义实例。</para>
        /// </summary>
        public RevSequenceHandle Play(RevSequenceBuilder builder, object source = null)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder), "Play(builder) 收到了 null");

            return Play(builder.Build(), source);
        }

        /// <summary>
        /// 播放并返回可 await 的任务：正常跑完为 true；被取消为 false。
        /// <para>被并发策略拒绝时立即返回 false（句柄为空）。</para>
        /// <para>适用："等这条演出播完再做下一步"——但别忘了它要等引擎 Tick，别在 Tick 内部 await 造成自锁。</para>
        /// </summary>
        public RevTask<bool> PlayAsync(RevSequenceDefinition definition, object source = null)
        {
            RevSequenceHandle handle = Play(definition, source);
            if (!handle.IsAssigned) return RevTask<bool>.FromResult(false);

            RevTaskCompletionSource<bool> waiter = RevTask<bool>.CreateSource();
            _waiters[handle.Id] = waiter;               // 序列结束时（FinalizeRun）会完成它
            return waiter.Task;
        }

        /// <summary>
        /// 内部播放：并发策略判定 → 池里取运行实例 → 挂到活跃表 → 启动回调。
        /// </summary>
        private RevSequenceHandle PlayInternal(RevSequenceDefinition definition)
        {
            if (_disposed)
                throw new InvalidOperationException("引擎已释放（Dispose 之后不能再 Play）");

            if (definition == null)
                throw new ArgumentNullException(nameof(definition), "Play(definition) 收到了 null —— 是不是忘了 .Build()？");

            if (definition.StepCount == 0)
                throw new InvalidOperationException($"序列「{definition.Name}」没有任何步骤（构建期校验本该拦住它）");

            // ① 并发策略：只对"同一条定义 + 同一个触发者"生效
            //    注意：Play 不传 source（= null）时所有触发者被视为同一个 → 策略退化成"全局唯一"。
            if (definition.Concurrency != RevSequenceConcurrency.Free)
            {
                object source = _template.Source;

                for (int i = 0; i < _active.Count; i++)
                {
                    RevSequenceRun running = _active[i];
                    if (running.IsCancellationRequested) continue;                   // 已在收尾路上，不算"在跑"
                    if (!ReferenceEquals(running.Definition, definition)) continue;  // 不同定义互不影响
                    if (!ReferenceEquals(running.Source, source)) continue;          // 不同触发者互不影响

                    if (definition.Concurrency == RevSequenceConcurrency.RejectPerSource)
                        return default;                                              // 忽略：返回空句柄

                    // ReplacePerSource：顶替旧的（只打标记，真正收尾在 Tick 的步骤边界做）
                    running.IsCancellationRequested = true;
                }
            }

            // ② 取运行实例（池化：稳态下不产生分配）
            RevSequenceRun run = Rent();
            run.PrepareForReuse(this, _nextId++, definition, _template);
            _active.Add(run);

            return run.Handle;
        }

        /// <summary>从池里取一个运行实例（取不到就新建）</summary>
        private RevSequenceRun Rent() => _pool.Count > 0 ? _pool.Pop() : new RevSequenceRun();

        // ============================================================
        // 控制
        // ============================================================

        /// <summary>
        /// 取消一条序列。
        /// <para>取消在<b>下一个 Tick 的步骤边界</b>生效（不打断正在执行的步骤）；
        /// <paramref name="runFinally"/> = true 时先执行定义的 finally 步骤再做收尾回调。</para>
        /// </summary>
        /// <returns>true = 确实标记了一条正在跑的序列；false = 句柄已失效（什么都没做）</returns>
        public bool Stop(RevSequenceHandle handle, bool runFinally = true)
        {
            RevSequenceRun run = FindRun(handle.Id);
            if (run == null) return false;

            if (!runFinally) run.SkipFinally = true;
            run.IsCancellationRequested = true;         // 只是打标记：真正的收尾在 Tick 里做
            return true;
        }

        /// <summary>取运行实例（已结束或不存在返回 null）—— 句柄查询走它</summary>
        internal RevSequenceRun FindRun(long id)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Id == id) return _active[i];
            }

            return null;    // 活跃数量通常只有几十条，线性查找足够；上千条再改成字典索引
        }

        /// <summary>取消所有活跃序列（例如场景切换、宿主销毁）</summary>
        /// <param name="runFinally">是否执行各自的 finally 步骤</param>
        public void StopAll(bool runFinally = true)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (!runFinally) _active[i].SkipFinally = true;
                _active[i].IsCancellationRequested = true;
            }

            // 立刻收尾一次（不必等下一帧），保证 StopAll 之后活跃表就是空的
            Tick(0f);
        }

        // ============================================================
        // 驱动
        // ============================================================

        /// <summary>
        /// 每帧驱动：宿主在自己的 Update / LateUpdate / 固定帧里调（频率由你决定，见 07 文档 §6）。
        /// <para>传物理帧就是"与物理对齐"，传渲染帧就是"与表现对齐"，框架不预设。</para>
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_disposed) return;
            if (deltaTime < 0f) deltaTime = 0f;         // 防御：负 dt 会让"等秒数"倒着走

            TickCount++;

            // 倒序遍历：边遍历边移除是安全的；本帧新启动的序列（追加在尾部）从下一帧开始推进
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                RevSequenceRun run = _active[i];

                run.DeltaTime = deltaTime;
                run.Elapsed += deltaTime;

                // ① 取消请求：在步骤边界响应（不打断正在执行的步骤）
                if (run.IsCancellationRequested)
                {
                    RunFinallySteps(run);
                    FinalizeRun(run, RevSequenceStatus.Cancelled);
                    continue;
                }

                // ② 推进序列
                try
                {
                    Advance(run);
                }
                catch (Exception e)
                {
                    HandleFault(run, e);
                    continue;
                }

                // ③ 跑完了：收尾
                if (run.Status == RevSequenceStatus.Completed)
                    FinalizeRun(run, RevSequenceStatus.Completed);
            }
        }

        /// <summary>
        /// 推进一条序列：逐步骤 Execute → 问 IsCompleted → 通过就下一步。
        /// <para>— 立即步骤在同一帧内可以连续放行（不会一步一帧）；
        /// — 阻塞步骤（IsCompleted=false）就 <b>return</b>，停在当前步等下一帧。</para>
        /// </summary>
        private void Advance(RevSequenceRun run)
        {
            RevSequenceDefinition definition = run.Definition;
            RevISequenceStep[] steps = definition.StepArray;
            RevSequenceContext context = run.Context;

            while (run.StepIndex < steps.Length)
            {
                RevISequenceStep step = steps[run.StepIndex];

                if (!run.StepExecuted)
                {
                    step.Execute(run, context);          // 每步只执行一次
                    run.StepExecuted = true;
                }

                if (!step.IsCompleted(run, context)) return;    // ★ 阻塞：挂起在此，下一帧再问

                run.StepIndex++;
                run.StepExecuted = false;                // 放行下一步（同帧继续 while）
            }

            run.Status = RevSequenceStatus.Completed;
        }

        // ============================================================
        // 收尾 / 异常 / 池化
        // ============================================================

        /// <summary>
        /// 执行定义的 finally 步骤（取消时调用）。
        /// <para>约束：finally 步骤<b>只同步执行一次、必须立即完成</b> —— 里面不该出现阻塞步骤
        /// （阻塞步骤会永远等不到放行；收尾要"立刻生效"）。</para>
        /// </summary>
        private void RunFinallySteps(RevSequenceRun run)
        {
            if (run.SkipFinally) return;

            RevISequenceStep[] steps = run.Definition.FinallyArray;
            if (steps.Length == 0) return;

            try
            {
                for (int i = 0; i < steps.Length; i++)
                    steps[i].Execute(run, run.Context);
            }
            catch (Exception)
            {
                // 收尾步骤自己抛异常：吞掉 —— 取消路径上不能再抛二次异常（否则宿主收尾会被打断）。
                // 框架不打日志：要上报就在收尾步骤里自己 try/catch + 用自己的日志。
            }
        }

        /// <summary>
        /// 步骤抛异常：DEBUG 直接抛（尽早发现）；正式包终止这条序列、跑收尾后收掉。
        /// <para>正式包不往外抛，避免一个业务步骤的异常打断整个 Tick 循环里的其它序列。</para>
        /// </summary>
        private void HandleFault(RevSequenceRun run, Exception e)
        {
#if DEBUG
            throw new InvalidOperationException(
                $"[RevActionSequence] 序列「{run.Definition.Name}」第 {run.StepIndex + 1} 步「{run.CurrentStepName}」抛异常", e);
#else
            // 框架不打日志、也没有失败回调 —— 要上报请在步骤里自己 try/catch + 用自己的日志
            RunFinallySteps(run);
            FinalizeRun(run, RevSequenceStatus.Cancelled);
#endif
        }

        /// <summary>
        /// 结束一条序列：置状态 → 回调 → 唤醒 await 者 → 归还池。
        /// <para>幂等：不在活跃表里就直接返回（避免重复收尾）。</para>
        /// </summary>
        private void FinalizeRun(RevSequenceRun run, RevSequenceStatus status)
        {
            if (!_active.Remove(run)) return;

            run.Status = status;

            try
            {
                // ① 结束回调（回调自己抛异常时，下面的 finally 仍会保证收尾干净）
                try
                {
                    if (status == RevSequenceStatus.Completed) run.Definition.OnCompleted?.Invoke(run);
                    else run.Definition.OnCancelled?.Invoke(run);
                }
                catch (Exception)
                {
                    // 回调抛异常：吞掉（框架不打日志）—— 绝不能因此漏掉下面的收尾
                }

                // ② 全局钩子（统计 / 采样）
                RunFinished?.Invoke(run);

                // ③ 唤醒 PlayAsync 的等待者（正常完成 true / 取消 false）
                if (_waiters.TryGetValue(run.Id, out RevTaskCompletionSource<bool> waiter))
                {
                    _waiters.Remove(run.Id);
                    waiter.SetResult(status == RevSequenceStatus.Completed);
                }
            }
            finally
            {
                // ★ 无论如何都要做：取消未跑完的子序列 + 清状态 + 归还池
                run.StopChildHandles(this, runFinally: true);   // 父序列收尾：不留孤儿序列
                run.ReleaseState();                             // 先清状态再入池，避免下一条序列读到残留
                _pool.Push(run);
            }
        }

        // ============================================================
        // 清理
        // ============================================================

        /// <summary>清空引擎：取消所有序列、清空池与事件订阅（宿主销毁时调用）</summary>
        public void Clear()
        {
            StopAll(runFinally: false);
            _pool.Clear();
            _waiters.Clear();
            Events.Clear();
        }

        /// <summary>释放（等价于 <see cref="Clear"/> + 标脏：后续 Tick 不再生效）</summary>
        public void Dispose()
        {
            if (_disposed) return;
            Clear();
            _disposed = true;
        }

        /// <summary>调试显示：例如 <c>RevSequenceRunner(活跃 3 / 池 2)</c></summary>
        public override string ToString() => $"RevSequenceRunner(活跃 {_active.Count} / 池 {_pool.Count})";
    }
}
