// ============================================================
// RevDemoUiStackDemo.cs —— 轻量级【栈状态机】Demo 的驱动（MonoBehaviour）
//
// 位置：Revolution.Demo\Unity\RevStateMachine\LightweightUiStack\
//
// 【怎么跑】场景里挂一个空物体，把本组件加上即可（无需 UI/Prefab）。
//
// 【演示的按键】
//   [1] 打开背包        Push<RevDemoUiBagState>()（栈顶被压住 → OnSuspend）
//   [2] 打开设置        Push<RevDemoUiSettingsState>()（同样 OnSuspend）
//   [3] 打开帮助        Push<RevDemoUiTipsState>()（★ 工厂注册，可以连点叠多层）
//   [4] 返回上一级      Pop()（栈顶 OnExit → 新栈顶 OnResume）
//   [5] 全部关闭        Clear()（逐层 OnExit，不触发 OnResume）
//   [6] 故意重复压栈    先压设置、再压同一个实例 → ★ 被框架拒绝（看警告日志）
//   [7] 替换栈顶        Change<RevDemoUiSettingsState>()（栈深不变，TargetState 记录去向）
//
// 【这个 Demo 想让人看见三件事】
//   ① 被压住的状态【还在栈里】—— 面板上每个状态的"被驱动帧数"会停住，但状态没销毁；
//   ② 返回时是 OnResume 而不是重建 —— 所以"数据一直在"，不需要重新拉取；
//   ③ 框架不提供"按下标访问任意层"，所以**返回路径（面包屑）由业务自己订阅事件维护** ——
//      本 Demo 用一个 List 快照演示了这件事（这正是"栈内容想画成列表"时的推荐做法）。
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo
{
    /// <summary>
    /// UI 界面栈 Demo：主界面 → 背包 → 设置/帮助，逐级压栈、逐级返回。
    /// </summary>
    public sealed class RevDemoUiStackDemo : MonoBehaviour
    {
        private RevLightStackStateMachine<RevLightStateBase> _sm;

        /// <summary>
        /// 业务自己维护的"返回路径快照"（面包屑）。
        /// <para>为什么不让状态机提供：栈用 Stack&lt;T&gt; 承载，只有 LIFO，读不了"倒数第 N 层"；
        /// 想画整条路径就在 Push/Pop 事件里同步一份 —— 成本极低，且永远不会和真栈不同步。</para>
        /// </summary>
        private readonly List<RevLightStateBase> _breadcrumb = new List<RevLightStateBase>();

        private int _tipsLayer;     // 帮助弹窗的层号（工厂每次创建时 +1）

        private void Awake()
        {
            _sm = new RevLightStackStateMachine<RevLightStateBase>();

            // 订阅入栈 / 出栈事件，维护路径快照（王者⑤"事件解耦"的用法）
            _sm.StatePushed += state =>
            {
                _breadcrumb.Add(state);
                Debug.Log($"[UI栈] 压栈 → 栈深 {_sm.Count}，当前路径：{BreadcrumbText()}");
            };
            _sm.StatePopped += state =>
            {
                _breadcrumb.Remove(state);
                Debug.Log($"[UI栈] 弹栈 ← 栈深 {_sm.Count}，当前路径：{BreadcrumbText()}");
            };

            // 注册状态：前三个用【实例】（同一时刻只会有一个）
            _sm.Register(new RevDemoUiLobbyState());
            _sm.Register(new RevDemoUiBagState());
            _sm.Register(new RevDemoUiSettingsState());

            // 帮助弹窗用【工厂】：可以叠多层，每次都要新实例
            _sm.Register<RevDemoUiTipsState>(() => new RevDemoUiTipsState(++_tipsLayer));

            // 启动：主界面入栈（栈底）
            _sm.Push<RevDemoUiLobbyState>();
        }

        private void Update()
        {
            // ★ 栈状态机也只驱动"栈顶"：被压住的界面不会被 Update（省性能，也避免看不见的界面还在响应输入）
            _sm.Update(Time.deltaTime);

            if (Input.GetKeyDown(KeyCode.Alpha1)) PushOnce<RevDemoUiBagState>("背包");
            if (Input.GetKeyDown(KeyCode.Alpha2)) PushOnce<RevDemoUiSettingsState>("设置");
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                Debug.Log("[按键] 打开帮助（工厂注册 → 可叠多层）");
                _sm.Push<RevDemoUiTipsState>();
            }

            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                RevLightStateBase popped = _sm.Pop();
                Debug.Log(popped == null ? "[按键] 栈已经空了" : $"[按键] 返回上一级，关掉 {popped.GetType().Name}");
            }

            if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                Debug.Log("[按键] 全部关闭（逐层 OnExit，不触发 OnResume）");
                _sm.Clear();
            }

            if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                // 故意演示误用：把"同一个实例"连压两次会被框架拒绝
                PushOnce<RevDemoUiSettingsState>("设置");
                try
                {
                    _sm.Push<RevDemoUiSettingsState>();
                }
                catch (InvalidOperationException e)
                {
                    Debug.LogWarning($"[按键] ★ 框架拦住了这次误用：{e.Message}");
                }
            }

            if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                // Change：替换栈顶（栈深不变）。旧状态的 OnExit 里可以读到 TargetState（= 王者①的 tarState）
                Debug.Log("[按键] 用【设置】替换当前栈顶（栈深不变）");
                _sm.Change<RevDemoUiSettingsState>();
            }
        }

        /// <summary>只在"该类型还不在栈顶"时压栈：避免 Demo 里按键连点触发框架的重复压栈保护</summary>
        private void PushOnce<TState>(string label) where TState : RevLightStateBase
        {
            if (_sm.Is<TState>())
            {
                Debug.Log($"[按键] {label} 已经在栈顶了，重复压同一个实例会被框架拒绝 → 这次跳过");
                return;
            }

            _sm.Push<TState>();
        }

        /// <summary>把路径快照拼成面包屑文本（主界面 &gt; 背包 &gt; 设置）</summary>
        private string BreadcrumbText()
        {
            if (_breadcrumb.Count == 0) return "（空）";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _breadcrumb.Count; i++)
            {
                if (i > 0) sb.Append(" > ");
                sb.Append(ShortName(_breadcrumb[i]));
            }
            return sb.ToString();
        }

        /// <summary>把状态类名缩短一点，方便面板显示</summary>
        private static string ShortName(RevLightStateBase s)
            => s == null ? "（无）" : s.GetType().Name.Replace("RevDemoUi", string.Empty);

        /// <summary>左上角面板：栈结构 + "谁正在被驱动"一眼看清</summary>
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, 640f, 250f), GUI.skin.box);

            GUILayout.Label("=== 轻量级·栈状态机 Demo：UI 界面栈 ===");
            GUILayout.Label($"栈深：{_sm.Count}    栈顶：{ShortName(_sm.Current)}    栈顶下一层(Under)：{ShortName(_sm.Under)}");
            GUILayout.Label($"栈顶进入时长：{_sm.StateTime:F1} 秒    面包屑：{BreadcrumbText()}");

            GUILayout.Label("—— 从栈底到栈顶（括号里是【被驱动过多少帧】）——");
            for (int i = 0; i < _breadcrumb.Count; i++)
            {
                int frames = _breadcrumb[i] switch
                {
                    RevDemoUiLobbyState lobby => lobby.UpdatedFrames,
                    RevDemoUiBagState bag => bag.UpdatedFrames,
                    RevDemoUiSettingsState settings => settings.UpdatedFrames,
                    RevDemoUiTipsState tips => tips.UpdatedFrames,
                    _ => 0,
                };
                string suffix = (i == _breadcrumb.Count - 1) ? "  ← 栈顶（只有它在被 Update 驱动）" : string.Empty;
                GUILayout.Label($"  {i + 1}. {ShortName(_breadcrumb[i])}（{frames} 帧）{suffix}");
            }

            GUILayout.Label("[1] 打开背包  [2] 打开设置  [3] 打开帮助(可叠)  [4] 返回  [5] 全关  [6] 演示重复压栈  [7] 替换栈顶");

            GUILayout.EndArea();
        }
    }
}
