// RevSequenceBuilder.Advanced.cs —— 【第 3 个要看的文件】进阶能力：用到哪个再查哪个
//
//   .DoBlocking("启动+盯结束", ctx => 启动(), ctx => 完成了)   自己盯一个跨帧过程（状态必须在 ctx 或服务里）
//   .Step(new MyStep())                                      自定义步骤（需要"本次运行私有状态"时用，见 RevStepBase.cs）
//   .WaitTask(ctx => 任务, "等异步任务")                       等 RevTask（资源加载 / 网络回包）
//   .Parallel("同时播", p => ...)                             组内并行，全部完成才算这步完成
//   .Repeat("闪三次", 3, r => ...)                            组内步骤重复 N 轮
//   .Sequence("先开箱", 别的清单)                              嵌套另一条序列（父被取消 → 子一起取消）
//   .Publish(new MyEvent(id))                                发强类型事件（串联别的序列 / 通知业务系统）
//   .OnCompleted / .OnCancelled                               生命周期回调（结束时的状态同步 / 上报）
//
//  说明：本文件与 RevSequenceBuilder.cs 是同一个 partial 类 —— 分开只是为了"小白不用一次看 20 个方法"。

using System;

namespace Revolution
{
    public sealed partial class RevSequenceBuilder
    {
        // ============================================================
        // 立即 / 阻塞步骤
        // ============================================================

        /// <summary>
        /// 阻塞步骤：执行一次 <paramref name="execute"/>，之后每帧问 <paramref name="isCompleted"/>，
        /// 返回 true 才放行下一步。
        /// <code>.DoBlocking("等加载", ctx =&gt; StartLoad(), ctx =&gt; LoadDone)</code>
        /// </summary>
        public RevSequenceBuilder DoBlocking(string name,
                                             Action<RevSequenceContext> execute,
                                             Func<RevSequenceContext, bool> isCompleted)
            => Add(new RevDelegateStep(name, execute, isCompleted));

        /// <summary>
        /// 加入一个<b>自定义步骤</b>（继承 <see cref="RevStepBase"/> 实现）。
        /// <para>需要"跨帧私有状态"时用它 —— 状态存在运行实例的状态槽里，天然按运行实例隔离；
        /// 而 <see cref="DoBlocking"/> 的谓词只能从 <c>context</c> 或业务服务取状态（闭包捕获构建期字段会并发互踩）。</para>
        /// <code>.Step(new RevWaitClickStep())</code>
        /// </summary>
        public RevSequenceBuilder Step(RevISequenceStep step) => Add(step);

        // ============================================================
        // 事件
        // ============================================================

        /// <summary>
        /// 发一个强类型事件（立即步骤）：用来串联别的序列或通知业务系统。
        /// <code>.Publish(new MyEvent(id: 1))</code>
        /// <para>★ 载荷是<b>构建期</b>给定的 → 只适合常量事件；要带运行期数据（ctx.Source / uid）请用
        /// <c>.Do("广播", ctx =&gt; ctx.Events.Publish(new MyEvent(ctx.Source, uid)))</c>。</para>
        /// </summary>
        public RevSequenceBuilder Publish<TEvent>(TEvent evt, string name = null)
            => Add(new RevPublishStep<TEvent>(name ?? $"发布事件 {typeof(TEvent).Name}", evt));

        // ============================================================
        // 异步等待
        // ============================================================

        /// <summary>
        /// 等一个异步任务（RevTask）完成 —— 把"等资源加载 / 等网络回包"接进序列。
        /// <para>★ 传的是<b>工厂</b>（<c>ctx =&gt; 任务</c>）而不是任务本身：任务在<b>运行时</b>才创建，
        /// 所以清单可以缓存复用、多个玩家并发触发也互不干扰。</para>
        /// <code>.WaitTask(ctx =&gt; ctx.Get&lt;IProtocolService&gt;()?.RequestAsync(uid), "等服务器回包")</code>
        /// </summary>
        public RevSequenceBuilder WaitTask(Func<RevSequenceContext, RevTask> taskFactory, string name = "等异步任务")
        {
            if (taskFactory == null)
                throw new ArgumentNullException(nameof(taskFactory), "WaitTask 需要一个 ctx => 任务 的工厂方法");

            return Add(new RevWaitTaskStep(name, taskFactory));
        }

        // ============================================================
        // 组合步骤
        // ============================================================

        /// <summary>
        /// 并行分组：组内步骤各自推进，全部完成才算这步完成。
        /// <code>.Parallel("同时播", p =&gt; p.Do("音乐", ...).Wait(1f))</code>
        /// </summary>
        public RevSequenceBuilder Parallel(string name, Action<RevSequenceBuilder> build)
        {
            RevSequenceBuilder sub = CreateSub(name);
            build?.Invoke(sub);

            if (sub._steps.Count == 0)
                throw new InvalidOperationException($"序列「{_name}」的并行分组「{name}」是空的 —— 并行组至少要有一个步骤");

            return Add(new RevParallelStep(name, sub._steps.ToArray()));
        }

        /// <summary>
        /// 重复分组：把组内步骤重复 N 轮。
        /// <code>.Repeat("闪三次", 3, r =&gt; r.Do("亮", ...).WaitFrames(6).Do("灭", ...))</code>
        /// </summary>
        public RevSequenceBuilder Repeat(string name, int times, Action<RevSequenceBuilder> build)
        {
            if (times < 1) throw new ArgumentOutOfRangeException(nameof(times), "重复次数必须 ≥ 1");

            RevSequenceBuilder sub = CreateSub(name);
            build?.Invoke(sub);

            if (sub._steps.Count == 0)
                throw new InvalidOperationException($"序列「{_name}」的重复分组「{name}」是空的 —— 重复组至少要有一个步骤");

            return Add(new RevRepeatStep(name, times, sub._steps.ToArray()));
        }

        /// <summary>
        /// 嵌套播放另一条已构建好的序列（父序列等它跑完；父序列被取消时它也会被取消）。
        /// <code>.Sequence("先开箱", ChestOpen)</code>
        /// </summary>
        public RevSequenceBuilder Sequence(string name, RevSequenceDefinition nested)
        {
            if (nested == null)
                throw new ArgumentNullException(nameof(nested), $"序列「{_name}」的嵌套步骤「{name}」需要一个已构建好的 Definition");

            return Add(new RevNestedStep(name, nested));
        }

        // ============================================================
        // 生命周期回调（构建期设定）
        // ============================================================

        /// <summary>正常跑完时回调</summary>
        public RevSequenceBuilder OnCompleted(Action<RevSequenceRun> callback)
        {
            _onCompleted = callback;
            return this;
        }

        /// <summary>被取消时回调（在收尾步骤之后触发）</summary>
        public RevSequenceBuilder OnCancelled(Action<RevSequenceRun> callback)
        {
            _onCancelled = callback;
            return this;
        }
    }
}
