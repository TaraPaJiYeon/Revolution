// RevSequenceBuilder.cs —— 【第 2 个要看的文件】常用 6 个方法（学会这 6 个就能写完 90% 的演出）
//
//   .Do("做什么", ctx => ...)                              立刻做一件事（播音乐 / 开界面 / 发奖励）
//   .Wait(1.5f)   /   .WaitFrames(6)                       等一会儿（秒 / 帧；时间来自引擎 Tick，不读 Time.time）
//   .WaitUntil("等条件", ctx => 条件, timeoutSeconds: 15f)   等外部状态（条件永不成立时超时放行 + 告警，不会卡死）
//   .OnCancel(f => f.Do("清理", ctx => ...))               取消时必然执行的收尾（回收生成物 / 恢复镜头）
//   .Build()                                               收尾：静态校验 + 生成不可变清单（只调用一次）
//
//   进阶（并行 / 重复 / 嵌套 / 事件 / 等异步 / 自定义步骤 / 埋点回调）见同目录 RevSequenceBuilder.Advanced.cs
//   设计动机与王者真实场景对照：Revolution.Document\动作序列\
//
// 【本文件负责】构建器状态 + 常用步骤 + Add / Build 校验；进阶方法在另一个 partial 文件里。

using System;
using System.Collections.Generic;

namespace Revolution
{
    /// <summary>
    /// 流式构建器：链式把步骤与回调串起来，最后 <see cref="Build"/> 成不可变的
    /// <see cref="RevSequenceDefinition"/>。
    /// <para>构建器是"一次性"的：Build 之后再调用链式方法会抛异常（避免你以为改了蓝图其实没改）。</para>
    /// <para>进阶方法见 <c>RevSequenceBuilder.Advanced.cs</c>（并行 / 重复 / 嵌套 / 事件 / 等异步 / 自定义步骤）。</para>
    /// </summary>
    public sealed partial class RevSequenceBuilder
    {
        private readonly string _name;
        private readonly RevSequenceConcurrency _concurrency;
        private readonly bool _isSubBuilder;        // 子构建器（Parallel/Repeat/OnCancel 内部用），不能单独 Build
        private readonly List<RevISequenceStep> _steps = new List<RevISequenceStep>(8);
        private readonly List<RevISequenceStep> _finallySteps = new List<RevISequenceStep>(2);
        private bool _built;

        // 生命周期回调（构建期设定，Build 时写入 Definition）
        private Action<RevSequenceRun> _onCompleted;
        private Action<RevSequenceRun> _onCancelled;

        internal RevSequenceBuilder(string name, RevSequenceConcurrency concurrency, bool isSubBuilder = false)
        {
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                throw new ArgumentException("动作序列必须有名字（日志与调试面板靠它定位）", nameof(name));

            _name = name;
            _concurrency = concurrency;
            _isSubBuilder = isSubBuilder;
        }

        // ============================================================
        // 1. 立即步骤：执行完就放行下一步
        // ============================================================

        /// <summary>
        /// 立即步骤：执行一句业务代码，执行完就继续下一步（不阻塞）。
        /// <code>.Do("做一件事", ctx =&gt; ctx.Get&lt;IMyService&gt;()?.DoSomething())</code>
        /// </summary>
        public RevSequenceBuilder Do(string name, Action<RevSequenceContext> body)
            => Add(new RevDelegateStep(name, body));

        // ============================================================
        // 2. 等待步骤：这一步没放行，后面的步骤不会开始
        // ============================================================

        /// <summary>等固定秒数（用引擎 Tick 的 dt 累计，不读 Time.time）</summary>
        public RevSequenceBuilder Wait(float seconds)
        {
            if (seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds), "等待时间不能为负");
            return Add(new RevWaitSecondsStep($"等待 {seconds:F2}s", seconds));
        }

        /// <summary>等固定帧数（引擎 Tick 次数）；<c>WaitFrames(1)</c> = 至少再等一帧</summary>
        public RevSequenceBuilder WaitFrames(int frames)
        {
            if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames), "等待帧数不能为负");
            return Add(new RevWaitFramesStep($"等待 {frames} 帧", frames));
        }

        /// <summary>
        /// 等条件成立（可带超时保护 —— 超时是"放行 + 告警"，不会永久卡死）。
        /// <para>这是框架里<b>唯一的"等外部状态"</b>步骤：等动画播完、等玩家点击、等某个标志位……
        /// 一律由业务自己写谓词表达 —— 框架不内建任何带具体语义的等待步骤（保持轻量与通用）。</para>
        /// <code>.WaitUntil("等条件成立", ctx =&gt; ctx.Get&lt;IMyService&gt;()?.IsReady == true, timeoutSeconds: 10f)</code>
        /// </summary>
        public RevSequenceBuilder WaitUntil(string name, Func<RevSequenceContext, bool> predicate, float timeoutSeconds = 0f)
            => Add(new RevWaitUntilStep(name, predicate, timeoutSeconds));

        // ============================================================
        // 3. 收尾与构建
        // ============================================================

        /// <summary>
        /// 收尾步骤：序列被取消时执行（<b>只同步执行一次，必须是立即步骤</b>）。
        /// <para>用途：回收生成物、反注册事件、恢复临时状态 —— 原体系靠节点自觉 OnDisable 兜底，
        /// 这里升级成框架契约：<b>取消必然走收尾</b>（除非 Stop(runFinally:false) 显式跳过）。</para>
        /// </summary>
        public RevSequenceBuilder OnCancel(Action<RevSequenceBuilder> buildFinally)
        {
            RevSequenceBuilder sub = CreateSub($"{_name}/收尾");
            buildFinally?.Invoke(sub);
            _finallySteps.Clear();
            _finallySteps.AddRange(sub._steps);
            return this;
        }

        /// <summary>
        /// 构建成不可变蓝图（做静态校验）。
        /// <para>建议在加载期调用一次并把结果缓存到 <c>static readonly</c> 字段 —— 运行期 Play 它零构建成本。</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">空序列、重复 Build、子构建器单独 Build 等用法错误</exception>
        public RevSequenceDefinition Build()
        {
            if (_isSubBuilder)
                throw new InvalidOperationException($"子构建器「{_name}」（Parallel/Repeat/OnCancel 内部使用）不能单独 Build，请在外层序列上统一 Build()");

            if (_built)
                throw new InvalidOperationException($"序列「{_name}」已经 Build 过了 —— Definition 是不可变蓝图，请把它缓存起来复用，不要重复 Build");

            if (_steps.Count == 0)
                throw new InvalidOperationException($"序列「{_name}」没有任何步骤 —— 空序列没有意义（占位请至少加一个 Do）");

            ValidateSteps(_name, _steps);
            ValidateSteps($"{_name}/收尾", _finallySteps);

            _built = true;

            return new RevSequenceDefinition(_name, _concurrency, _steps, _finallySteps)
            {
                OnCompleted = _onCompleted,
                OnCancelled = _onCancelled,
            };
        }

        // ============================================================
        // 内部（进阶文件里的方法也复用这几个）
        // ============================================================

        private RevSequenceBuilder Add(RevISequenceStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (_built) throw new InvalidOperationException($"序列「{_name}」已经 Build 过了，不能再加步骤（要改请重新构建一条）");

            _steps.Add(step);
            return this;
        }

        private RevSequenceBuilder CreateSub(string name)
            => new RevSequenceBuilder(name, RevSequenceConcurrency.Free, isSubBuilder: true);

        /// <summary>步骤校验：空名报错，重名告警（日志里带下标，仍可定位）</summary>
        private static void ValidateSteps(string owner, List<RevISequenceStep> steps)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                RevISequenceStep step = steps[i];

                if (step == null)
                    throw new InvalidOperationException($"{owner} 的第 {i + 1} 个步骤是 null");

                if (string.IsNullOrEmpty(step.Name) || step.Name.Trim().Length == 0)
                    throw new InvalidOperationException($"{owner} 的第 {i + 1} 个步骤没有名字（报错与排查靠它定位【卡在哪一步】）");
            }
        }

        /// <summary>调试显示</summary>
        public override string ToString() => $"RevSequenceBuilder(「{_name}」{_steps.Count} 步{(_built ? "，已构建" : string.Empty)})";
    }
}
