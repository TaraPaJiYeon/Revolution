// ============================================================
// ABAnalysisViews.cs —— 三个"检查类"视图：依赖 / 体积 / 重名
//
// 位置：Editor\资源加载\ABTool\
//
// 【为什么三个放一个文件】
//   它们共用同一套东西：同一次只读扫描（ABCollectCache）、同一个依赖分析结果
//   （ABDependencyCache）、同一批小工具（定位 / 体积格式化 / 排序）。
//   拆三个文件就得把这套东西复制三遍，或者再抽一个文件出来 —— 都不如放一起清楚。
//
// 【数据从哪来】
//   · 分包信息：ABCollector.CollectReadOnly()（只读；绝不触发 AutoByFolder 的自动重标记）
//   · 依赖关系：ABDependencyAnalyzer.AnalyzeFromDatabase()（编辑器侧反推，不用先打包）
//   ★ 依赖分析要对每个资源跑一次 GetDependencies，比较贵 —— 所以按"扫描结果实例"缓存：
//     同一份扫描只算一次，标记一变（缓存失效）才会重算。
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Revolution.Editor
{
    /// <summary>
    /// 只读扫描结果的共享缓存。
    /// 四个视图（分包 / 依赖 / 体积 / 重名）共用同一份数据，避免每次切页签都重扫一遍；
    /// 任何"改了标记"的地方调一次 Invalidate()，下一次绘制就会自动重扫。
    /// </summary>
    internal static class ABCollectCache
    {
        private static ABCollectResult _collect;
        private static bool _dirty = true;

        public static void Invalidate() => _dirty = true;

        public static ABCollectResult Get()
        {
            if (_dirty || _collect == null)
            {
                _collect = ABCollector.CollectReadOnly();
                _dirty = false;
            }
            return _collect;
        }
    }

    /// <summary>依赖分析结果缓存：同一份扫描只算一次</summary>
    internal static class ABDependencyCache
    {
        private static ABCollectResult _forCollect;
        private static ABDependencyReport _report;

        public static ABDependencyReport Get(ABCollectResult collect)
        {
            // 拿"扫描结果实例"当版本号：它只在缓存失效时才换成新对象
            if (_report == null || !ReferenceEquals(_forCollect, collect))
            {
                _forCollect = collect;
                _report = ABDependencyAnalyzer.AnalyzeFromDatabase(collect);
            }
            return _report;
        }
    }

    /// <summary>检查类视图的公共部分（标题栏 / 排序 / 体积格式化 / 定位）</summary>
    internal abstract class ABAnalysisView
    {
        public abstract void Draw(float availableHeight);

        /// <summary>画"刷新"按钮 + 标题，并返回本帧的扫描结果</summary>
        protected static ABCollectResult DrawTitle(string title)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(48)))
                    ABCollectCache.Invalidate();          // 标记可能被别处（Inspector / 别的窗口）改过 → 重扫

                EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Width(120));
                GUILayout.FlexibleSpace();
            }

            return ABCollectCache.Get();
        }

        // ---------------- 小工具（三个视图都要用） ----------------

        protected static List<KeyValuePair<string, T>> Sorted<T>(Dictionary<string, T> map)
        {
            var list = new List<KeyValuePair<string, T>>(map);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        protected static string FormatSize(long bytes)
            => bytes >= 1024 * 1024
                ? $"{bytes / 1024f / 1024f:F2} MB"
                : $"{bytes / 1024f:F1} KB";

        protected static long FileSize(string assetPath)
        {
            string abs = Path.GetFullPath(assetPath);
            return File.Exists(abs) ? new FileInfo(abs).Length : 0;
        }

        /// <summary>长路径只留 "Assets/" 之后的部分，列表才不至于被撑爆</summary>
        protected static string Short(string assetPath)
            => assetPath.StartsWith("Assets/") ? assetPath.Substring("Assets/".Length) : assetPath;

        protected static void Ping(string assetPath)
        {
            UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (obj == null) return;

            EditorGUIUtility.PingObject(obj);              // Project 窗口里闪一下
            Selection.activeObject = obj;                  // 同时选中，方便接着改
        }

        /// <summary>一行：[定位] 按钮 + 文字</summary>
        protected static void RowWithPing(string assetPath, string text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(38))) Ping(assetPath);
                EditorGUILayout.LabelField(text, EditorStyles.miniLabel);
            }
        }
    }

    // ============================================================
    // ① 依赖视图
    // ============================================================
    /// <summary>包 → 包 的依赖关系、会被复制多份的资源、循环依赖</summary>
    internal sealed class ABDependencyView : ABAnalysisView
    {
        private Vector2 _scroll;

        public override void Draw(float availableHeight)
        {
            ABCollectResult collect = DrawTitle("依赖视图");
            ABDependencyReport report = ABDependencyCache.Get(collect);

            int edges = 0;
            foreach (var pair in report.directDeps) edges += pair.Value.Length;

            EditorGUILayout.LabelField(
                $"{report.directDeps.Count} 个包 · {edges} 条包间依赖 · " +
                $"{report.sharedAssets.Count} 个会被复制多份的资源",
                EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(availableHeight - 56f));

            // ---------- ① 环：最严重，放最前面 ----------
            if (report.circularBundles.Count > 0)
                EditorGUILayout.HelpBox(
                    "发现循环依赖（加载顺序无解，必须拆开）：" + string.Join("、", report.circularBundles),
                    MessageType.Error);
            else if (edges == 0)
                EditorGUILayout.HelpBox(
                    "没有任何包间依赖 —— 每个包都能独立加载，这是最理想的状态。",
                    MessageType.Info);

            // ---------- ② 包依赖 ----------
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("包 → 它依赖的包", EditorStyles.boldLabel);

            if (report.directDeps.Count == 0)
                EditorGUILayout.LabelField("（还没有任何包）", EditorStyles.miniLabel);

            foreach (var pair in Sorted(report.directDeps))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(pair.Key, GUILayout.Width(180));
                    EditorGUILayout.LabelField(
                        pair.Value.Length == 0 ? "—（不依赖别的包）" : string.Join("、", pair.Value),
                        EditorStyles.miniLabel);
                }
            }

            // ---------- ③ 会被复制多份的资源：体积膨胀的真正来源 ----------
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("被多个包引用、又没分包（会被复制多份）", EditorStyles.boldLabel);

            if (report.sharedAssets.Count == 0)
            {
                EditorGUILayout.LabelField("（无：被引用到的资源都明确归了某个包）", EditorStyles.miniLabel);
            }
            else
            {
                long wasted = 0;

                foreach (var pair in Sorted(report.sharedAssets))
                {
                    long size = FileSize(pair.Key);
                    wasted += size * (pair.Value.Count - 1);

                    RowWithPing(pair.Key,
                        $"{Short(pair.Key)}    被 {string.Join("、", pair.Value)} 引用    " +
                        $"{FormatSize(size)} × {pair.Value.Count} 份");
                }

                EditorGUILayout.HelpBox(
                    $"这些资源会被复制进每一个引用它的包，合计多占约 {FormatSize(wasted)}。\n" +
                    "把它们单独打成一个共享包（或提升为常驻资源）通常是最省体积的一步。",
                    MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }
    }

    // ============================================================
    // ② 体积对比
    // ============================================================
    /// <summary>每个包多大、含依赖多大，以及最大的那些资源</summary>
    internal sealed class ABSizeView : ABAnalysisView
    {
        private struct Row
        {
            public string bundle;
            public int count;
            public long own;          // 包内资源自身
            public long withDeps;     // 自身 + 会被复制进来的未分包资源
        }

        private Vector2 _scroll;
        private bool _bySize = true;

        public override void Draw(float availableHeight)
        {
            ABCollectResult collect = DrawTitle("体积对比");
            ABDependencyReport report = ABDependencyCache.Get(collect);

            // ---------- 汇总 ----------
            var rows = new List<Row>();

            foreach (var pair in collect.bundleToAssets)
            {
                var row = new Row { bundle = pair.Key, count = pair.Value.Count };

                foreach (string logic in pair.Value)
                    if (collect.logicToAssetPath.TryGetValue(logic, out string assetPath))
                        row.own += FileSize(assetPath);

                report.extraBytes.TryGetValue(pair.Key, out long extra);
                row.withDeps = row.own + extra;

                rows.Add(row);
            }

            rows.Sort(_bySize
                ? (Comparison<Row>)((a, b) => b.withDeps.CompareTo(a.withDeps))
                : ((a, b) => string.CompareOrdinal(a.bundle, b.bundle)));

            long total = 0;
            long max = 1;
            foreach (Row r in rows) { total += r.withDeps; max = Math.Max(max, r.withDeps); }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{rows.Count} 个包 · 合计约 {FormatSize(total)}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("按体积", EditorStyles.miniButton, GUILayout.Width(60))) _bySize = true;
                if (GUILayout.Button("按名字", EditorStyles.miniButton, GUILayout.Width(60))) _bySize = false;
            }

            EditorGUILayout.LabelField(
                "深色 = 包自身，浅色 = 含依赖（含会被复制进来的未分包资源）",
                EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(availableHeight - 110f));

            // ---------- 条形列表 ----------
            if (rows.Count == 0)
                EditorGUILayout.LabelField("（还没有任何包）", EditorStyles.miniLabel);

            foreach (Row row in rows)
            {
                Rect line = EditorGUILayout.GetControlRect(false, 24f);

                GUI.Label(new Rect(line.x, line.y + 3f, 170f, 18f), row.bundle, EditorStyles.boldLabel);

                // 条形轨道（宽度留出右边数字的位置；窗口太窄时兜个底，别算出负宽度）
                float barWidth = Mathf.Max(40f, line.width - 172f - 240f);
                Rect track = new Rect(line.x + 172f, line.y + 5f, barWidth, 14f);
                EditorGUI.DrawRect(track, new Color(0.5f, 0.5f, 0.5f, 0.12f));

                float kWith = Mathf.Clamp01(row.withDeps / (float)max);
                float kOwn = Mathf.Clamp01(row.own / (float)max);

                EditorGUI.DrawRect(new Rect(track.x, track.y, track.width * kWith, track.height),
                    new Color(0.35f, 0.60f, 1.00f, 0.35f));
                EditorGUI.DrawRect(new Rect(track.x, track.y, track.width * kOwn, track.height),
                    new Color(0.35f, 0.60f, 1.00f, 0.85f));

                GUI.Label(new Rect(line.xMax - 236f, line.y + 3f, 236f, 18f),
                    $"{FormatSize(row.own)}   含依赖 {FormatSize(row.withDeps)}   {row.count} 个资源",
                    EditorStyles.miniLabel);
            }

            // ---------- 最大的资源 ----------
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("最大的 10 个资源", EditorStyles.boldLabel);

            var assets = new List<KeyValuePair<string, long>>();
            foreach (var pair in collect.logicToAssetPath)
                assets.Add(new KeyValuePair<string, long>(pair.Value, FileSize(pair.Value)));
            assets.Sort((a, b) => b.Value.CompareTo(a.Value));

            if (assets.Count == 0)
                EditorGUILayout.LabelField("（还没有任何资源）", EditorStyles.miniLabel);

            for (int i = 0; i < Math.Min(10, assets.Count); i++)
                RowWithPing(assets[i].Key, $"{FormatSize(assets[i].Value)}    {Short(assets[i].Key)}");

            EditorGUILayout.EndScrollView();
        }
    }

    // ============================================================
    // ③ 重名检测
    // ============================================================
    /// <summary>
    /// 问题检查：同包内重名 / 逻辑路径冲突 / 会被复制多份的资源 / 漏标（没有 AB 标记的资源）。
    ///
    /// 这四类都是"编辑器里看不出、运行时才炸"的问题，所以集中在一页，
    /// 每一条都能点一下直接在 Project 里定位。
    /// </summary>
    internal sealed class ABDuplicateView : ABAnalysisView
    {
        private Vector2 _scroll;

        public override void Draw(float availableHeight)
        {
            ABCollectResult collect = DrawTitle("问题检查");

            // ---------- 统计 ----------
            // 同包内重名：按 (包, 资源名) 分组，组内 ≥ 2 就是重名
            var sameName = new SortedDictionary<string, SortedDictionary<string, List<string>>>(StringComparer.Ordinal);

            foreach (ResMapEntry entry in collect.entries)
            {
                if (!sameName.TryGetValue(entry.bundle, out var names))
                {
                    names = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
                    sameName[entry.bundle] = names;
                }
                if (!names.TryGetValue(entry.asset, out List<string> logics))
                {
                    logics = new List<string>();
                    names[entry.asset] = logics;
                }
                logics.Add(entry.logic);
            }

            // ★ 先数清楚再画汇总 —— 否则汇总里永远是 0（数的时候画的那行已经过去了）
            int sameNameCount = 0;
            foreach (var bundlePair in sameName)
                foreach (var namePair in bundlePair.Value)
                    if (namePair.Value.Count >= 2) sameNameCount++;

            int logicClashCount = collect.duplicateLogicDetail.Count;
            int unmarkedCount = collect.unmarkedAssets.Count;
            ABDependencyReport report = ABDependencyCache.Get(collect);
            int sharedCount = report.sharedAssets.Count;

            EditorGUILayout.LabelField(
                $"同包内重名 {sameNameCount} 处 · 逻辑路径冲突 {logicClashCount} 处 · " +
                $"没有 AB 标记的资源 {unmarkedCount} 个 · 会被复制多份的资源 {sharedCount} 个",
                EditorStyles.miniLabel);

            if (sameNameCount + logicClashCount + unmarkedCount + sharedCount == 0)
                EditorGUILayout.HelpBox("没有发现问题。", MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(availableHeight - 56f));

            // ---------- ① 同一包内资源重名 ----------
            EditorGUILayout.LabelField("① 同一包内资源重名", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "同一个包里有两个同名资源 → LoadAsset(\"名字\") 取到哪个不确定，必须改名或拆包",
                EditorStyles.miniLabel);

            bool any = false;
            foreach (var bundlePair in sameName)
            {
                foreach (var namePair in bundlePair.Value)
                {
                    if (namePair.Value.Count < 2) continue;

                    any = true;

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField($"包 \"{bundlePair.Key}\"  →  {namePair.Key}（{namePair.Value.Count} 个）",
                        EditorStyles.boldLabel);

                    foreach (string logic in namePair.Value)
                        if (collect.logicToAssetPath.TryGetValue(logic, out string assetPath))
                            RowWithPing(assetPath, "    " + Short(assetPath) + "    逻辑路径 " + logic);
                }
            }
            if (!any)
                EditorGUILayout.LabelField("（无）", EditorStyles.miniLabel);

            // ---------- ② 逻辑路径冲突 ----------
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("② 逻辑路径冲突（同名不同扩展名）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "逻辑路径 = 相对资源根目录的路径去掉扩展名；所以 Hero/1001.prefab 和 Hero/1001.png 会撞成同一个逻辑名",
                EditorStyles.miniLabel);

            if (collect.duplicateLogicDetail.Count == 0)
                EditorGUILayout.LabelField("（无）", EditorStyles.miniLabel);

            foreach (var pair in Sorted(collect.duplicateLogicDetail))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField($"逻辑路径 \"{pair.Key}\" 被这几个文件撞了：", EditorStyles.boldLabel);

                foreach (string assetPath in pair.Value)
                    RowWithPing(assetPath, "    " + Short(assetPath));
            }

            // ---------- ③ 会被复制多份 ----------
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("③ 被多个包引用、又没分包（内容会被复制多份）", EditorStyles.boldLabel);

            if (report.sharedAssets.Count == 0)
            {
                EditorGUILayout.LabelField("（无）", EditorStyles.miniLabel);
            }
            else
            {
                long wasted = 0;

                foreach (var pair in Sorted(report.sharedAssets))
                {
                    long size = FileSize(pair.Key);
                    wasted += size * (pair.Value.Count - 1);

                    RowWithPing(pair.Key,
                        $"{Short(pair.Key)}    被 {string.Join("、", pair.Value)} 引用    " +
                        $"{FormatSize(size)} × {pair.Value.Count} 份");
                }

                EditorGUILayout.HelpBox(
                    $"合计多占约 {FormatSize(wasted)}（同一份内容在多个包里各存一份）。\n" +
                    "这不是\"名字\"问题，而是\"内容重复\"问题 —— 通常把它单独打成一个共享包最省。",
                    MessageType.Warning);
            }

            // ---------- ④ 漏标：没有任何 AB 标记的资源 ----------
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("④ 没有 AB 标记的资源（不会进包）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "这些资源在资源根目录下、却没有被任何包收走：" +
                "编辑器直读照样能读到，一出真机就是 FileNotExist",
                EditorStyles.miniLabel);

            if (collect.unmarkedAssets.Count == 0)
            {
                EditorGUILayout.LabelField("（无）", EditorStyles.miniLabel);
            }
            else
            {
                // 点一行就在 Project 里高亮定位（和上面几个视图同一套操作）
                foreach (string assetPath in collect.unmarkedAssets)
                    RowWithPing(assetPath, "    " + Short(assetPath));
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
