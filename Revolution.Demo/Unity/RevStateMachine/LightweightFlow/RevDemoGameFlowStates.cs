// ============================================================
// RevDemoGameFlowStates.cs —— 轻量级【单状态机】Demo：游戏主流程的 5 个状态
//
// 位置：Revolution.Demo\Unity\RevStateMachine\LightweightFlow\
//
// 【王者业务映射】
//   王者①的 StackStateMachine（非泛型栈式）有 8+ 业务在用，其中一条就是
//   **"游戏主流程"**：登录 → 大厅 → 匹配 → 战斗加载 → 战斗。
//   这条链路的两个特点，正好覆盖轻量级单状态机的全部能力：
//     · 大部分切换是同步的（点按钮立刻切）→ ChangeTo
//     · "进战斗"要等资源就绪才允许交割     → ChangeToAsync（事务式）
//     · 离开加载态时要判断"本来要去哪"     → PendingState（= 王者①的 tarState）
//
// 【本文件包含 5 个状态】
//   RevDemoLoginState        登录（异步登录流程）
//   RevDemoLobbyState        大厅（同步切换的"稳定态"）
//   RevDemoMatchingState     匹配中（异步等待 + ★ 回来先确认"我还活着"）
//   RevDemoBattleLoadingState 战斗加载（★ 事务式切换 + ★ tarState 兜底释放资源）
//   RevDemoBattleState       战斗（★ 实现 PrepareEnterAsync：进场演出播完才交割）
//
// 【为什么状态要拿状态机引用】
//   状态需要"自己发起切换"（比如匹配成功后自动进战斗）。
//   轻量级刻意不做注册表，所以状态机引用由业务在构造时传进来 ——
//   这与王者①里状态持有 stateMachine 的做法一致。
// ============================================================
using UnityEngine;

namespace Revolution.Demo
{
    /// <summary>
    /// 登录状态：进入后启动一次异步登录流程，成功后自动进大厅。
    /// <para>要点：异步流程回来时必须确认"我还是当前状态"，否则会出现
    /// "玩家已经手动退回登录页了，旧的登录流程还在把我推进大厅"。</para>
    /// </summary>
    public sealed class RevDemoLoginState : RevLightStateBase
    {
        private readonly RevLightStateMachine<RevLightStateBase> _sm;

        public RevDemoLoginState(RevLightStateMachine<RevLightStateBase> sm) => _sm = sm;

        public override void OnEnter()
        {
            Debug.Log("[主流程] 进入【登录】：开始异步登录（模拟 1 秒）");
            LoginAsync().Forget();      // Forget：显式表示"发起后不等它"（比 async void 更清楚）
        }

        public override void OnExit() => Debug.Log("[主流程] 离开【登录】");

        private async RevTask LoginAsync()
        {
            await RevTask.Delay(1000);                                   // 模拟登录请求
            if (!ReferenceEquals(_sm.CurrentState, this)) return;        // ★ 已经不在了 → 本次结果作废

            Debug.Log("[主流程] 登录成功 → 切到大厅");
            _sm.ChangeTo<RevDemoLobbyState>();                           // 按类型切（大厅是注册过的实例）
        }
    }

    /// <summary>
    /// 大厅状态：一个稳定的"常驻态"，玩家在大厅停留时只有它的 OnUpdate 被驱动。
    /// <para>它是流程的"回退点"：取消匹配、战斗结束都会切回这里。</para>
    /// </summary>
    public sealed class RevDemoLobbyState : RevLightStateBase
    {
        private readonly RevLightStateMachine<RevLightStateBase> _sm;
        private bool _idleTipShown;     // 保证"待太久"的提示只打一次（避免每帧刷屏）

        public RevDemoLobbyState(RevLightStateMachine<RevLightStateBase> sm) => _sm = sm;

        public override void OnEnter()
        {
            _idleTipShown = false;
            Debug.Log("[主流程] 进入【大厅】：可以开始匹配了");
        }

        public override void OnUpdate(float deltaTime)
        {
            // 大厅在 Demo 里不做每帧逻辑，只用 StateTime 演示"进入本状态多久了"。
            // ★ 注意：轻量级的 StateTime 在【状态机】上（_sm.StateTime），不在状态基类上 ——
            //   因为轻量级状态不强制持有状态机引用（重量级的 StateTime 才是状态基类的 protected 属性）。
            if (!_idleTipShown && _sm.StateTime > 5f)
            {
                _idleTipShown = true;
                Debug.Log($"[主流程] 在大厅已经 {_sm.StateTime:F1} 秒 —— StateTime 就是干这个的（切换时自动清零）");
            }
        }

        public override void OnExit() => Debug.Log("[主流程] 离开【大厅】");
    }

    /// <summary>
    /// 匹配状态：异步等待匹配结果，成功后自动切到战斗加载。
    /// <para>★ 这是"异步流程 + 状态机"最容易出错的地方：await 回来后必须先确认
    /// 自己还是当前状态（玩家可能已经点了取消），否则会把玩家"强行拉进战斗"。</para>
    /// </summary>
    public sealed class RevDemoMatchingState : RevLightStateBase
    {
        private readonly RevLightStateMachine<RevLightStateBase> _sm;

        public RevDemoMatchingState(RevLightStateMachine<RevLightStateBase> sm) => _sm = sm;

        public override void OnEnter()
        {
            Debug.Log("[主流程] 进入【匹配中】：开始匹配（约 2 秒，期间可按 [2] 取消）");
            MatchAsync().Forget();
        }

        public override void OnExit() => Debug.Log("[主流程] 离开【匹配中】（被取消或已匹配成功）");

        private async RevTask MatchAsync()
        {
            await RevTask.Delay(2000);                               // 模拟匹配耗时

            // ★ 关键校验：期间玩家可能已经取消（切回大厅），那就不能继续推进流程
            if (!ReferenceEquals(_sm.CurrentState, this))
            {
                Debug.Log("[主流程] 匹配结果回来时已不在匹配态 → 丢弃这次结果（避免把玩家强行拉进战斗）");
                return;
            }

            Debug.Log("[主流程] 匹配成功 → 切到【战斗加载】");
            _sm.ChangeTo<RevDemoBattleLoadingState>();
        }
    }

    /// <summary>
    /// 战斗加载状态：进读条 → 等资源 → 事务式切到战斗。
    /// <para>★★ 本 Demo 最值钱的两个点都在这个类：</para>
    /// <para>① <b>事务式切换</b>：<c>await ChangeToAsync&lt;RevDemoBattleState&gt;()</c> ——
    /// 只有当目标状态自己说"我准备好了"（PrepareEnterAsync 完成）才交割，
    /// 期间读条界面照常运行、玩家照常能点取消。</para>
    /// <para>② <b>tarState 兜底</b>：离开本状态时用 <see cref="RevLightStateMachine{T}.PendingState"/>
    /// 判断"本来要去哪" —— 目标不是战斗，说明是被取消了，必须把已经加载一半的战斗资源放掉。
    /// 这是王者 LoadingState 的真实用法。</para>
    /// </summary>
    public sealed class RevDemoBattleLoadingState : RevLightStateBase
    {
        private readonly RevLightStateMachine<RevLightStateBase> _sm;

        public RevDemoBattleLoadingState(RevLightStateMachine<RevLightStateBase> sm) => _sm = sm;

        public override void OnEnter()
        {
            Debug.Log("[主流程] 进入【战斗加载】：读条开始（按 [2] 可在读条中取消，观察资源兜底释放）");
            LoadThenEnterBattleAsync().Forget();
        }

        public override void OnExit()
        {
            // ★★ tarState 用法（王者 LoadingState 的真实逻辑）
            //   PendingState 的含义是"当前事务要去哪"：
            //     · 若本次离开是"异步交割进战斗"→ 此刻 PendingState 就是那个战斗态 → 资源保留；
            //     · 若本次离开是"玩家取消/切回大厅"（同步切换会先作废事务，PendingState 被清空）
            //       → 说明战斗去不成了 → ★ 兜底把战斗资源释放掉，别让它白占内存。
            if (_sm.PendingState is RevDemoBattleState)
            {
                Debug.Log("[主流程] 离开【战斗加载】，目标就是战斗 → 资源保留给战斗使用");
            }
            else
            {
                Debug.Log("[主流程] 离开【战斗加载】，但目标不是战斗 → ★ 兜底释放战斗资源");
                RevDemoBattleAssetService.Release();
            }
        }

        private async RevTask LoadThenEnterBattleAsync()
        {
            // ① 等资源（真实项目：await ResManager.LoadAsync<GameObject>("Battle/Scene", token)）
            await RevDemoBattleAssetService.LoadAsync();

            // ② 加载完先确认还在读条态（玩家可能已经取消了）
            if (!ReferenceEquals(_sm.CurrentState, this))
            {
                Debug.Log("[主流程] 资源加载完成时已离开读条态 → 不再切战斗（资源由 OnExit 兜底释放）");
                return;
            }

            // ③ 事务式切换：等战斗态 PrepareEnterAsync（进场演出）完成才交割
            Debug.Log("[主流程] 资源就绪 → 发起事务式切换（等战斗态准备好）");
            await _sm.ChangeToAsync<RevDemoBattleState>();

            // ④ await 回来必须确认是否真的切过去了（被更新的请求取代时状态不变，且不抛异常）
            if (!_sm.Is<RevDemoBattleState>())
                Debug.Log("[主流程] 本次切换被取代/取消 → 仍留在读条态，资源交给 OnExit 判断");
        }
    }

    /// <summary>
    /// 战斗状态：演示"状态实现异步准备"（<see cref="RevILightAsyncState"/>）。
    /// <para>为什么要继承 <see cref="RevLightAsyncStateBase"/>：只有实现 RevILightAsyncState
    /// 的状态才会让状态机走"事务式交割"这条路；否则切换是立刻发生的。</para>
    /// <para>本状态带构造参数（战斗模式），所以它会被<b>工厂注册</b>：
    /// <c>sm.Register&lt;RevDemoBattleState&gt;(() =&gt; new RevDemoBattleState(sm, mode))</c>。</para>
    /// </summary>
    public sealed class RevDemoBattleState : RevLightAsyncStateBase
    {
        private readonly RevLightStateMachine<RevLightStateBase> _sm;
        private readonly string _mode;      // 本局模式（5v5 / 1v1）—— 说明"为什么用工厂注册"

        public RevDemoBattleState(RevLightStateMachine<RevLightStateBase> sm, string mode)
        {
            _sm = sm;
            _mode = mode;
        }

        /// <summary>★ 事务式准备：进场演出播完才允许交割（期间读条页仍在运行）</summary>
        public override async RevTask PrepareEnterAsync(RevCancellationToken token)
        {
            Debug.Log($"[战斗] 进入准备：播放进场演出（{_mode}）…");
            await RevTask.Delay(600);        // 真实项目里这里可能是"等动画播完 / 等资源到位"
        }

        public override void OnEnter() => Debug.Log($"[战斗] 进入【战斗】（{_mode}）：开打！3 秒后自动结束");

        public override void OnUpdate(float deltaTime)
        {
            if (_sm.StateTime > 3f)          // 用状态机的 StateTime 做"本状态内计时"，不用自己记时间
            {
                Debug.Log("[战斗] 战斗结束 → 回大厅");
                _sm.ChangeTo<RevDemoLobbyState>();
            }
        }

        public override void OnExit()
        {
            Debug.Log("[战斗] 离开【战斗】：释放战斗资源");
            RevDemoBattleAssetService.Release();      // 正常退出战斗 → 正常释放
        }
    }
}
