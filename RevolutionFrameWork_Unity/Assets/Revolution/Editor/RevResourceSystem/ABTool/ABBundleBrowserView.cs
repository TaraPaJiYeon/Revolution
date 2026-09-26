// ============================================================
// ABBundleBrowserView.cs —— 分包浏览 / 编辑（AssetBundleBrowser 那套交互）
//
// 位置：Editor\资源加载\ABTool\
//
// 【它补上了什么】
//   打包工具原来只能"整包一键打包"，看不到"现在分成了哪些包、每个包里有什么"。
//   这里补上：左边包列表，右边选中包里的资源，能新建 / 重命名 / 删除包，
//   也能【把 Project 里的资源直接拖进某个包】。
//
// 【★ 前提：Unity 里"包"不是实体，没有"创建一个包"这种 API】
//   所谓分包，就是"资源身上写着属于哪个包"（标记存在 .meta 的 assetBundleName 里）。所以：
//     · 新建包 = 一个还没人用的名字（资源拖进去才算真正存在）；
//     · 删除包 = 清空所有属于它的标记（资源本身不动）；
//     · 重命名 = 把所有"显式写着旧名"的标记改成新名。
//   文件夹也能被标记，子文件自动继承 —— 这也是本工具推荐的分包方式。
//   所以改名 / 删除必须【连文件夹一起改】，否则会出现"删了包、资源却还在包里"。
//
// 【★ 为什么全程用只读扫描】
//   ABCollector.CollectReadOnly()：绝不触发 AutoByFolder 的自动重标记。
//   浏览 / 手动改标记的窗口，不该顺手把全工程的分包重写一遍。
//   数据来自 ABCollectCache（依赖 / 体积 / 检查 三个视图也用同一份）。
//
// 【★ 自动同步：改了就跟随】
//   别处改了分包标记（Project 拖拽 / Inspector 的 AssetBundle 栏 / 脚本 / VCS 切分支），
//   本视图会自动更新，不必手动点刷新 —— 检测与去抖都在 ABMarkerWatcher，
//   这里只负责：顶部显示同步状态 + 给一个「自动同步」开关（关掉后「刷新」依然可用）。
//   本工具自己改完标记走 Refresh()：立刻生效，不等去抖。
//
// 【放资源进包的两种方式】
//   ① 从 Project 把资源（或文件夹）拖到左侧某个包上；
//   ② 拖到右侧底部的投放区 —— 加进当前选中的包。
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Revolution.Editor
{
    /// <summary>分包浏览视图：由 ABBuildWindow 以"分包浏览"页签承载（不自建窗口）</summary>
    internal sealed class ABBundleBrowserView
    {
        private ABCollectResult _collect;                 // 本帧用的扫描结果（来自共享缓存 ABCollectCache）
        private Vector2 _bundleScroll, _assetScroll;

        private string _selected;                         // 当前选中的包
        private string _newName = "";                     // "新建包"输入框
        private string _renameName = "";                  // "重命名"输入框
        private string _filter = "";                      // 包名过滤

        /// <summary>本帧拖拽中的"工程内资源"（每帧只算一次，避免每个包行都分配一次）</summary>
        private string[] _dragging = Array.Empty<string>();

        /// <summary>本次新建、但还没有任何资源的包 —— Unity 里这种包无法真正存在，只能先挂在界面上</summary>
        private readonly HashSet<string> _pending = new HashSet<string>();

        private static readonly Color SelectedTint = new Color(0.35f, 0.60f, 1.00f, 0.28f);
        private static readonly Color HoverTint = new Color(0.30f, 0.85f, 0.40f, 0.30f);

        /// <summary>
        /// 本工具自己改完标记后调它：立刻让共享缓存失效（不等自动同步的去抖）。
        /// 别处（Project 拖拽 / Inspector / 脚本）改的标记由 ABMarkerWatcher 自动发现 ——
        /// 两条路最终都是"失效同一份缓存"，效果一致。
        /// </summary>
        private static void Refresh() => ABMarkerWatcher.RefreshNow("工具内编辑分包");

        // ============================================================
        // 主绘制
        // ============================================================
        public void Draw(float availableHeight)
        {
            _collect = ABCollectCache.Get();              // 本帧数据：缓存没失效就直接复用，不重扫

            _dragging = CollectDragPaths();               // 本帧的拖拽内容（下面各处的悬停判断都用它）

            DrawHeader();

            float listHeight = Mathf.Max(160f, availableHeight - 104f);
            float leftWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.34f, 200f, 340f);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(leftWidth)))
                    DrawBundleList(listHeight);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    DrawAssetList(listHeight);
            }

            DrawToolbar();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(48)))
                    ABMarkerWatcher.RefreshNow("手动刷新");

                EditorGUILayout.LabelField(
                    $"共 {BundleNames().Count} 个包 · {_collect.logicToAssetPath.Count} 个资源",
                    EditorStyles.boldLabel, GUILayout.Width(220));

                GUILayout.FlexibleSpace();

                // 自动同步：别处改了分包标记，这里自动跟随（「刷新」永远可用，关掉也不影响手动刷）
                bool autoSync = EditorGUILayout.ToggleLeft("自动同步", ABMarkerWatcher.Enabled,
                    EditorStyles.miniLabel, GUILayout.Width(66));
                if (autoSync != ABMarkerWatcher.Enabled) ABMarkerWatcher.Enabled = autoSync;

                EditorGUILayout.LabelField(ABMarkerWatcher.StatusContent(),
                    EditorStyles.miniLabel, GUILayout.Width(156));
            }

            EditorGUILayout.LabelField(
                "把 Project 里的资源（或文件夹）拖到左侧的包上 = 加入该包；文件夹标记会被子文件自动继承",
                EditorStyles.miniLabel);

            if (ABBuildConfig.Instance.markMode == ABMarkMode.AutoByFolder)
                EditorGUILayout.HelpBox(
                    "当前是「按目录自动分包」模式：下次打包会先清空所有标记、再按目录重打，" +
                    "所以在这里手动改的分包会被覆盖。要手动分包，请把配置里的模式改成「扫描已有标记」。",
                    MessageType.Warning);
        }

        // ============================================================
        // 左：包列表（同时是拖拽投放目标）
        // ============================================================
        private void DrawBundleList(float height)
        {
            EditorGUILayout.LabelField("包列表", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("过滤", EditorStyles.miniLabel, GUILayout.Width(28));
                _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);
            }

            _bundleScroll = EditorGUILayout.BeginScrollView(_bundleScroll, GUILayout.Height(height - 34f));

            List<string> names = BundleNames();
            int shown = 0;
            foreach (string bundle in names)
            {
                if (_filter.Length > 0 &&
                    bundle.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                DrawBundleRow(bundle);
                shown++;
            }

            if (shown == 0)
                EditorGUILayout.LabelField(
                    names.Count == 0 ? "（还没有任何 AB 包，先在下面「新建包」）" : "（过滤后没有匹配的包）",
                    EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawBundleRow(string bundle)
        {
            Event e = Event.current;
            Rect row = EditorGUILayout.GetControlRect(false, 20f);

            // 拖拽悬停 → 绿色高亮；选中 → 蓝色底
            bool hovered = _dragging.Length > 0 && row.Contains(e.mousePosition);
            if (hovered) EditorGUI.DrawRect(row, HoverTint);
            else if (bundle == _selected) EditorGUI.DrawRect(row, SelectedTint);

            HandleDrop(row, bundle);

            // 先铺一层按钮吃掉点击（背景），再在上面画文字 —— 这样文字能左对齐，像 AssetBundleBrowser
            if (GUI.Button(row, GUIContent.none, EditorStyles.miniButton))
            {
                _selected = bundle;
                _renameName = bundle;
                GUI.FocusControl(null);
            }

            int count = CountOf(bundle);
            string label = (count == 0 && _pending.Contains(bundle))
                ? $"{bundle}    （空包，拖资源进来）"
                : $"{bundle}    ({count})";

            GUI.Label(new Rect(row.x + 6f, row.y, row.width - 10f, row.height), label,
                bundle == _selected ? EditorStyles.boldLabel : EditorStyles.label);
        }

        // ============================================================
        // 右：选中包里的资源
        // ============================================================
        private void DrawAssetList(float height)
        {
            if (string.IsNullOrEmpty(_selected))
            {
                EditorGUILayout.HelpBox(
                    "左边选一个包，这里会列出它包含的资源。\n" +
                    "也可以直接把 Project 里的资源拖到这里 —— 加进当前选中的包。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                $"包 \"{_selected}\"    {CountOf(_selected)} 个资源    {FormatSize(SizeOfBundle(_selected))}",
                EditorStyles.boldLabel);

            _assetScroll = EditorGUILayout.BeginScrollView(_assetScroll, GUILayout.Height(height - 34f));

            // ToArray：行里的「移出」会触发刷新，遍历旧快照最稳
            if (_collect.bundleToAssets.TryGetValue(_selected, out List<string> logics) && logics.Count > 0)
            {
                foreach (string logic in logics.ToArray())
                    DrawAssetRow(logic);
            }
            else
            {
                EditorGUILayout.LabelField("（空包：把资源拖进来即可）", EditorStyles.miniLabel);
            }

            DrawDropZone(_selected);

            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetRow(string logic)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(38))) Ping(logic);
                if (GUILayout.Button("移出", EditorStyles.miniButton, GUILayout.Width(38))) RemoveFromBundle(logic);

                // 继承来的标记"单独移不掉"：它归文件夹管 —— 标出来，免得用户以为按钮坏了
                bool inherited = IsInherited(logic);
                string text = $"{logic}    ({FormatSize(SizeOf(logic))})" +
                              (inherited ? "    ← 继承自文件夹标记" : "");

                EditorGUILayout.LabelField(text, inherited ? EditorStyles.miniLabel : EditorStyles.label);
            }
        }

        /// <summary>资源区底部的投放区（把资源加进当前选中的包）</summary>
        private void DrawDropZone(string bundle)
        {
            Rect zone = GUILayoutUtility.GetRect(0f, 36f, GUILayout.ExpandWidth(true));

            bool hovering = _dragging.Length > 0 && zone.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(zone, hovering ? HoverTint : new Color(0.5f, 0.5f, 0.5f, 0.12f));
            GUI.Label(new Rect(zone.x, zone.y + 8f, zone.width, 20f),
                hovering ? $"松手 → 加入 \"{bundle}\"" : $"把资源拖到这里 → 加入 \"{bundle}\"",
                EditorStyles.centeredGreyMiniLabel);

            HandleDrop(zone, bundle);
        }

        // ============================================================
        // 底部：新建 / 重命名 / 删除
        // ============================================================
        private void DrawToolbar()
        {
            EditorGUILayout.Space(2);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("新建包", EditorStyles.miniLabel, GUILayout.Width(46));
                _newName = EditorGUILayout.TextField(_newName, GUILayout.Width(150));
                if (GUILayout.Button("创建", EditorStyles.miniButton, GUILayout.Width(50))) CreateBundle();

                GUILayout.Space(16);

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selected)))
                {
                    EditorGUILayout.LabelField("当前包", EditorStyles.miniLabel, GUILayout.Width(46));
                    _renameName = EditorGUILayout.TextField(_renameName, GUILayout.Width(150));
                    if (GUILayout.Button("重命名", EditorStyles.miniButton, GUILayout.Width(60))) RenameBundle();
                    if (GUILayout.Button("删除包", EditorStyles.miniButton, GUILayout.Width(60))) DeleteBundle();
                }
            }

            EditorGUILayout.LabelField(
                "改完分包记得回「打包」页签点一次「扫描并生成映射」—— 映射表和 RevResPath 才会跟着更新。",
                EditorStyles.miniLabel);
        }

        // ============================================================
        // 拖拽
        // ============================================================

        /// <summary>当前拖拽中的工程内资源路径（拖进来的如果不是 Assets 下的东西，一律不收）</summary>
        private static string[] CollectDragPaths()
        {
            string[] raw = DragAndDrop.paths;
            if (raw == null || raw.Length == 0) return Array.Empty<string>();

            var list = new List<string>(raw.Length);
            foreach (string p in raw)
            {
                string norm = p.Replace('\\', '/');
                if (norm.StartsWith("Assets/")) list.Add(norm);
            }
            return list.ToArray();
        }

        /// <summary>
        /// 接住"放下"这个动作。
        /// ★ 只处理 DragUpdated / DragPerform 两种事件：前者是给鼠标反馈（显不显示"可放下"），
        ///   后者才是真正落地。AcceptDrag 和 e.Use() 少任何一个，都会出现"松手没反应"。
        /// </summary>
        private void HandleDrop(Rect rect, string bundle)
        {
            Event e = Event.current;
            if (e == null) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            if (!rect.Contains(e.mousePosition)) return;
            if (_dragging.Length == 0) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddAssets(bundle, _dragging);
                e.Use();
            }
        }

        private void AddAssets(string bundle, string[] assetPaths)
        {
            int added = 0, moved = 0, skipped = 0;

            foreach (string path in assetPaths)
            {
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer == null) { skipped++; continue; }

                string current = importer.assetBundleName;
                if (current == bundle) { skipped++; continue; }        // 本来就在这个包里

                if (!string.IsNullOrEmpty(current)) moved++;
                importer.assetBundleName = bundle;
                importer.assetBundleVariant = string.Empty;            // 顺带清变体，免得出现"包名.变体"这种难管的名字
                added++;
            }

            if (added > 0) AssetDatabase.SaveAssets();                 // 标记写在 .meta 里，显式落盘
            _pending.Remove(bundle);                                   // 它已经有资源了，不再是"空包"

            Debug.Log($"[LiteAB] 加入包 \"{bundle}\"：新增 {added} 个" +
                      (moved > 0 ? $"，其中 {moved} 个是从别的包挪过来的" : "") +
                      (skipped > 0 ? $"，跳过 {skipped} 个" : ""));
            Refresh();
        }

        // ============================================================
        // 新建 / 重命名 / 删除 / 移出
        // ============================================================

        private void CreateBundle()
        {
            string name = (_newName ?? string.Empty).Trim();
            if (!IsLegalBundleName(name, out string why))
            {
                Debug.LogError($"[LiteAB] 包名不合法：{why}");
                return;
            }

            if (BundleNames().Contains(name))
            {
                _selected = name;                                      // 已存在 → 直接选中它
                _renameName = name;
                return;
            }

            _pending.Add(name);                                        // 空包：Unity 里它还不存在，先挂在界面上
            _selected = name;
            _renameName = name;
            _newName = "";

            Debug.Log($"[LiteAB] 已新建空包 \"{name}\"：把 Project 里的资源拖到它上面，就真正建好了。");
            Refresh();
        }

        private void RenameBundle()
        {
            string oldName = _selected;
            string newName = (_renameName ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(oldName)) return;
            if (!IsLegalBundleName(newName, out string why))
            {
                Debug.LogError($"[LiteAB] 包名不合法：{why}");
                return;
            }
            if (newName == oldName) return;

            int changed = RewriteMarks(oldName, newName);

            if (_pending.Remove(oldName)) _pending.Add(newName);
            _selected = newName;

            Debug.Log($"[LiteAB] 包 \"{oldName}\" 已重命名为 \"{newName}\"（改了 {changed} 处标记）");
            Refresh();
        }

        private void DeleteBundle()
        {
            string bundle = _selected;
            if (string.IsNullOrEmpty(bundle)) return;

            if (!EditorUtility.DisplayDialog("删除包",
                    $"要删除包 \"{bundle}\" 吗？\n\n" +
                    "会清空属于它的 AB 标记（资源文件本身不会被删）。\n" +
                    "如果它是靠\"给文件夹设标记\"生效的，那个文件夹的标记也会一起清掉，\n" +
                    "子文件随之退出此包。",
                    "删除", "取消"))
                return;

            int changed = RewriteMarks(bundle, "");
            _pending.Remove(bundle);
            _selected = null;

            Debug.Log($"[LiteAB] 已删除包 \"{bundle}\"（清掉了 {changed} 处标记）");
            Refresh();
        }

        /// <summary>把单个资源移出它所在的包（文件夹继承来的移不掉，会给出指引）</summary>
        private void RemoveFromBundle(string logic)
        {
            if (!_collect.logicToAssetPath.TryGetValue(logic, out string path)) return;

            if (IsInherited(logic))
            {
                Debug.LogWarning($"[LiteAB] \"{logic}\" 的包是从文件夹标记继承来的，单独移不掉 —— " +
                                 "请在 Project 里选中它所在的文件夹、把 AssetBundle 栏清空，" +
                                 "或者直接用「删除包」。");
                return;
            }

            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null) return;

            importer.assetBundleName = string.Empty;
            importer.assetBundleVariant = string.Empty;
            AssetDatabase.SaveAssets();

            Debug.Log($"[LiteAB] 已把 \"{logic}\" 移出包");
            Refresh();
        }

        /// <summary>
        /// 把"显式写着 oldName"的标记整体改成 newName（newName 为空 = 清掉 = 删包）。
        ///
        /// ★ 必须连【文件夹】一起改：文件夹上的标记会让子文件隐式继承，
        ///   只改文件不改文件夹，就会出现"删了包、资源却还在包里"的怪现象。
        /// </summary>
        private static int RewriteMarks(string oldName, string newName)
        {
            int changed = 0;

            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/")) continue;

                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer == null || importer.assetBundleName != oldName) continue;

                importer.assetBundleName = newName;
                if (newName.Length == 0) importer.assetBundleVariant = string.Empty;
                changed++;
            }

            if (changed > 0) AssetDatabase.SaveAssets();
            return changed;
        }

        // ============================================================
        // 小工具
        // ============================================================

        /// <summary>当前所有包名：工程里真实存在的 + 本次新建但还空的</summary>
        private List<string> BundleNames()
        {
            var set = new HashSet<string>(_collect.bundleToAssets.Keys);
            foreach (string empty in _collect.emptyBundles) set.Add(empty);
            foreach (string pending in _pending) set.Add(pending);

            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);                 // 稳定顺序，免得列表每次刷新都跳
            return list;
        }

        private int CountOf(string bundle)
            => _collect.bundleToAssets.TryGetValue(bundle, out List<string> list) ? list.Count : 0;

        /// <summary>这个资源的包是不是从上层文件夹继承来的（自己没写标记）</summary>
        private bool IsInherited(string logic)
        {
            if (!_collect.logicToAssetPath.TryGetValue(logic, out string path)) return false;

            AssetImporter importer = AssetImporter.GetAtPath(path);
            return importer != null && string.IsNullOrEmpty(importer.assetBundleName);
        }

        private void Ping(string logic)
        {
            if (!_collect.logicToAssetPath.TryGetValue(logic, out string path)) return;

            UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null) return;

            EditorGUIUtility.PingObject(obj);                  // Project 窗口里闪一下
            Selection.activeObject = obj;                      // 同时选中，方便接着改
        }

        private long SizeOfBundle(string bundle)
        {
            long total = 0;
            if (_collect.bundleToAssets.TryGetValue(bundle, out List<string> list))
                foreach (string logic in list) total += SizeOf(logic);
            return total;
        }

        private long SizeOf(string logic)
        {
            if (!_collect.logicToAssetPath.TryGetValue(logic, out string path)) return 0;

            string abs = Path.GetFullPath(path);
            return File.Exists(abs) ? new FileInfo(abs).Length : 0;
        }

        private static string FormatSize(long bytes)
            => bytes >= 1024 * 1024
                ? $"{bytes / 1024f / 1024f:F1} MB"
                : $"{bytes / 1024f:F0} KB";

        /// <summary>包名合法性：Unity 里 '.' 是变体分隔符、'\' 与空格各平台容易出问题</summary>
        private static bool IsLegalBundleName(string name, out string why)
        {
            if (string.IsNullOrEmpty(name)) { why = "名字不能为空"; return false; }
            if (name.Contains("\\")) { why = "不要包含 '\\'（用 '/' 分层）"; return false; }
            if (name.Contains(".")) { why = "不要包含 '.'（Unity 用它分隔变体）"; return false; }
            if (name.Contains(" ")) { why = "不要包含空格"; return false; }
            if (name.EndsWith("/")) { why = "结尾不要带 '/'"; return false; }

            foreach (string seg in name.Split('/'))
                if (seg.Length == 0) { why = "不要出现连续的 '/'"; return false; }

            why = null;
            return true;
        }
    }
}
