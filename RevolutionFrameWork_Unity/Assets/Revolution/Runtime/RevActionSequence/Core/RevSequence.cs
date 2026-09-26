// RevSequence.cs —— ★ 小白从这里开始（3 分钟就能跑起来）
//
// ┌─ 三步走 ──────────────────────────────────────────────────────────────────────────┐
// │ 1) 构建清单（只建一次，加载期缓存到 static readonly）                               │
// │    static readonly RevSequenceDefinition ChestOpen = RevSequence                   │
// │        .Create("宝箱开箱")                                                          │
// │        .Do("播出现音效", ctx => ctx.Get<ISoundService>()?.Play("Play_Box_Appear"))   │
// │        .WaitUntil("等玩家点击", ctx => ctx.Get<IInputService>()?.Clicked == true, 15f)│
// │        .Do("开门", ctx => ctx.Get<IDoorService>()?.Open())                          │
// │        .Build();                                                                    │
// │                                                                                     │
// │ 2) 播放（Unity 里一行，零配置：框架自动每帧驱动）                                     │
// │    RevSequencePlayer.Default.Play(ChestOpen, source: 玩家对象);                       │
// │                                                                                     │
// │ 3) 取消（句柄是唯一入口；被取消时必然执行 .OnCancel 里写的收尾）                       │
// │    handle.Stop();                                                                   │
// └─────────────────────────────────────────────────────────────────────────────────────┘
//
// 【接下来看哪】（只有前两个是你必须看的）
//   ① 本文件                            构建入口 + 三条铁律
//   ② RevSequenceBuilder.cs             常用 6 个方法：Do / Wait / WaitFrames / WaitUntil / OnCancel / Build
//   ③ RevSequenceBuilder.Advanced.cs    需要时才看：并行 / 重复 / 嵌套 / 事件 / 等异步 / 自定义步骤 / 埋点
//   ④ RevStepBase.cs                    只有"需要跨帧私有状态"时才看
//   其余都不用读：Engine\ = 引擎内部（跑得动就行），Steps\ = 内置步骤库，Extras\ = 可选能力，Unity\ = 宿主适配
//
// 【三条铁律】
//   ① 清单只 Build 一次并缓存 —— 别在运行期反复构建
//   ② 状态别存在构建期字段里 —— 用业务服务，或用自定义步骤的状态槽（否则两个玩家同时触发会互踩）
//   ③ 占用了什么（生成的物件 / 推近的镜头），就在 .OnCancel 里还回去
//
// 完整教程：Revolution.Document\动作序列\（使用说明 = 上手；使用指南 = 设计与王者真实场景对照）

namespace Revolution
{
    /// <summary>
    /// 动作序列的构建入口 —— 唯一必须先看懂的类。
    /// <code>
    /// // ① 构建一次（加载期；Definition 是不可变蓝图，可以反复播、可以跨场景复用）
    /// static readonly RevSequenceDefinition Flow = RevSequence
    ///     .Create("一段流程")
    ///     .Do("做一件事", ctx =&gt; ctx.Get&lt;IMyService&gt;()?.Do())
    ///     .Wait(0.5f)
    ///     .Build();
    ///
    /// // ② 播放（运行期；零构建成本）
    /// RevSequencePlayer.Default.Play(Flow, source: gameObject);
    /// </code>
    /// </summary>
    public static class RevSequence
    {
        /// <summary>
        /// 开始构建一条序列。
        /// </summary>
        /// <param name="name">序列名（日志与调试面板靠它定位；不能为空）</param>
        /// <param name="concurrency">
        /// 并发策略：同一个触发者重复触发这条序列时怎么办。
        /// 默认 <see cref="RevSequenceConcurrency.Free"/>（各跑各的）；防连点用 <see cref="RevSequenceConcurrency.RejectPerSource"/>。
        /// </param>
        public static RevSequenceBuilder Create(string name, RevSequenceConcurrency concurrency = RevSequenceConcurrency.Free)
            => new RevSequenceBuilder(name, concurrency);
    }
}
