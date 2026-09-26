// RevStepBase.cs —— 自定义步骤基类（需要「本次运行私有状态」时用）
// 【三件事】① 内部类 State ② Execute 里 GetOrCreateState<T>(run) ③ IsCompleted 里判断。
// 【铁律】状态存槽里、时间从 run 取 —— 在 lambda 里闭包捕获字段会被并发触发互踩。

namespace Revolution
{
    /// <summary>
    /// 动作序列步骤基类：提供"按运行实例隔离的状态槽"与"子步骤槽位分配"两件事。
    /// <para>不想继承它也行（直接实现 <see cref="RevISequenceStep"/>），但那样就得自己找地方放状态。</para>
    /// </summary>
    public abstract class RevStepBase : RevISequenceStep
    {
        private int _slotIndex = -1;        // 构建期由 RevSequenceDefinition 分配

        /// <summary>步骤名（用于日志与调试面板：告诉你卡在哪一步）</summary>
        public abstract string Name { get; }

        /// <summary>执行本步（每步只调用一次）</summary>
        public abstract void Execute(RevSequenceRun run, RevSequenceContext context);

        /// <summary>
        /// 本步完成了吗（每帧问一次）。默认 <c>true</c> = <b>立即步骤</b>；
        /// 阻塞步骤请重写它（返回 false 会让序列挂起在此步）。
        /// </summary>
        public virtual bool IsCompleted(RevSequenceRun run, RevSequenceContext context) => true;

        /// <summary>本步骤占用的状态槽号（引擎构建期分配；-1 = 尚未分配）</summary>
        internal int SlotIndex => _slotIndex;

        /// <summary>
        /// 分配状态槽（构建期调用一次）。
        /// <para>组合步骤（并行/重复）重写本方法时，<b>必须先调 base.AssignSlots(ref next)</b>，
        /// 再给子步骤分配 —— 否则子步骤会占用父步骤的槽位。</para>
        /// </summary>
        internal virtual void AssignSlots(ref int next) => _slotIndex = next++;

        /// <summary>读本步骤在"本次运行"中的状态（未写过则为 null）</summary>
        protected T GetState<T>(RevSequenceRun run) where T : class => run.GetStepState<T>(_slotIndex);

        /// <summary>写本步骤在"本次运行"中的状态</summary>
        protected void SetState(RevSequenceRun run, object state) => run.SetStepState(_slotIndex, state);

        /// <summary>取状态，取不到就创建并写入（省掉每次判空；状态对象随运行实例池化复用）</summary>
        protected T GetOrCreateState<T>(RevSequenceRun run) where T : class, new()
        {
            T state = run.GetStepState<T>(_slotIndex);
            if (state == null)
            {
                state = new T();
                run.SetStepState(_slotIndex, state);
            }

            return state;
        }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }
}
