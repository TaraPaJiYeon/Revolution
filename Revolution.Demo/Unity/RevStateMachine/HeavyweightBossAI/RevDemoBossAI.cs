// ============================================================
// RevDemoBossAI.cs —— 重量级状态机 Demo 的宿主（MonoBehaviour）
//
// 位置：Revolution.Demo\Unity\RevStateMachine\HeavyweightBossAI\
//
// 【怎么跑】场景里挂一个空物体，加本组件即可（不需要模型/动画，表现层用日志代替）。
//          想用真实距离，可以把玩家 Transform 拖到 _player 上；
//          不填就用按键模拟"玩家靠近/远离"。
//
// 【演示的按键】
//   [A] 玩家靠近 3 米          → 距离变小，Boss 会 待机 → 追击 → 战斗 自然流转
//   [D] 玩家远离 3 米          → 距离变大，Boss 会脱战回待机
//   [H] Boss 掉血 20%          → 战斗中血量 < 50% 时，会走【事务式切换】进二阶段
//                                 （血量归零则触发"全局规则"切死亡）
//   [K] 手动请求释放技能        → 宿主把请求转发给【技能状态机】（两台机器组合）
//   [R] 复活                   → 血量回满并切回待机
//   [L] 打印切换历史            → fsm.GetHistory()：能看到"什么时候、因为什么切走的"
//
// 【这个 Demo 想讲清的四件事】
//   ① 枚举 + 行为接口：状态只面向 IRevDemoBoss 编程，换实现不影响状态代码；
//   ② 三路驱动：Update / FixedUpdate / LateUpdate 各有分工（位移放 FixedUpdate）；
//   ③ 事务式切换：二阶段要等资源与演出（ChangeToAsync + PrepareEnterAsync）；
//   ④ 组合多台状态机：行为机 + 技能机各开一台，各自驱动，互不干扰 —— 而不是"分层状态机"。
// ============================================================
using UnityEngine;

namespace Revolution.Demo
{
    /// <summary>
    /// Boss AI 宿主：实现 <see cref="IRevDemoBoss"/>，持有并驱动两台状态机。
    /// </summary>
    public sealed class RevDemoBossAI : MonoBehaviour, IRevDemoBoss
    {
        [Header("可选：把玩家 Transform 拖进来；不填则用按键模拟距离")]
        [SerializeField] private Transform _player;

        [Header("演示参数")]
        [SerializeField] private float _simDistance = 10f;      // 模拟距离（没接玩家时生效）
        [SerializeField] private float _simMoveSpeed = 8f;      // 模拟靠近速度（米/秒）

        // ── 两台状态机 ────────────────────────────────────────
        private RevHeavyFsm<RevDemoBossStateType, IRevDemoBoss> _fsm;       // ① 行为/阶段
        private RevHeavyFsm<RevDemoBossSkillType, IRevDemoBoss> _skillFsm;  // ② 技能吟唱

        // ── 宿主数据（真实项目里这些来自属性系统/配置表）─────────
        private float _hp = 1f;
        private int _moveCallCount;         // 限流用：位移日志不要每帧刷
        private int _transitionCount;       // 切换次数（避免 OnGUI 每帧调 GetHistory 分配数组）
        private string _lastTransition = "（无）";

        // ============================================================
        // IRevDemoBoss：宿主行为（状态通过这些接口操作 Boss）
        // ============================================================

        /// <inheritdoc/>
        public float Hp => _hp;

        /// <inheritdoc/>
        public float DistanceToPlayer =>
            _player != null ? Vector3.Distance(transform.position, _player.position) : _simDistance;

        /// <inheritdoc/>
        public bool IsPlayerInAttackRange() => DistanceToPlayer <= 3f;

        /// <inheritdoc/>
        public void PlayAnimation(string clip) => Debug.Log($"[Boss·表现] 播放动画：{clip}");

        /// <inheritdoc/>
        public void MoveTowardsPlayer(float deltaTime)
        {
            // 有真实玩家就朝它走；否则改模拟距离（Demo 里不依赖场景布置）
            if (_player != null)
            {
                transform.position = Vector3.MoveTowards(transform.position, _player.position, deltaTime * 5f);
            }
            else
            {
                _simDistance = Mathf.Max(0f, _simDistance - deltaTime * _simMoveSpeed);
            }

            // 位移是高频行为，日志限流（每 40 次打一条），避免刷屏
            if (++_moveCallCount % 40 == 0)
                Debug.Log($"[Boss·表现] 朝玩家移动中，当前距离 {DistanceToPlayer:F1} 米");
        }

        /// <inheritdoc/>
        public void RequestCastSkill()
        {
            // ★ 组合多台的关键接线：状态只管"我要放技能"，宿主决定该由哪台机器处理
            if (_skillFsm.IsTransitioning || _skillFsm.CurrentStateType != RevDemoBossSkillType.None)
            {
                Debug.Log("[Boss] 技能机还在吟唱/冷却中 → 本次请求忽略");
                return;
            }

            Debug.Log("[Boss] 宿主收到技能请求 → 转发给【技能状态机】");
            _skillFsm.ChangeState(RevDemoBossSkillType.Casting, "战斗状态下请求释放技能");
        }

        // ============================================================
        // 生命周期：建机器、注册状态、驱动
        // ============================================================

        private void Awake()
        {
            // ① 把框架诊断日志接到 Unity 控制台（日志出口可替换 —— 真实项目里换成你们统一的日志系统）
            RevHeavyFsmLog.Sink = message => Debug.Log(message);

            // ② 主状态机：显式 new 注册（不是反射创建 → IL2CPP/AOT 安全，构造参数看得见）
            _fsm = new RevHeavyFsm<RevDemoBossStateType, IRevDemoBoss>(this);
            _fsm.AddState(RevDemoBossStateType.Idle,   new RevDemoBossIdleState(_fsm));
            _fsm.AddState(RevDemoBossStateType.Patrol, new RevDemoBossPatrolState(_fsm));
            _fsm.AddState(RevDemoBossStateType.Chase,  new RevDemoBossChaseState(_fsm));
            _fsm.AddState(RevDemoBossStateType.Combat, new RevDemoBossCombatState(_fsm));
            _fsm.AddState(RevDemoBossStateType.Phase2, new RevDemoBossPhase2State(_fsm));
            _fsm.AddState(RevDemoBossStateType.Dead,   new RevDemoBossDeadState(_fsm));

            // 事件解耦：外部系统（血条 UI/战斗统计/调试面板）订阅即可，不用状态机主动去找它们
            _fsm.StateChanged += (from, to, reason) =>
            {
                _transitionCount++;
                _lastTransition = $"{from} → {to}（{reason}）";
                Debug.Log($"[Boss] 状态变化：{_lastTransition}");
            };

            _fsm.ChangeState(RevDemoBossStateType.Idle, "初始化");

            // ③ 技能状态机（第二台）：与主机器完全独立，各自驱动
            _skillFsm = new RevHeavyFsm<RevDemoBossSkillType, IRevDemoBoss>(this);
            _skillFsm.AddState(RevDemoBossSkillType.None,     new RevDemoBossSkillNoneState(_skillFsm));
            _skillFsm.AddState(RevDemoBossSkillType.Casting,  new RevDemoBossCastingState(_skillFsm));
            _skillFsm.AddState(RevDemoBossSkillType.Cooldown, new RevDemoBossCooldownState(_skillFsm));
            _skillFsm.ChangeState(RevDemoBossSkillType.None, "初始化");
        }

        private void Update()
        {
            CheckGlobalRules();                             // 全局规则先判（死亡这类不该散落到每个状态里）

            _fsm.UpdateState(Time.deltaTime);               // ★ 三路驱动之一
            _skillFsm.UpdateState(Time.deltaTime);

            HandleDemoInput();
        }

        private void FixedUpdate()
        {
            // ★ 位移/物理类逻辑走固定步长（与渲染帧率解耦）
            _fsm.FixedUpdateState(Time.fixedDeltaTime);
            _skillFsm.FixedUpdateState(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            // ★ 表现纠偏（相机跟随、血条对齐）走 LateUpdate；本 Demo 无表现层，保留接线示意
            _fsm.LateUpdateState(Time.deltaTime);
        }

        private void OnDestroy()
        {
            // 释放：清空 + 禁用 + 放掉宿主引用（状态机的生命周期要跟宿主一致）
            _fsm.Dispose();
            _skillFsm.Dispose();
        }

        // ============================================================
        // 全局规则与"假 UI"输入
        // ============================================================

        /// <summary>
        /// 全局规则：死亡判断。
        /// <para>为什么集中在这里而不是写进每个状态：死亡是"任何状态下都成立"的规则，
        /// 散落到每个状态的 OnUpdate 里必然漏掉一个（这就是 AI 里最常见的"打死了还在动"）。</para>
        /// </summary>
        private void CheckGlobalRules()
        {
            if (_hp > 0f) return;
            if (_fsm.CurrentStateType == RevDemoBossStateType.Dead) return;

            Debug.Log("[Boss] 血量归零 → 触发全局规则：切死亡");
            _fsm.ChangeState(RevDemoBossStateType.Dead, "血量归零");
        }

        /// <summary>Demo 的按键输入（真实项目里对应：UI 按钮、技能输入、战斗系统回调）</summary>
        private void HandleDemoInput()
        {
            if (Input.GetKeyDown(KeyCode.A))
            {
                _simDistance = Mathf.Max(0f, _simDistance - 3f);
                Debug.Log($"[按键] 玩家靠近 → 距离 {DistanceToPlayer:F1} 米");
            }

            if (Input.GetKeyDown(KeyCode.D))
            {
                _simDistance += 3f;
                Debug.Log($"[按键] 玩家远离 → 距离 {DistanceToPlayer:F1} 米");
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                _hp = Mathf.Max(0f, _hp - 0.2f);
                Debug.Log($"[按键] Boss 掉血 → HP {_hp * 100f:F0}%");
            }

            if (Input.GetKeyDown(KeyCode.K))
            {
                Debug.Log("[按键] 手动请求释放技能");
                RequestCastSkill();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                _hp = 1f;
                Debug.Log("[按键] 复活：血量回满");
                if (_fsm.CurrentStateType == RevDemoBossStateType.Dead)
                    _fsm.ChangeState(RevDemoBossStateType.Idle, "复活");
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                // 切换历史：带"原因 + 时刻"，专治"AI 为什么突然切走了"这类问题
                Debug.Log($"[按键] 最近 {_transitionCount} 次切换：");
                foreach (RevHeavyFsmTransition<RevDemoBossStateType> t in _fsm.GetHistory())
                    Debug.Log($"    {t.Time:F2}s  {t}");
            }
        }

        /// <summary>左上角面板：状态机内部状态 + 两台机器一览</summary>
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 660f, 210f), GUI.skin.box);

            GUILayout.Label("=== 重量级·状态机 Demo：Boss AI（枚举 + 行为接口）===");
            GUILayout.Label($"HP：{_hp * 100f:F0}%      与玩家距离：{DistanceToPlayer:F1} 米      " +
                            $"在攻击范围(≤3m)：{IsPlayerInAttackRange()}");
            GUILayout.Label($"行为机当前状态：{_fsm.CurrentStateType}      进入本状态：{_fsm.StateTime:F1} 秒");
            GUILayout.Label($"事务式切换在途：{_fsm.IsTransitioning}      事务目标：{(_fsm.PendingStateType.HasValue ? _fsm.PendingStateType.Value.ToString() : "（无）")}");
            GUILayout.Label($"技能机当前状态：{_skillFsm.CurrentStateType}      切换次数：{_transitionCount}");
            GUILayout.Label($"最近一次切换：{_lastTransition}");
            GUILayout.Label("[A] 靠近  [D] 远离  [H] 掉血  [K] 放技能  [R] 复活  [L] 打印切换历史");

            GUILayout.EndArea();
        }
    }
}
