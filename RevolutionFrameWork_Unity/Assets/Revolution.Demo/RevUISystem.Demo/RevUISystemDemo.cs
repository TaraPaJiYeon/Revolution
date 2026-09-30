// ============================================================
// RevUISystemDemo.cs —— UI 系统演示入口（打开 / 关闭 / 查询）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\
//
// 【怎么用】打开配套场景 RevUISystemDemo.unity → 点 Play：
//   ① 第一次使用：点「① 生成演示面板预制体」（编辑器菜单也可以）—— 只需做一次；
//   ② 点「② 打开面板」→ 弹出演示面板（Popup 层自动带遮罩挡住下面）；
//   ③ 面板里点按钮体验 OnClick 分发；点关闭按钮回到本界面；
//   ④ 「③ 关闭全部」「④ Back」「⑤ 状态查询」体验管理接口。
//
// 【本示例演示什么】
//   ① 声明式面板：类上 [RevUIPanel] 声明，业务只调 RevUI.Open<T>()；
//   ② 打开 / 关闭 / 返回键（Back）与面板池（关了再开不重新实例化）；
//   ③ 状态查询：IsOpen / Get / OpenedCount。
//
// 【面板本体】见 RevUIDemoPanel.cs（业务面板的标准写法）；
// 【预制体生成】见 Editor\RevUIDemoPrefabBuilder.cs。
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.UI
{
    public sealed class RevUISystemDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start()
        {
            Ui("UI 系统演示就绪。");
            Ui("第一次使用先点「① 生成演示面板预制体」（用代码搭出一个 Panel 并存成 prefab，只需一次）。");
            Ui($"面板是否已开：{RevUI.IsOpen<RevUIDemoPanel>()}（统计详情用下方按钮 ⑤ 看 DumpStats）。");
        }

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevUI UI 系统演示</b>", TitleStyle());

#if UNITY_EDITOR
            // ① 生成演示面板预制体（只需一次；生成物 RevUIDemoPanel.prefab 在本目录，进版本库）
            if (GUILayout.Button("① 生成演示面板预制体（首次使用点一次）"))
            {
                bool ok = Editor.RevUIDemoPrefabBuilder.Build();
                Ui(ok ? "预制体已生成：RevUISystem.Demo/RevUIDemoPanel.prefab ✓（下一步就能打开面板）"
                      : "生成失败，详见 Console。");
            }
            GUILayout.Space(4);
#endif

            // ② 打开面板：业务唯一要写的一行（面板没加载过会自动异步加载预制体）
            if (GUILayout.Button("② 打开演示面板（RevUI.Open，Popup 层自动带遮罩）"))
            {
                RevUI.Open<RevUIDemoPanel>(panel =>
                {
                    if (panel != null) Ui("面板已打开（第二次打开走面板池，秒开）。");
                });
                Ui("已发起打开（需要加载时异步，完成时回调）……");
            }

            // ③ 关闭：按类型关（面板里也可以自己 CloseSelf()）
            if (GUILayout.Button("③ 关闭面板（RevUI.Close）"))
            {
                bool closed = RevUI.Close<RevUIDemoPanel>();
                Ui(closed ? "已关闭（KeepAlive 默认策略：实例进面板池，下次秒开）。" : "面板本来就没开。");
            }

            // ④ 返回键语义：关掉最晚打开、且参与返回栈的面板
            if (GUILayout.Button("④ Back（返回键：关最上层可返回面板）"))
            {
                Ui(RevUI.Back() ? "已返回一层。" : "没有可返回的面板（Toast 层不参与返回栈）。");
            }

            GUILayout.Space(6);

            // ⑤ 查询
            if (GUILayout.Button("⑤ 查询状态"))
            {
                var panel = RevUI.Get<RevUIDemoPanel>();
                Ui($"IsOpen：{RevUI.IsOpen<RevUIDemoPanel>()}　Get：{(panel == null ? "null" : panel.State.ToString())}");
                Debug.Log("[RevUISystemDemo] 全部面板统计：\n" + RevUI.DumpStats());
            }

            if (GUILayout.Button("⑥ CloseAll：关闭全部面板"))
            {
                int closed = RevUI.CloseAll();
                Ui($"已关闭 {closed} 个面板。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("原则：业务只认 RevUI 入口与面板类 —— 换加载方式 / 换层级实现，业务代码一行不改。", HintStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：演示日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 470, 10, 460, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
