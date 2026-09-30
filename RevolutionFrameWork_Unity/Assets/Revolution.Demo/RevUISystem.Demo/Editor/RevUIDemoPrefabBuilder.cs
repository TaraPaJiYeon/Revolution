// ============================================================
// RevUIDemoPrefabBuilder.cs —— 一键生成"UI 演示面板"的预制体（编辑器专用）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\Editor\
//
// 【它做什么】
//   用代码搭出演示面板的结构（背景 / 信息文本 / 两个按钮），存成
//   Assets\Revolution.Demo\RevUISystem.Demo\RevUIDemoPanel.prefab。
//   生成的节点名与 RevUIDemoPanel.cs 里的 [RevBind] / OnClick(节点名) 严格对应：
//     btnAdd    —— 加一条日志（OnClick 分发）
//     txtInfo   —— 信息文本（[RevBind] Text）
//     btnClose  —— 关闭面板（OnClick 分发）
//
// 【怎么用】菜单 Revolution.Tools/Demo/生成 UI 演示面板预制体；或 RevUISystemDemo 的按钮 ①。
//   只需生成一次（生成物进版本库，随用随开）。改了面板结构可重复生成覆盖。
// ============================================================
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI.Editor
{
    internal static class RevUIDemoPrefabBuilder
    {
        private const string PrefabPath = "Assets/Revolution.Demo/RevUISystem.Demo/RevUIDemoPanel.prefab";
        private const string RootName = "RevUIDemoPanel";

        [MenuItem("Revolution.Tools/Demo/生成 UI 演示面板预制体", false, 31)]
        private static void MenuBuild() => Build();

        /// <summary>搭出面板结构并保存为预制体。返回是否成功。</summary>
        internal static bool Build()
        {
            // ---------- 根节点：面板本体（挂 RevUIDemoPanel 脚本；管理器按 [RevUIPanel] 找到它） ----------
            var root = new GameObject(RootName, typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(520, 320);

            var panel = root.AddComponent<RevUIDemoPanel>();          // 面板脚本（含 [RevUIPanel] 元数据）

            // ---------- 背景：半透明卡片 ----------
            var bg = NewNode("Bg", root);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.13f, 0.15f, 0.19f, 0.96f);
            Stretch(bg);

            // ---------- 标题 ----------
            var title = NewNode("txtTitle", root);
            SetText(title, "演示面板（RevUIDemoPanel）", 18, FontStyle.Bold, new Color(0.9f, 0.94f, 1f));
            Anchor(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20, -46), new Vector2(-20, -10));

            // ---------- 信息文本（[RevBind] Text _txtInfo） ----------
            var info = NewNode("txtInfo", root);
            SetText(info, "演示面板已打开。", 14, FontStyle.Normal, new Color(0.82f, 0.86f, 0.92f));
            Anchor(info, new Vector2(0f, 0.35f), new Vector2(1f, 0.9f), new Vector2(20, 10), new Vector2(-20, -10));

            // ---------- 按钮：加一条日志（OnClick("btnAdd")） ----------
            var btnAdd = NewNode("btnAdd", root);
            MakeButton(btnAdd, "加一条日志", new Color(0.25f, 0.55f, 0.95f));
            Anchor(btnAdd, new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(20, 16), new Vector2(-10, 70));

            // ---------- 按钮：关闭（OnClick("btnClose")） ----------
            var btnClose = NewNode("btnClose", root);
            MakeButton(btnClose, "✕ 关闭", new Color(0.75f, 0.3f, 0.28f));
            Anchor(btnClose, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(10, 16), new Vector2(-20, 70));

            // ---------- 存成预制体 ----------
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) !);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);                            // 场景里的临时对象用完即弃

            if (prefab == null)
            {
                Debug.LogError($"[RevUIDemoPrefabBuilder] 预制体保存失败：{PrefabPath}");
                return false;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[RevUIDemoPrefabBuilder] 演示面板预制体已生成：{PrefabPath}");
            return true;
        }

        // ---------- 小工具：搭 UGUI 节点 ----------

        private static GameObject NewNode(string name, GameObject parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        /// <summary>铺满父节点（背景用）。</summary>
        private static void Stretch(GameObject go)
        {
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        /// <summary>按锚点与偏移摆一个矩形。</summary>
        private static void Anchor(GameObject go, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            var r = (RectTransform)go.transform;
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = offMin;
            r.offsetMax = offMax;
        }

        private static void SetText(GameObject go, string content, int size, FontStyle style, Color color)
        {
            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// <summary>把一个节点做成按钮（Image + Button + 居中文本）。</summary>
        private static void MakeButton(GameObject go, string label, Color color)
        {
            var image = go.AddComponent<Image>();
            image.color = color;

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(color.r + 0.08f, color.g + 0.08f, color.b + 0.08f);
            colors.pressedColor = new Color(color.r - 0.06f, color.g - 0.06f, color.b - 0.06f);
            button.colors = colors;

            var labelGo = NewNode("Text", go);
            var text = labelGo.AddComponent<Text>();
            text.text = label;
            text.fontSize = 15;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Stretch(labelGo);
        }
    }
}
