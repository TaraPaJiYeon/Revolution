// RevSequenceDefinition.cs —— 不可变蓝图（Build 的产物）
// 【职责】存步骤数组、并发策略、生命周期回调、状态槽数量。
// 【要点】构建一次即可无限次播放、可跨场景复用、可 static readonly 缓存。
// 【并发】并发策略按「同一条定义 + 同一个 Source」判定。

using System;
using System.Collections.Generic;

namespace Revolution
{
    /// <summary>
    /// 一条动作序列的蓝图（不可变）：步骤列表 + 并发策略 + 生命周期回调 + 收尾步骤。
    /// <para>由 <see cref="RevSequenceBuilder"/> 的 Build() 产出；用 <c>runner.Play(definition, ...)</c> 播放。</para>
    /// </summary>
    public sealed class RevSequenceDefinition
    {
        /// <summary>步骤数组（引擎热路径直接用数组遍历，不经过接口的 IReadOnlyList 包装）</summary>
        internal readonly RevISequenceStep[] StepArray;

        /// <summary>收尾步骤数组（取消时执行，必须都是立即步骤）</summary>
        internal readonly RevISequenceStep[] FinallyArray;

        /// <summary>序列名（日志、调试面板与 Build 校验用）</summary>
        public string Name { get; }

        /// <summary>并发策略（同源重复触发时怎么办，见 <see cref="RevSequenceConcurrency"/>）</summary>
        public RevSequenceConcurrency Concurrency { get; }

        /// <summary>步骤列表（只读视图；数组内部维护，请勿修改）</summary>
        public IReadOnlyList<RevISequenceStep> Steps => StepArray;

        /// <summary>收尾步骤列表（只读视图）</summary>
        public IReadOnlyList<RevISequenceStep> FinallySteps => FinallyArray;

        /// <summary>步骤数量</summary>
        public int StepCount => StepArray.Length;

        /// <summary>
        /// 这条序列需要多少状态槽（= 步骤树里所有步骤数，含组合步骤的子步骤）。
        /// <para>框架内部使用：运行实例按它开状态数组，业务不用关心。</para>
        /// </summary>
        internal int SlotCount { get; }

        /// <summary>正常跑完时回调</summary>
        public Action<RevSequenceRun> OnCompleted { get; internal set; }

        /// <summary>被取消时回调（<c>runFinally = true</c> 的取消：先跑收尾步骤，再回调本事件）</summary>
        public Action<RevSequenceRun> OnCancelled { get; internal set; }

        internal RevSequenceDefinition(string name,
                                       RevSequenceConcurrency concurrency,
                                       List<RevISequenceStep> steps,
                                       List<RevISequenceStep> finallySteps)
        {
            Name = name;
            Concurrency = concurrency;
            StepArray = steps.ToArray();
            FinallyArray = finallySteps != null ? finallySteps.ToArray() : Array.Empty<RevISequenceStep>();

            // 给步骤树分配状态槽：组合步骤（并行/重复）会递归给自己的子步骤继续分配
            int nextSlot = AssignSlots(StepArray, 0);
            SlotCount = AssignSlots(FinallyArray, nextSlot);
        }

        /// <summary>调试显示：例如 <c>「某条序列」(4 步/ReplacePerSource)</c></summary>
        public override string ToString() => $"「{Name}」({StepCount} 步/{Concurrency})";

        /// <summary>
        /// 递归分配状态槽。
        /// <para>派生自 <see cref="RevStepBase"/> 的步骤由基类递归分配（含其子步骤）；
        /// 直接实现接口的步骤也占一个槽 —— 保证各步骤的槽位绝不重叠。</para>
        /// </summary>
        private static int AssignSlots(RevISequenceStep[] steps, int nextSlot)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] is RevStepBase baseStep) baseStep.AssignSlots(ref nextSlot);
                else nextSlot++;
            }

            return nextSlot;
        }
    }
}
