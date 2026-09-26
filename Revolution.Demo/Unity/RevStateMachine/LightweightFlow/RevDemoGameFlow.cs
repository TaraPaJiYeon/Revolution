// ============================================================
// RevDemoGameFlow.cs —— 轻量级【单状态机】Demo 的驱动（MonoBehaviour）
//
// 位置：Revolution.Demo\Unity\RevStateMachine\LightweightFlow\
//
// 【怎么跑】
//   1. 把 RevStateMachine 整个文件夹放进 Unity 工程的 Assets 下（或任意位置，随工程编译）；
//   2. 场景里新建一个空物体，挂上本组件，Play 即可（不需要任何 UI/Prefab）；
//   3. 左上角会显示实时状态，Console 里能看到完整流转日志。
//
// 【演示的按键】
//   [1] 开始匹配            → ChangeTo<RevDemoMatchingState>()（按类型切注册状态）
//   [2] 取消 / 回大厅        → ChangeTo<RevDemoLobbyState>()
//                              ★ 在读条期间按它，能看到"兜底释放战斗资源"（tarState 用法）
//   [3] 切换下一局模式        → 5v5 ⇄ 1v1（下一次进战斗由【工厂注册】给出新实例）
//   [4] 重置统计并回登录      → 重新走一遍流程
//
// 【这个 Demo 覆盖了轻量级单状态机的全部能力】
//   · 注册（实例 + 工厂）与按类型切换
//   · 同步切换 / 事务式异步切换（ChangeToAsync）
//   · PendingState（= 王者①的 tarState）做"异常离开"的兜底判断
//   · StateTime 计时、StateChanged 事件、每帧只驱动当前状态
// ============================================================
using UnityEngine;

namespace Revolution.Demo
{
    /// <summary>
    /// 游戏主流程 Demo：登录 → 大厅 → 匹配 → 战斗加载 → 战斗 → 回大厅。
    /// <para>挂到场景任意物体上即可运行，操作说明见 OnGUI 提示。</para>
    /// </summary>
    public sealed class RevDemoGameFlow : MonoBehaviour
    {
        private RevLightStateMachine<RevLightStateBase> _sm;
        private string _nextBattleMode = "5v5";     // 进战斗时用（演示"工厂注册"为什么存在）

        private void Awake()
        {
            // ① 建状态机（T 选 RevLightStateBase：所有轻量状态都能注册进来）
            _sm = new RevLightStateMachine<RevLightStateBase>();

            // ② 订阅切换事件（王者⑤"事件解耦"：状态机广播，外部系统各自响应）
            _sm.StateChanged += (prev, next) =>
                Debug.Log($"[主流程] 状态变化：{Name(prev)} → {Name(next)}");

            // ③ 固定状态 → 注册【实例】（复用同一个对象：切到"已经是当前状态"时会被自动跳过）
            _sm.Register(new RevDemoLoginState(_sm));
            _sm.Register(new RevDemoLobbyState(_sm));
            _sm.Register(new RevDemoMatchingState(_sm));
            _sm.Register(new RevDemoBattleLoadingState(_sm));

            // ④ 带参数的状态 → 注册【工厂】（每局给新实例：战斗模式可能不同）
            //    注意：工厂状态每次都是新对象，所以"切到同一个战斗态"不会被判重跳过 —— 这正是我们想要的
            _sm.Register<RevDemoBattleState>(() => new RevDemoBattleState(_sm, _nextBattleMode));

            // ⑤ 启动流程：直接按类型切到登录态
            _sm.ChangeTo<RevDemoLoginState>();
        }

        private void Update()
        {
            // ★ 轻量级状态机的唯一驱动入口：每帧把 deltaTime 交给它，它只驱动"当前状态"
            _sm.Update(Time.deltaTime);

            // 以下按键是"假 UI"：真实项目里这些都是按钮/事件回调，调用的是同一批 API
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                Debug.Log("[按键] 开始匹配");
                _sm.ChangeTo<RevDemoMatchingState>();
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                Debug.Log("[按键] 取消 / 回大厅（若正在读条，注意看资源兜底释放）");
                _sm.ChangeTo<RevDemoLobbyState>();
            }

            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                _nextBattleMode = _nextBattleMode == "5v5" ? "1v1" : "5v5";
                Debug.Log($"[按键] 下一局模式切到 {_nextBattleMode}（下次进战斗会由工厂新建实例）");
            }

            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                RevDemoBattleAssetService.ResetStats();
                Debug.Log("[按键] 重置统计并从登录重来");
                _sm.ChangeTo<RevDemoLoginState>();
            }
        }

        /// <summary>左上角实时面板：把"状态机内部状态"直接摊开给人看</summary>
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 620f, 150f), GUI.skin.box);

            GUILayout.Label("=== 轻量级·单状态机 Demo：游戏主流程 ===");
            GUILayout.Label($"当前状态：{Name(_sm.CurrentState)}      进入本状态：{_sm.StateTime:F1} 秒");
            GUILayout.Label($"异步切换在途：{_sm.IsTransitioning}      事务目标 PendingState：{Name(_sm.PendingState)}");
            GUILayout.Label($"战斗资源：已加载={RevDemoBattleAssetService.IsLoaded}   " +
                            $"加载={RevDemoBattleAssetService.LoadCount} 次   释放={RevDemoBattleAssetService.ReleaseCount} 次");
            GUILayout.Label("[1] 开始匹配   [2] 取消/回大厅   [3] 换下一局模式   [4] 重置统计并回登录");

            GUILayout.EndArea();
        }

        /// <summary>状态名（null 显示中文占位，避免面板里出现空白）</summary>
        private static string Name(RevLightStateBase state)
            => state == null ? "（无）" : state.GetType().Name;
    }
}
