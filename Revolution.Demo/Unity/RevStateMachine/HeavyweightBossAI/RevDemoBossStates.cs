// ============================================================
// RevDemoBossStates.cs —— 重量级状态机的状态实现（Boss 行为 + 技能两台机器）
//
// 位置：Revolution.Demo\Unity\RevStateMachine\HeavyweightBossAI\
//
// 【状态里能用什么】（对比轻量级，重量级多出来的东西）
//   · Machine       —— 所属状态机（构造时传入，自动拿到）
//   · Owner         —— 宿主行为接口（强类型，不用转型）
//   · StateType     —— 本状态对应的枚举值（声明式，能拿去查表）
//   · StateTime     —— 进入本状态多久了（框架自动计时、切换清零）
//   · IsActive      —— 是否仍是当前状态（异步流程回来先验它，防止"已经切走了还在干活"）
//   · ChangeTo(枚举, 原因)   —— 带"原因"的切换（王者 2024 才补的能力：AI 为什么切走要能查）
//   · ChangeToAsync(枚举, 原因) —— 事务式切换（等双方 Prepare 完成才交割）
//   · OnFixedUpdate / OnLateUpdate —— 三路驱动，物理逻辑与表现纠偏各有归属
//
// 【七个回调全部是 virtual 空实现】所以每个状态只写自己关心的那几个：
//   OnEnter / OnExit / OnUpdate / OnFixedUpdate / OnLateUpdate
//   PrepareExitAsync / PrepareEnterAsync（默认直接完成 = 同步切换）
//
// 【本文件包含】
//   Boss 主状态机：Idle / Patrol / Chase / Combat / Phase2 / Dead
//   技能状态机：None / Casting / Cooldown（演示"组合多台"）
// ============================================================
using UnityEngine;

namespace Revolution.Demo
{
    // ============================================================
    // 一、Boss 主状态机的状态（键 = RevDemoBossStateType）
    // ============================================================

    /// <summary>待机：原地发呆；发现玩家去追，超时去巡逻。</summary>
    public sealed class RevDemoBossIdleState : RevHeavyFsmState<RevDemoBossStateType, IRevDemoBoss>
    {
        public override RevDemoBossStateType StateType => RevDemoBossStateType.Idle;

        public RevDemoBossIdleState(RevHeavyFsm<RevDemoBossStateType, IRevDemoBoss> machine) : base(machine) { }

        public override void OnEnter()
        {
            Owner.PlayAnimation("Idle");
            Debug.Log("[Boss] 进入【待机】");
        }

        public override void OnUpdate(float deltaTime)
        {
            // 判断优先级写在 OnUpdate 里（每个状态自己决定什么时候走）——
            // 这就是"状态机替代 if-else 堆叠"的地方：条件只在这个状态下成立
            if (Owner.IsPlayerInAttackRange())
            {
                ChangeTo(RevDemoBossStateType.Combat, "玩家已在攻击范围内");
                return;
            }

            if (Owner.DistanceToPlayer < 12f)
            {
                ChangeTo(RevDemoBossStateType.Chase, "发现玩家");
                return;
            }

            if (StateTime > 3f)
            {
                ChangeTo(RevDemoBossStateType.Patrol, "待机超时，开始巡逻");
            }
        }
    }

    /// <summary>巡逻：走一会儿；发现玩家去追，走太久回待机。</summary>
    public sealed class RevDemoBossPatrolState : RevHeavyFsmState<RevDemoBossStateType, IRevDemoBoss>
    {
        public override RevDemoBossStateType StateType => RevDemoBossStateType.Patrol;

        public RevDemoBossPatrolState(RevHeavyFsm<RevDemoBossStateType, IRevDemoBoss> machine) : base(machine) { }

        public override void OnEnter() => Debug.Log("[Boss] 进入【巡逻】");

        public override void OnFixedUpdate(float fixedDeltaTime)
        {
            // ★ 物理/位移类逻辑放 FixedUpdate（与帧率解耦，王者②③也是这么分的）
            Owner.MoveTowardsPlayer(fixedDeltaTime * 0.2f);     // 巡逻时慢速移动（示例里复用"靠近"动作）
        }

        public override void OnUpdate(float deltaTime)
        {
            if (Owner.DistanceToPlayer < 12f)
            {
                ChangeTo(RevDemoBossStateType.Chase, "巡逻中发现玩家");
                return;
            }

            if (StateTime > 6f)
            {
                ChangeTo(RevDemoBossStateType.Idle, "巡逻结束，回待机");
            }
        }
    }

    /// <summary>追击：快速靠近；进射程转战斗，跟丢回待机。</summary>
    public sealed class RevDemoBossChaseState : RevHeavyFsmState<RevDemoBossStateType, IRevDemoBoss>
    {
        public override RevDemoBossStateType StateType => RevDemoBossStateType.Chase;

        public RevDemoBossChaseState(RevHeavyFsm<RevDemoBossStateType, IRevDemoBoss> machine) : base(machine) { }

        public override void OnEnter()
        {
            Owner.PlayAnimation("Run");
            Debug.Log("[Boss] 进入【追击】");
        }

        public override void OnFixedUpdate(float fixedDeltaTime) => Owner.MoveTowardsPlayer(fixedDeltaTime);

        public override void OnUpdate(float deltaTime)
        {
            if (Owner.IsPlayerInAttackRange())
            {
                ChangeTo(RevDemoBossStateType.Combat, "进入攻击范围");
                return;
            }

            if (Owner.DistanceToPlayer > 18f)
            {
                ChangeTo(RevDemoBossStateType.Idle, "跟丢玩家");
            }
        }
    }

    /// <summary>
    /// 战斗：输出与技能；血量过半时走 <b>事务式切换</b>进二阶段，射程外转追击。
    /// <para>★ 这是重量级最典型的用法：常规切换用同步的 <c>ChangeTo</c>（必须马上切），
    /// 需要"等演出/等资源"的用 <c>ChangeToAsync</c>（事务式）。</para>
    /// </summary>
    public sealed class RevDemoBossCombatState : RevHeavyFsmState<RevDemoBossStateType, IRevDemoBoss>
    {
        private bool _skillRequested;       // 本次战斗里是否已经放过一次技能

        public override RevDemoBossStateType StateType => RevDemoBossStateType.Combat;

        public RevDemoBossCombatState(RevHeavyFsm<RevDemoBossStateType, IRevDemoBoss> machine) : base(machine) { }

        public override void OnEnter()
        {
            _skillRequested = false;
            Owner.PlayAnimation("Combat_Ready");
            Debug.Log("[Boss] 进入【战斗】");
        }

        public override void OnUpdate(float deltaTime)
        {
            // ① 血量低于 50% → 事务式切二阶段（等二阶段资源与变身演出就绪，期间战斗照常进行）
            if (Owner.Hp < 0.5f)
            {
                Debug.Log("[Boss] 血量过半 → 发起事务式切换（二阶段）");
                ChangeToAsync(RevDemoBossStateType.Phase2, "血量低于 50%").Forget();
                return;
            }

            // ② 打一会儿放个技能（由宿主转发给"技能状态机"，体现两台机器的组合）
            if (!_skillRequested && StateTime > 2f)
            {
                _skillRequested = true;
                Owner.RequestCastSkill();
            }

            // ③ 玩家跑出射程 → 转追击（同步切换：必须马上切）
            if (!Owner.IsPlayerInAttackRange())
            {
                ChangeTo(RevDemoBossStateType.Chase, "玩家跑出射程");
                return;
            }

            // ④ 打太久脱战（避免"永远卡在战斗里"这类 AI 僵死）
            if (StateTime > 12f)
            {
                ChangeTo(RevDemoBossStateType.Idle, "战斗超时，脱战");
            }
        }
    }

    /// <summary>
    /// 二阶段：★ 用 <see cref="PrepareEnterAsync"/> 演示事务式交割 ——
    /// 资源没加载完、变身演出没播完，就<b>不允许</b>交割到本状态。
    /// <para>对比：如果按老写法在 OnEnter 里"边演边加载"，玩家会看到"先变身再加载完"的穿帮；
    /// 事务式的价值就是把"准备"与"进入"分开。</para>
    /// </summary>
    public sealed class RevDemoBossPhase2State : RevHeavyFsmState<RevDemoBossStateType, IRevDemoBoss>
    {
        public override RevDemoBossStateType StateType => RevDemoBossStateType.Phase2;

        public RevDemoBossPhase2State(RevHeavyFsm<RevDemoBossStateType, IRevDemoBoss> machine) : base(machine) { }

        /// <summary>★ 事务式准备：加载二阶段资源 + 等变身演出（期间旧状态 Combat 仍在运行、仍在被驱动）</summary>
        public override async RevTask PrepareEnterAsync(RevCancellationToken token)
        {
            Debug.Log("[Boss] 二阶段进入准备：加载二阶段特效 + 变身演出（900ms）…");
            await RevTask.Delay(900);       // 真实项目：await ResManager.LoadAsync<GameObject>("Boss/Phase2Fx", token)
        }

        public override void OnEnter()
        {
            Owner.PlayAnimation("Phase2_Intro");
            Debug.Log("[Boss] 进入【二阶段】：攻击力与速度提升");
        }

        public override void OnUpdate(float deltaTime)
        {
            if (!Owner.IsPlayerInAttackRange())
            {
                ChangeTo(RevDemoBossStateType.Chase, "二阶段追击");
                return;
            }

            if (StateTime > 10f)
            {
                ChangeTo(RevDemoBossStateType.Idle, "二阶段结束，回待机");
            }
        }
    }

    /// <summary>死亡：什么都不做，等复活（由宿主在全局规则里切进来）。</summary>
    public sealed class RevDemoBossDeadState : RevHeavyFsmState<RevDemoBossStateType, IRevDemoBoss>
    {
        public override RevDemoBossStateType StateType => RevDemoBossStateType.Dead;

        public RevDemoBossDeadState(RevHeavyFsm<RevDemoBossStateType, IRevDemoBoss> machine) : base(machine) { }

        public override void OnEnter()
        {
            Owner.PlayAnimation("Dead");
            Debug.Log("[Boss] 进入【死亡】：停止一切行为");
        }

        public override void OnExit() => Debug.Log("[Boss] 离开【死亡】（复活）");

        // 死亡态不需要任何回调 —— 这正是"回调全 virtual 空实现"的意义：
        // 不用为了满足接口而写一堆空方法
    }

    // ============================================================
    // 二、技能状态机（第二台机器，键 = RevDemoBossSkillType）
    //    演示"组合多台"：阶段推进 与 技能吟唱 是两件独立的事，各开一台更清楚
    // ============================================================

    /// <summary>技能空闲：什么都不做，随时可以起手。</summary>
    public sealed class RevDemoBossSkillNoneState : RevHeavyFsmState<RevDemoBossSkillType, IRevDemoBoss>
    {
        public override RevDemoBossSkillType StateType => RevDemoBossSkillType.None;

        public RevDemoBossSkillNoneState(RevHeavyFsm<RevDemoBossSkillType, IRevDemoBoss> machine) : base(machine) { }
    }

    /// <summary>吟唱中：0.8 秒后转冷却。</summary>
    public sealed class RevDemoBossCastingState : RevHeavyFsmState<RevDemoBossSkillType, IRevDemoBoss>
    {
        public override RevDemoBossSkillType StateType => RevDemoBossSkillType.Casting;

        public RevDemoBossCastingState(RevHeavyFsm<RevDemoBossSkillType, IRevDemoBoss> machine) : base(machine) { }

        public override void OnEnter()
        {
            Owner.PlayAnimation("Cast_A");
            Debug.Log("[Boss·技能] 开始吟唱");
        }

        public override void OnUpdate(float deltaTime)
        {
            if (StateTime > 0.8f)
            {
                ChangeTo(RevDemoBossSkillType.Cooldown, "吟唱结束，技能生效");
            }
        }
    }

    /// <summary>冷却中：1.5 秒后回到空闲。</summary>
    public sealed class RevDemoBossCooldownState : RevHeavyFsmState<RevDemoBossSkillType, IRevDemoBoss>
    {
        public override RevDemoBossSkillType StateType => RevDemoBossSkillType.Cooldown;

        public RevDemoBossCooldownState(RevHeavyFsm<RevDemoBossSkillType, IRevDemoBoss> machine) : base(machine) { }

        public override void OnEnter() => Debug.Log("[Boss·技能] 进入冷却");

        public override void OnUpdate(float deltaTime)
        {
            if (StateTime > 1.5f)
            {
                ChangeTo(RevDemoBossSkillType.None, "冷却结束");
            }
        }
    }
}
