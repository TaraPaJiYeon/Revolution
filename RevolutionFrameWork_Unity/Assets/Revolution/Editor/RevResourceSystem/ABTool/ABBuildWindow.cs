// ============================================================
// ABBuildWindow.cs —— LiteAB 可视化打包窗口
//
// 位置：Editor\资源加载\ABTool\
//
// 【打开方式】菜单 Tools/资源/LiteAB 打包工具（另有 Tools/资源/AB 分包浏览 直达浏览页签）
//
// 【两个页签】
//   · 打包      ：收集并校验 → 扫描并生成映射（ResMap + RevResPath + 音效目录常量）→ 一键打包 → 看报告
//   · 分包浏览  ：看/改分包（包列表 + 包内资源 + 新建/重命名/删除 + 把资源拖进包），见 ABBundleBrowserView
// ============================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Revolution.Editor
{
    public class ABBuildWindow : EditorWindow
    {
        // 窗口共享状态（做成 static，脚本重编译后不丢）
        private static ABCollectResult _collect;
        private static ABValidateResult _validate;
        private static ABDependencyReport _dependency;
        private static ABBuildResult _build;

        /// <summary>
        /// 收集校验时的"同步计数"（ABMarkerWatcher.SyncCount）。
        /// 与当前计数不一致 = 这之后项目里的分包又变过 → 界面上那份校验结果是旧数据，要提示重算。
        /// </summary>
        private static int _collectSyncStamp;

        private Vector2 _scroll;
        private int _tab;                                          // 0=打包 1=分包 2=依赖 3=体积 4=检查
        private static readonly string[] Tabs = { "打包", "分包", "依赖", "体积", "检查" };

        private readonly ABBundleBrowserView _browser = new ABBundleBrowserView();
        private readonly ABDependencyView _dependencyView = new ABDependencyView();
        private readonly ABSizeView _sizeView = new ABSizeView();
        private readonly ABDuplicateView _duplicateView = new ABDuplicateView();

        [MenuItem("Revolution.Tools/资源/LiteAB 打包工具", false, 1)]
        public static void Open()
        {
            var win = GetWindow<ABBuildWindow>("LiteAB 打包工具");
            win.minSize = new Vector2(520, 620);
        }

        /// <summary>只开分包浏览（不用先开打包窗口再切页签）</summary>
        [MenuItem("Revolution.Tools/资源/AB 分包浏览", false, 2)]
        private static void OpenBrowser()
        {
            var win = GetWindow<ABBuildWindow>("LiteAB 打包工具");
            win.minSize = new Vector2(520, 620);
            win._tab = 1;
        }

        // 注册进 ABMarkerWatcher：只有窗口开着它才干活（关掉窗口后自动同步完全静默）
        private void OnEnable() => ABMarkerWatcher.Attach(this);
        private void OnDisable() => ABMarkerWatcher.Detach(this);

        private void OnGUI()
        {
            GuardAutoSync();

            // 页签放在滚动区【外面】：后面的页签各自管自己的滚动（两层滚动套在一起很难用）
            _tab = GUILayout.Toolbar(_tab, Tabs, GUILayout.Height(22));

            float viewHeight = position.height - 34f;              // 减掉页签那一行

            switch (_tab)
            {
                case 0:                                            // 打包
                    _scroll = EditorGUILayout.BeginScrollView(_scroll);

                    DrawConfigArea();
                    EditorGUILayout.Space(8);
                    DrawActionArea();
                    EditorGUILayout.Space(8);
                    DrawValidateArea();
                    EditorGUILayout.Space(8);
                    DrawResultArea();

                    EditorGUILayout.EndScrollView();
                    break;

                case 1: _browser.Draw(viewHeight); break;           // 分包
                case 2: _dependencyView.Draw(viewHeight); break;    // 依赖
                case 3: _sizeView.Draw(viewHeight); break;          // 体积
                default: _duplicateView.Draw(viewHeight); break;    // 重名
            }
        }

        // ==================== 自动同步 ====================

        /// <summary>
        /// 自动同步的"消费点"（兜底那一道）：项目里改了分包标记 → 让共享扫描缓存失效，
        /// 四个视图（分包 / 依赖 / 体积 / 检查）下次绘制就是新数据，不用再手动点刷新。
        ///
        /// ★ 主驱动其实是 ABMarkerWatcher 自己的 tick（窗口开着就每帧跑一次，不受"是否重绘"影响）；
        ///   这里再问一次是**双保险** —— 任一条路都生效，效果一致（重复调用是幂等的）。
        /// ★ 拖拽期间不消费：重扫会让左右两个列表在鼠标下方重建，投放位置会和高亮错位。
        ///   这个"去抖 + 拖拽保护"的判断全在 ABMarkerWatcher 里，窗口只负责问一次。
        /// ★ 只失效缓存，**不写任何文件** —— ResMap / RevResPath 仍由按钮显式生成。
        /// </summary>
        private static void GuardAutoSync()
            => ABMarkerWatcher.SyncIfDue(DragAndDrop.paths != null && DragAndDrop.paths.Length > 0);

        /// <summary>收集 + 校验，并记下"这份结果对应的同步时刻"（用于提示结果是否已过期）</summary>
        private static void CollectAndValidate()
        {
            _collect = ABCollector.Collect();
            _validate = ABValidator.Validate(_collect);
            _collectSyncStamp = ABMarkerWatcher.SyncCount;
        }

        // ==================== 配置区 ====================

        private void DrawConfigArea()
        {
            EditorGUILayout.LabelField("① 打包配置", EditorStyles.boldLabel);

            ABBuildConfig cfg = ABBuildConfig.Instance;

            // ---------- 资源根目录：可拖拽的文件夹槽 + "选择…"按钮（★ 代码里没有任何默认路径）----------
            using (new EditorGUILayout.HorizontalScope())
            {
                // 用 DefaultAsset 画一个"文件夹槽"：可以把 Project 里的文件夹直接拖进来
                // ★ 空路径不能丢给 LoadAssetAtPath（Unity 会报 "path must start with Assets/"）
                var current = cfg.HasResRoot
                    ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(cfg.GetResRoot())
                    : null;
                var picked = (DefaultAsset)EditorGUILayout.ObjectField(
                    "资源根目录", current, typeof(DefaultAsset), false);

                if (picked != null)
                {
                    string p = AssetDatabase.GetAssetPath(picked);
                    if (AssetDatabase.IsValidFolder(p) && p != cfg.resRoot)
                    {
                        cfg.resRoot = p;
                        cfg.ApplyToRuntime();
                        SaveConfig(cfg);                 // ★ 落盘：不然重启后这个设置就没了
                        RegeneratePathConsts();          // 根目录变了 → 旧常量全部作废，重生成
                    }
                }

                // "选择…"按钮：弹系统目录框，但只允许选 Assets 内的目录
                if (GUILayout.Button("选择…", GUILayout.Width(60)))
                {
                    string abs = EditorUtility.OpenFolderPanel("选择资源根目录", "Assets", "");
                    if (!string.IsNullOrEmpty(abs))
                    {
                        // 绝对路径 → "Assets/xxx"（只有这种形式才能被 AssetDatabase 使用）
                        string projectRoot = System.IO.Directory.GetParent(Application.dataPath)
                                                 .FullName.Replace('\\', '/');
                        abs = abs.Replace('\\', '/');

                        if (abs.StartsWith(projectRoot + "/"))
                        {
                            cfg.resRoot = abs.Substring(projectRoot.Length + 1);
                            cfg.ApplyToRuntime();
                            SaveConfig(cfg);             // ★ 落盘：不然重启后这个设置就没了
                            RegeneratePathConsts();      // 根目录变了 → 旧常量全部作废，重生成
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("无效目录", "请选择工程 Assets 目录内的文件夹", "确定");
                        }
                    }
                }
            }

            if (!cfg.HasResRoot)
                EditorGUILayout.HelpBox(
                    "还没设置「资源根目录」—— 它下面的资源才会按'相对路径'生成逻辑名，" +
                    "RevResPath 常量、编辑器直读都依赖它。\n" +
                    "把 Assets 里的文件夹拖到上面的槽里，或点「选择…」。",
                    MessageType.Warning);

            EditorGUILayout.Space(6);

            // ---------- 音效目录：音效系统（RevSoundSystem）加载音效 / BGM 的地方 ----------
            EditorGUILayout.LabelField("音效目录（RevSoundSystem）", EditorStyles.boldLabel);

            if (!cfg.HasResRoot)
            {
                EditorGUILayout.HelpBox(
                    "先设置上面的「资源根目录」：音效目录是它下面的子目录，" +
                    "存的是相对路径（换工程、换盘符都不会失效）。",
                    MessageType.Info);
            }
            else
            {
                DrawSubFolderRow(cfg, "音效根目录", cfg.GetSfxRoot(), ABBuildConfig.DefaultSfxRoot,
                                 v => cfg.sfxRoot = v);

                DrawSubFolderRow(cfg, "BGM 根目录", cfg.GetBgmRoot(), ABBuildConfig.DefaultBgmRoot,
                                 v => cfg.bgmRoot = v);

                if (!AssetDatabase.IsValidFolder(cfg.GetSfxFolderPath()) ||
                    !AssetDatabase.IsValidFolder(cfg.GetBgmFolderPath()))
                {
                    EditorGUILayout.HelpBox(
                        "目录还不存在（把音频放进去就行）：\n" +
                        cfg.GetSfxFolderPath() + "\n" + cfg.GetBgmFolderPath(),
                        MessageType.Info);
                }

                // 子目录是支持的（音效名里带子目录即可）—— 把现有子目录列出来，省得去 Project 窗口翻
                DrawSubFolderHint(cfg.GetSfxFolderPath(), "音效目录");
                DrawSubFolderHint(cfg.GetBgmFolderPath(), "BGM 目录");
            }

            EditorGUILayout.Space(4);

            // ---------- 其余配置项：遍历 SerializedObject 自动绘制（跳过已单独画的 resRoot / 音效目录）----------
            var so = new SerializedObject(cfg);
            so.Update();

            SerializedProperty prop = so.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "resRoot" || prop.name == "sfxRoot" || prop.name == "bgmRoot" ||
                    prop.name == "m_Script") continue;
                EditorGUILayout.PropertyField(prop, true);
            }
            // 只在真的改了才落盘（ApplyModifiedProperties 返回 true = 有属性确实变了）
            if (so.ApplyModifiedProperties()) SaveConfig(cfg);
        }

        /// <summary>
        /// 画一行"资源根目录下的子目录"选择：拖文件夹进来 / 点「选择…」/ 点「默认」。
        /// ★ 存的是**相对资源根目录的逻辑段**（如 Audio/Sfx）—— 换工程、换盘符都不失效，
        ///   且与运行时 ResManager 的 rootPath、ResMap 里的逻辑名完全一致。
        /// ★ 必须是资源根目录**之内**的目录：外面的目录编辑器直读拼不出路径，
        ///   RevResPath 也不会为它生成常量（真机与编辑器行为会割裂）。
        /// </summary>
        private void DrawSubFolderRow(ABBuildConfig cfg, string label, string current, string defaultSegment,
                                      System.Action<string> apply)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // 文件夹槽：空路径不能丢给 LoadAssetAtPath（Unity 会报 "path must start with Assets/"）
                string full = current.Length > 0 ? cfg.GetResRoot() + "/" + current : "";
                var currentAsset = full.Length > 0
                    ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(full)
                    : null;

                var picked = (DefaultAsset)EditorGUILayout.ObjectField(
                    label, currentAsset, typeof(DefaultAsset), false);

                if (picked != null)
                {
                    string p = AssetDatabase.GetAssetPath(picked);
                    if (AssetDatabase.IsValidFolder(p)) SetSubFolder(cfg, current, apply, p);
                }

                if (GUILayout.Button("选择…", GUILayout.Width(60)))
                {
                    string abs = EditorUtility.OpenFolderPanel("选择" + label, "Assets", "");
                    if (!string.IsNullOrEmpty(abs))
                    {
                        string projectRoot = System.IO.Directory.GetParent(Application.dataPath)
                                                 .FullName.Replace('\\', '/');
                        abs = abs.Replace('\\', '/');

                        if (abs.StartsWith(projectRoot + "/"))
                            SetSubFolder(cfg, current, apply, abs.Substring(projectRoot.Length + 1));
                        else
                            EditorUtility.DisplayDialog("无效目录", "请选择工程 Assets 目录内的文件夹", "确定");
                    }
                }

                if (GUILayout.Button("默认", GUILayout.Width(46)))
                {
                    apply(defaultSegment);
                    SaveConfig(cfg);
                    RegenerateSoundPathConsts();
                }
            }
        }

        /// <summary>
        /// 列出某个目录下的子目录（递归）—— 音效名里可以带子目录，例如 <c>RevSound.Play("UI/ui_click")</c>，
        /// 所以把可用的子目录直接摆出来，省得使用者去 Project 窗口里一层层翻。
        /// </summary>
        private static void DrawSubFolderHint(string fullFolder, string label)
        {
            if (fullFolder.Length == 0 || !AssetDatabase.IsValidFolder(fullFolder)) return;

            List<string> subs = ResPathNaming.CollectFolders(fullFolder);       // 递归，含多级
            if (subs.Count == 0) return;

            int show = Mathf.Min(subs.Count, 8);
            string list = string.Join("、", subs.GetRange(0, show)) + (subs.Count > show ? " …" : "");

            EditorGUILayout.HelpBox(
                $"{label}下已有 {subs.Count} 个子目录：{list}\n" +
                "调用时把子目录写进名字里就行（支持多级）：RevSound.Play(\"" + subs[0] + "/ui_click\");",
                MessageType.None);
        }

        /// <summary>把"工程内的路径"转成相对资源根目录的逻辑段；不合法 / 没变化就什么都不做</summary>
        private static void SetSubFolder(ABBuildConfig cfg, string current, System.Action<string> apply, string assetPath)
        {
            string root = cfg.GetResRoot();
            string normalized = assetPath.Replace('\\', '/').TrimEnd('/');

            if (!normalized.StartsWith(root + "/", System.StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("无效目录",
                    "音效目录必须放在「资源根目录」之内：\n" + root + "\n\n" +
                    "放在外面的话：编辑器直读拼不出路径，RevResPath 也不会为它生成常量。", "确定");
                return;
            }

            string segment = cfg.NormalizeSubRoot(normalized);
            if (segment.Length == 0 || segment == cfg.NormalizeSubRoot(current)) return;    // 没变化

            apply(segment);
            SaveConfig(cfg);
            RegenerateSoundPathConsts();
        }

        /// <summary>
        /// 音效目录变了 → 重新生成 RevSoundPath.cs（音效系统读的就是它）。
        /// ★ 同样必须延后一帧：Generate 会写 .cs 并触发脚本编译，OnGUI 绘制过程中不允许编译。
        /// </summary>
        private static void RegenerateSoundPathConsts()
            => EditorApplication.delayCall += () => ABSoundPathGenerator.Generate();

        /// <summary>
        /// 把配置改动落盘。
        /// ★ 必须显式保存：改 ScriptableObject 的字段只是改了"内存里的那个对象"，
        ///   不落盘的话，Unity 下次重启 / 重编译就可能把它丢掉 ——
        ///   表现就是"明明在窗口里配了，回头却像没配过"（resRoot 为空正是这么来的）。
        /// </summary>
        private static void SaveConfig(ABBuildConfig cfg)
        {
            EditorUtility.SetDirty(cfg);

            // 延后一帧真正写盘：OnGUI 绘制期间不做资产写入更稳（和 RegeneratePathConsts 同理）
            EditorApplication.delayCall += AssetDatabase.SaveAssets;
        }

        // ==================== 操作区 ====================

        private void DrawActionArea()
        {
            EditorGUILayout.LabelField("② 操作", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                // 只收集 + 校验（不改任何东西）
                if (GUILayout.Button("收集并校验", GUILayout.Height(28)))
                    CollectAndValidate();

                // 扫描已有 AB 标记 → 生成 ResMap + RevResPath（只读，不改你的分包）
                if (GUILayout.Button("扫描并生成映射", GUILayout.Height(28)))
                {
                    CollectAndValidate();
                    ABManifestWriter.WriteResMap(_collect);
                    ABResPathGenerator.Generate();
                    AssetDatabase.Refresh();
                }

                // 一键打包
                if (GUILayout.Button("一键打包", GUILayout.Height(28)))
                    DoOneClickBuild();
            }

            // 路径常量单独一行：它只依赖"资源根目录下的文件夹结构"，和 AB 分包无关
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("生成路径常量", GUILayout.Height(24)))
                    ABResPathGenerator.Generate();

                if (GUILayout.Button("打开 RevResPath.cs", GUILayout.Height(24)))
                    OpenPathCode();
            }

            // 音效目录常量单独一行：它由①配置里的「音效目录」决定（改了会自动重生成，这里给手动入口）
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("生成音效目录常量", GUILayout.Height(24)))
                    ABSoundPathGenerator.Generate();

                if (GUILayout.Button("打开 RevSoundPath.cs", GUILayout.Height(24)))
                    OpenSoundPathCode();
            }
        }

        /// <summary>
        /// 资源根目录变了 → 旧常量全部作废，重生成一次。
        /// ★ 必须延后一帧：Generate 会写 .cs 文件并触发脚本编译，
        ///   而 Unity 不允许在 OnGUI 绘制过程中编译 —— 直接调会被拒绝/报错。
        /// </summary>
        private static void RegeneratePathConsts()
            => EditorApplication.delayCall += () => ABResPathGenerator.Generate();

        /// <summary>在 IDE 里打开生成好的常量类（顺便看看有哪些目录可用）</summary>
        private static void OpenPathCode()
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ABBuildSetting.ResPathCodePath);
            if (obj == null)
            {
                EditorUtility.DisplayDialog("还没生成",
                    "RevResPath.cs 还不存在。\n先点「生成路径常量」。", "好");
                return;
            }
            AssetDatabase.OpenAsset(obj);
        }

        /// <summary>在 IDE 里打开生成好的音效目录常量类（音效系统加载音效/BGM 的目录就在里面）</summary>
        private static void OpenSoundPathCode()
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ABBuildSetting.SoundPathCodePath);
            if (obj == null)
            {
                EditorUtility.DisplayDialog("还没生成",
                    "RevSoundPath.cs 还不存在。\n先点「生成音效目录常量」。", "好");
                return;
            }
            AssetDatabase.OpenAsset(obj);
        }

        /// <summary>一键打包流水线：把六个步骤串起来</summary>
        private void DoOneClickBuild()
        {
            if (!ABBuildConfig.Instance.HasResRoot)
            {
                EditorUtility.DisplayDialog("还没设置资源根目录",
                    "请先在窗口顶部设置「资源根目录」（拖拽文件夹或点「选择…」）。\n\n" +
                    "逻辑路径是按它算出来的 —— 没设置就打不出能用的包。", "好");
                return;
            }

            // 1. 收集 AB 标记 + 2. 校验：有 error 就中止（避免打出坏包）
            CollectAndValidate();
            if (!_validate.CanBuild)
            {
                Debug.LogError($"[LiteAB] 校验未通过，共 {_validate.errors.Count} 个错误，已中止打包");
                return;
            }

            // 3. 打包
            _build = ABBuilderCore.Build();
            if (!_build.success)
            {
                Debug.LogError("[LiteAB] 打包失败");
                return;
            }

            // 4. 依赖分析
            _dependency = ABDependencyAnalyzer.Analyze(_build.manifest.raw);

            // 5. 写 ResMap + 产物清单
            int mapCount = ABManifestWriter.WriteResMap(_collect);
            ABManifestWriter.WriteBuildManifest(_build);

            // 6. 生成 RevResPath 常量类 + 可选拷贝到 StreamingAssets
            ABResPathGenerator.Generate();
            ABBuilderCore.CopyToStreamingAssets(_build.outputDir);

            AssetDatabase.Refresh();
            Debug.Log($"[LiteAB] 打包完成：{_build.manifest.allBundles.Length} 个包，映射 {mapCount} 条 → {_build.outputDir}");
        }

        // ==================== 校验报告 ====================

        private void DrawValidateArea()
        {
            if (_validate == null) return;

            EditorGUILayout.LabelField("③ 校验结果", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"包：{_validate.bundleCount} 个    资源：{_validate.assetCount} 个    " +
                $"体积：{_validate.totalBytes / 1024 / 1024} MB",
                MessageType.Info);

            // 这份结果属于"点按钮那一刻"：之后项目里若又改过分包（自动同步过），它就已经过期了 ——
            // 说清楚，免得看着旧报告做决定
            if (ABMarkerWatcher.SyncCount != _collectSyncStamp)
                EditorGUILayout.HelpBox(
                    $"注意：这之后项目里的分包标记又变过（自动同步 {ABMarkerWatcher.SyncCount - _collectSyncStamp} 次），" +
                    "下面这份校验结果是旧数据 —— 点「收集并校验」重新算一遍。",
                    MessageType.Warning);

            foreach (string e in _validate.errors)
                EditorGUILayout.HelpBox(e, MessageType.Error);      // 红色：必须修

            foreach (string w in _validate.warnings)
                EditorGUILayout.HelpBox(w, MessageType.Warning);    // 黄色：建议修
        }

        // ==================== 打包 / 依赖报告 ====================

        private void DrawResultArea()
        {
            if (_build != null)
            {
                EditorGUILayout.LabelField("④ 打包结果", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    _build.success ? $"成功 → {_build.outputDir}" : "失败",
                    _build.success ? MessageType.Info : MessageType.Error);
            }

            if (_dependency == null) return;

            EditorGUILayout.LabelField("⑤ 依赖报告", EditorStyles.boldLabel);

            foreach (var kv in _dependency.directDeps)
                if (kv.Value.Length > 0)
                    EditorGUILayout.LabelField($"{kv.Key} 依赖", string.Join(", ", kv.Value));

            if (_dependency.sharedAssets.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"发现 {_dependency.sharedAssets.Count} 个被多个包共享的资源（会被复制多份，体积膨胀）",
                    MessageType.Warning);

                foreach (var kv in _dependency.sharedAssets)
                    EditorGUILayout.LabelField(ShortPath(kv.Key), string.Join(", ", kv.Value));
            }

            foreach (string c in _dependency.circularBundles)
                EditorGUILayout.HelpBox($"循环依赖：{c}", MessageType.Error);
        }

        /// <summary>把长路径缩短显示（窗口宽度有限）</summary>
        private static string ShortPath(string p)
            => p.StartsWith("Assets/") ? p.Substring("Assets/".Length) : p;
    }
}
