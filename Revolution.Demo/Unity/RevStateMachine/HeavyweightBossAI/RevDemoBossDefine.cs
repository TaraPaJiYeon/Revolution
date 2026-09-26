// ============================================================
// RevDemoBossDefine.cs —— 重量级【枚举 + 行为接口】Demo 的定义部分
//
// 位置：Revolution.Demo\Unity\RevStateMachine\HeavyweightBossAI\
//
// 【王者业务映射】
//   怪物 AI / Boss AI 是重量级状态机的正主（对应王者④ HLOD 的 FSM<T>：
//   枚举当键 + 事务式切换，也用在大怪物的阶段推进上）。
//   为什么这类业务要用"枚举 + 行为接口"这两样东西：
//     · 枚举：AI 要按状态做条件判断、查表（技能冷却表）、上报、存档 —— 用"类型"当键做不到这些；
//     · 行为接口：状态不依赖具体 MonoBehaviour，只面向行为编程 —— 于是状态逻辑
//       可以用一个假的实现做单元测试（不需要真场景、真模型）。
//
// 【本文件定义三样东西】
//   ① RevDemoBossStateType —— Boss 行为状态枚举（主状态机的键）
//   ② RevDemoBossSkillType —— 技能枚举（第二台状态机的键，演示"组合多台"）
//   ③ IRevDemoBoss —— 宿主行为接口（状态通过它操作 Boss 本体）
// ============================================================
namespace Revolution.Demo
{
    /// <summary>
    /// Boss 行为状态（主状态机的键）。
    /// <para>枚举当键的好处：可以写 <c>fsm.CurrentStateType == RevDemoBossStateType.Combat</c> 这类判断，
    /// 也能直接拿去查表 / 上报 / 存档。</para>
    /// </summary>
    public enum RevDemoBossStateType
    {
        /// <summary>待机：原地发呆，超时去巡逻</summary>
        Idle,

        /// <summary>巡逻：来回走动</summary>
        Patrol,

        /// <summary>追击：发现玩家，冲过去</summary>
        Chase,

        /// <summary>战斗：进射程打输出（血量过半时会事务式切二阶段）</summary>
        Combat,

        /// <summary>二阶段：需要加载资源 + 播变身演出（★ 事务式切换）</summary>
        Phase2,

        /// <summary>死亡：停止一切行为，等复活</summary>
        Dead,
    }

    /// <summary>
    /// 技能状态（第二台状态机的键）。
    /// <para>★ 这是本 Demo 想演示的一个重要取舍：**"组合多台状态机"而不是分层状态机**。
    /// Boss 的"阶段推进"和"技能吟唱"是两件独立的事，各开一台、各自驱动，比"父状态里套子状态机"清楚得多。</para>
    /// </summary>
    public enum RevDemoBossSkillType
    {
        /// <summary>空闲：可以起手</summary>
        None,

        /// <summary>吟唱中</summary>
        Casting,

        /// <summary>冷却中</summary>
        Cooldown,
    }

    /// <summary>
    /// Boss 宿主行为接口（对应原框架的 IFSMObj / 王者④的宿主抽象）。
    /// <para>状态只通过这些"行为"与 Boss 交互，因此：换一个 Boss 实现（真怪物 / 假实现测试）不需要改状态代码。</para>
    /// </summary>
    public interface IRevDemoBoss : RevIHeavyFsmOwner
    {
        /// <summary>当前血量比例（0~1）</summary>
        float Hp { get; }

        /// <summary>与玩家的距离（示例里可由按键模拟）</summary>
        float DistanceToPlayer { get; }

        /// <summary>玩家是否在攻击范围内</summary>
        bool IsPlayerInAttackRange();

        /// <summary>播放动画（表现层入口，状态只管"演什么"，不管"怎么演"）</summary>
        void PlayAnimation(string clip);

        /// <summary>朝玩家移动（物理类逻辑，放在 FixedUpdate 里调）</summary>
        void MoveTowardsPlayer(float deltaTime);

        /// <summary>请求宿主释放一次技能（由宿主转发给"技能状态机" —— 组合多台的关键接线）</summary>
        void RequestCastSkill();
    }
}
