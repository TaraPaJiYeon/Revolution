// RevGMWindow.cs —— GM 指令面板（编辑器窗口，IMGUI；不依赖任何运行时 UI 系统）
//
// 【打开】菜单 Revolution.Tools/GM 指令面板（快捷键 Ctrl+Shift+G）
//
// 【怎么用（4 步）】
//   ① 在搜索框里边打边看联想（支持 前缀 / 连续子串 / 字符级缩写 三种命中，命中字高亮）
//   ② ↑/↓ 选联想项，Tab 用它补全（会自动补一个空格，接着打参数）
//   ③ 回车执行（或点「执行」按钮）—— 结果与耗时会显示在下方；Ctrl+回车 = 跳过高危确认
//   ④ 左边是分组树（命令名里的 `/` 自动长成树），点一条 → 右边看说明与参数；双击 = 填入输入框
//
// 【编辑模式 vs Play 模式】
//   · 编辑模式：只查看与联想（命令清单来自 [RevGMEntry] 注册入口的快照）—— 不执行
//   · Play 模式：读运行期注册表，可真执行（被测环境就是游戏本身，结果真实）
//
// 【为什么不用 UGUI】面板完全活在编辑器里：EditorWindow + IMGUI，不占运行时、不进包体。
//   与工程既有的 Editor\RevResourceSystem\ABTool 保持同一套写法（EditorWindow + EditorStyles）。

using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Revolution.Editor
{
    /// <summary>GM 指令面板。</summary>
    public sealed class RevGMWindow : EditorWindow
    {
        private const string SearchControl = "RevGMSearchField";
        private const int MaxSuggestions = 8;
        private const int MaxHistory = 50;

        // ── 样式（首次 OnGUI 时创建） ────────────────────────────────
        private static GUIStyle _richLabel;      // 支持富文本的标签（命中高亮）
        private static GUIStyle _richMini;
        private static GUIStyle _iconLabel;

        // ── 状态 ────────────────────────────────────────────────────
        private string _input = string.Empty;
        private string _suggestedFor;                                           // 上次算联想用的输入（变了才重算）
        private IReadOnlyList<RevGMCommand> _suggestions = Array.Empty<RevGMCommand>();
        private int _selectedIndex;
        private RevGMCommand _detail;                                           // 右侧详情
        private RevGMResult? _lastResult;
        private string _lastExecuted = string.Empty;
        private bool _showEntries;
        private bool _focusSearch = true;
        private Vector2 _treeScroll, _detailScroll, _historyScroll;
        private readonly List<HistoryItem> _history = new List<HistoryItem>(16);
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>(16);
        private readonly Dictionary<string, List<RevGMCommand>> _groups = new Dictionary<string, List<RevGMCommand>>(16);

        private struct HistoryItem
        {
            public string Text;
            public RevGMResult Result;
            public string Time;
        }

        // ============================================================
        // 打开 / 初始化
        // ============================================================

        /// <summary>打开面板</summary>
        [MenuItem("Revolution.Tools/GM 指令面板 %#g", false, 2)]
        public static void Open()
        {
            RevGMWindow window = GetWindow<RevGMWindow>("GM 指令");
            window.minSize = new Vector2(780f, 480f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("GM 指令");
            _focusSearch = true;
        }

        // ============================================================
        // 绘制
        // ============================================================

        private void OnGUI()
        {
            EnsureStyles();
            HandleKeyboard();                       // ★ 先消费键盘事件，再画搜索框（否则方向键会被输入框吃掉）

            DrawToolbar();
            DrawSearchRow();
            DrawSuggestions();

            float bodyHeight = Mathf.Max(150f, position.height - 340f);
            DrawBody(bodyHeight);

            EditorGUILayout.Space(2f);
            DrawResult();
            DrawHistory();
        }

        private static void EnsureStyles()
        {
            if (_richLabel != null) return;

            _richLabel = new GUIStyle(EditorStyles.label) { richText = true };
            _richMini = new GUIStyle(EditorStyles.miniLabel) { richText = true };
            _iconLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
        }

        // ── 工具条 ──────────────────────────────────────────────────
        private void DrawToolbar()
        {
            bool playing = RevGMEditorCatalog.IsPlaying;
            IReadOnlyList<RevGMCommand> commands = RevGMEditorCatalog.Commands;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label(playing ? "● Play 中（可执行）" : "○ 编辑模式（只查看 / 联想）",
                            EditorStyles.miniLabel, GUILayout.Width(170f));
            GUILayout.Label($"命令 {commands.Count} 条", EditorStyles.miniLabel, GUILayout.Width(90f));

            if (!playing)
                GUILayout.Label("想在编辑期看到命令清单：给注册方法加 [RevGMEntry]", EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            _showEntries = GUILayout.Toggle(_showEntries, "命令来源", EditorStyles.toolbarButton, GUILayout.Width(66f));

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(46f)))
            {
                RevGMEditorCatalog.Invalidate();
                _suggestedFor = null;
                Repaint();
            }

            if (GUILayout.Button("帮助", EditorStyles.toolbarButton, GUILayout.Width(46f))) ShowHelp();

            EditorGUILayout.EndHorizontal();

            if (_showEntries) DrawEntries();
        }

        private void DrawEntries()
        {
            IReadOnlyList<RevGMEditorCatalog.EntryInfo> entries = RevGMEditorCatalog.Entries;

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有找到 [RevGMEntry] 注册入口。\n" +
                    "· 编辑模式：给注册方法加上 [RevGMEntry] 就能在这里看到命令清单（方法里只做 RevGM.Register）\n" +
                    "· Play 模式：命令来自游戏自己的注册，不需要这个标记",
                    MessageType.Info);
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                RevGMEditorCatalog.EntryInfo entry = entries[i];

                if (string.IsNullOrEmpty(entry.Error))
                    EditorGUILayout.LabelField("· " + entry.Display, EditorStyles.miniLabel);
                else
                    EditorGUILayout.HelpBox("· " + entry.Display + "\n  调用失败：" + entry.Error, MessageType.Error);
            }
        }

        // ── 搜索行 ──────────────────────────────────────────────────
        private void DrawSearchRow()
        {
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label("命令", GUILayout.Width(32f));

            GUI.SetNextControlName(SearchControl);

            EditorGUI.BeginChangeCheck();
            string typed = EditorGUILayout.TextField(_input, GUILayout.Height(20f));
            if (EditorGUI.EndChangeCheck())
            {
                _input = typed;
                _selectedIndex = 0;
                _suggestedFor = null;
            }

            if (_focusSearch)
            {
                EditorGUI.FocusTextInControl(SearchControl);
                _focusSearch = false;
            }

            if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(40f), GUILayout.Height(20f)))
            {
                _input = string.Empty;
                _selectedIndex = 0;
                _suggestedFor = null;
                _focusSearch = true;
            }

            bool playing = RevGMEditorCatalog.IsPlaying;
            using (new EditorGUI.DisabledScope(!playing))
            {
                if (GUILayout.Button(playing ? "执行 (Enter)" : "需 Play 才能执行", GUILayout.Width(120f), GUILayout.Height(20f)))
                    ExecuteInput(false);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── 联想列表 ────────────────────────────────────────────────
        private void DrawSuggestions()
        {
            if (_suggestedFor != _input)
            {
                _suggestions = RevGMEditorCatalog.Suggest(_input, MaxSuggestions);
                _suggestedFor = _input;
                if (_selectedIndex >= _suggestions.Count) _selectedIndex = 0;
            }

            if (_suggestions.Count == 0)
            {
                if (RevGMEditorCatalog.Commands.Count > 0 && _input.Trim().Length > 0)
                    EditorGUILayout.LabelField("（没有匹配的命令 —— 换个词试试，或者点左边分组树看看）", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            for (int i = 0; i < _suggestions.Count; i++)
            {
                RevGMCommand command = _suggestions[i];
                bool selected = i == _selectedIndex;

                Rect row = EditorGUILayout.GetControlRect(false, 18f);

                if (selected) EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.90f, 0.22f));

                Rect nameRect = new Rect(row.x + 4f, row.y, row.width * 0.52f, row.height);
                Rect hintRect = new Rect(row.x + row.width * 0.52f, row.y, row.width * 0.48f - 4f, row.height);

                GUI.Label(nameRect, Highlight(command.Name, _input), _richLabel);
                GUI.Label(hintRect, ArgsHint(command) + command.Description, _richMini);

                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                {
                    _selectedIndex = i;
                    _detail = command;

                    if (Event.current.clickCount >= 2) FillInput(command.Name);      // 双击填入

                    Event.current.Use();
                    Repaint();
                }
            }

            EditorGUILayout.EndVertical();
        }

        // ── 主体：左树 + 右详情 ─────────────────────────────────────
        private void DrawBody(float height)
        {
            EditorGUILayout.BeginHorizontal();

            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll, EditorStyles.helpBox,
                                                          GUILayout.Width(position.width * 0.46f), GUILayout.Height(height));
            DrawTree();
            EditorGUILayout.EndScrollView();

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, EditorStyles.helpBox, GUILayout.Height(height));
            DrawDetail();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTree()
        {
            IReadOnlyList<RevGMCommand> commands = RevGMEditorCatalog.Commands;

            if (commands.Count == 0)
            {
                EditorGUILayout.HelpBox(EmptyHint(), MessageType.Info);
                return;
            }

            BuildGroups(commands);

            List<string> groups = new List<string>(_groups.Keys);
            groups.Sort(StringComparer.Ordinal);

            for (int i = 0; i < groups.Count; i++)
            {
                string group = groups[i];
                string title = (string.IsNullOrEmpty(group) ? "通用" : group) + "  (" + _groups[group].Count + ")";

                if (!_foldouts.TryGetValue(group, out bool open)) open = true;

                _foldouts[group] = EditorGUILayout.Foldout(open, title, true);

                if (!_foldouts[group]) continue;

                EditorGUI.indentLevel++;

                List<RevGMCommand> list = _groups[group];
                for (int j = 0; j < list.Count; j++)
                {
                    RevGMCommand command = list[j];

                    EditorGUILayout.BeginHorizontal();

                    string mark = command.IsHighRisk ? "⚠ " : command.IsHidden ? "（隐藏）" : string.Empty;
                    if (GUILayout.Button(mark + command.BaseName, EditorStyles.miniLabel))
                    {
                        _detail = command;
                        if (Event.current.clickCount >= 2) FillInput(command.Name);
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawDetail()
        {
            if (_detail == null)
            {
                EditorGUILayout.LabelField("左边点一条命令 → 这里显示说明与参数", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("提示：↑/↓ 选联想项，Tab 补全，回车执行，Ctrl+回车跳过高危确认", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            EditorGUILayout.LabelField(_detail.Name, EditorStyles.boldLabel);

            if (_detail.IsHighRisk) EditorGUILayout.HelpBox("⚠ 高危命令：执行前会二次确认（Ctrl+回车可跳过）", MessageType.Warning);
            if (_detail.IsHidden) EditorGUILayout.HelpBox("隐藏命令：不出现在联想列表里，但可以直接执行", MessageType.Info);

            if (!string.IsNullOrEmpty(_detail.Description))
                EditorGUILayout.LabelField(_detail.Description, EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("参数", EditorStyles.boldLabel);

            if (_detail.Args.Length == 0)
            {
                EditorGUILayout.LabelField("（没有参数说明 —— 框架不做执行前校验）", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < _detail.Args.Length; i++)
                {
                    RevGMArg arg = _detail.Args[i];
                    EditorGUILayout.LabelField("· " + arg.Describe(), EditorStyles.miniLabel);

                    if (arg.Candidates != null && arg.Candidates.Length > 0)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.BeginHorizontal();
                        for (int c = 0; c < arg.Candidates.Length; c++)
                        {
                            if (GUILayout.Button(arg.Candidates[c], EditorStyles.miniButton, GUILayout.Width(110f)))
                                FillInput(_detail.Name + " " + arg.Candidates[c]);
                        }

                        EditorGUILayout.EndHorizontal();
                        EditorGUI.indentLevel--;
                    }
                }
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("填入输入框", GUILayout.Width(100f))) FillInput(_detail.Name);

            if (GUILayout.Button("复制命令名", GUILayout.Width(100f)))
            {
                EditorGUIUtility.systemCopyBuffer = _detail.Name;
                ShowNotification(new GUIContent("已复制：" + _detail.Name));
            }

            using (new EditorGUI.DisabledScope(!RevGMEditorCatalog.IsPlaying))
            {
                if (GUILayout.Button("执行", GUILayout.Width(70f)))
                {
                    FillInput(_detail.Name);
                    ExecuteInput(false);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── 结果 / 历史 ─────────────────────────────────────────────
        private void DrawResult()
        {
            if (!_lastResult.HasValue) return;

            RevGMResult result = _lastResult.Value;

            string title = result.Success ? "✔ 执行成功" : "✘ 执行失败";
            string body = _lastExecuted + "\n" + title + $"（{result.ElapsedMs:F2} ms）\n{result.Message}";

            EditorGUILayout.HelpBox(body, result.Success ? MessageType.Info : MessageType.Error);
        }

        private void DrawHistory()
        {
            if (_history.Count == 0) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("历史（点一条重新填入）", EditorStyles.miniLabel, GUILayout.Width(150f));

            if (GUILayout.Button("清空历史", EditorStyles.miniButton, GUILayout.Width(70f))) _history.Clear();

            EditorGUILayout.EndHorizontal();

            _historyScroll = EditorGUILayout.BeginScrollView(_historyScroll, GUILayout.Height(70f));

            for (int i = 0; i < _history.Count; i++)
            {
                HistoryItem item = _history[i];

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(item.Time, EditorStyles.miniLabel, GUILayout.Width(58f));
                GUILayout.Label(item.Result.Success ? "✔" : "✘", _iconLabel, GUILayout.Width(16f));

                if (GUILayout.Button(item.Text, EditorStyles.miniLabel)) FillInput(item.Text);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        // ============================================================
        // 键盘 / 执行
        // ============================================================

        private void HandleKeyboard()
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown) return;
            if (GUI.GetNameOfFocusedControl() != SearchControl) return;

            switch (current.keyCode)
            {
                case KeyCode.DownArrow:
                    if (_suggestions.Count > 0) _selectedIndex = (_selectedIndex + 1) % _suggestions.Count;
                    current.Use();
                    Repaint();
                    break;

                case KeyCode.UpArrow:
                    if (_suggestions.Count > 0) _selectedIndex = (_selectedIndex - 1 + _suggestions.Count) % _suggestions.Count;
                    current.Use();
                    Repaint();
                    break;

                case KeyCode.Tab:
                    if (_suggestions.Count > 0 && _selectedIndex < _suggestions.Count)
                    {
                        _detail = _suggestions[_selectedIndex];
                        FillInput(_detail.Name);
                    }

                    current.Use();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    ExecuteInput(current.control || current.command);
                    current.Use();
                    break;

                case KeyCode.Escape:
                    _input = string.Empty;
                    _suggestedFor = null;
                    _selectedIndex = 0;
                    current.Use();
                    Repaint();
                    break;
            }
        }

        private void ExecuteInput(bool force)
        {
            string line = _input?.Trim();

            if (string.IsNullOrEmpty(line))
            {
                ShowNotification(new GUIContent("先输入一条命令"));
                return;
            }

            if (!force && IsHighRisk(line))
            {
                if (!EditorUtility.DisplayDialog("确认执行高危 GM 命令", line + "\n\n这条命令被标记为高危（RevGMFlags.HighRisk）。确定执行？",
                                                 "执行", "取消"))
                    return;
            }

            RevGMResult result = RevGMEditorCatalog.Execute(line);

            _lastResult = result;
            _lastExecuted = line;

            _history.Insert(0, new HistoryItem { Text = line, Result = result, Time = DateTime.Now.ToString("HH:mm:ss") });
            if (_history.Count > MaxHistory) _history.RemoveAt(_history.Count - 1);

            Repaint();
        }

        private static bool IsHighRisk(string line)
        {
            int space = line.IndexOf(' ');
            string name = space < 0 ? line : line.Substring(0, space);

            IReadOnlyList<RevGMCommand> commands = RevGMEditorCatalog.Commands;

            // 完整名优先
            for (int i = 0; i < commands.Count; i++)
                if (string.Equals(commands[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return commands[i].IsHighRisk;

            // 退化为"末段名唯一命中"
            for (int i = 0; i < commands.Count; i++)
                if (string.Equals(commands[i].BaseName, name, StringComparison.OrdinalIgnoreCase))
                    return commands[i].IsHighRisk;

            return false;
        }

        private void FillInput(string text)
        {
            _input = text;
            _suggestedFor = null;
            _selectedIndex = 0;
            _focusSearch = true;
            Repaint();
        }

        // ============================================================
        // 小工具
        // ============================================================

        private void BuildGroups(IReadOnlyList<RevGMCommand> commands)
        {
            // 每次重绘重建分组（命令数是几十~几百条，成本可忽略；少一份状态就少一处不一致）
            _groups.Clear();

            for (int i = 0; i < commands.Count; i++)
            {
                RevGMCommand command = commands[i];
                if (!_groups.TryGetValue(command.Group, out List<RevGMCommand> list))
                {
                    list = new List<RevGMCommand>(8);
                    _groups[command.Group] = list;
                }

                list.Add(command);
            }
        }

        private static string ArgsHint(RevGMCommand command)
        {
            if (command.Args.Length == 0) return string.Empty;

            StringBuilder builder = new StringBuilder(24);
            for (int i = 0; i < command.Args.Length; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append('<').Append(command.Args[i].Name).Append('>');
            }

            return builder.Append("   ").ToString();
        }

        private static string Highlight(string text, string query)
        {
            if (string.IsNullOrEmpty(query) || !RevGMMatcher.TryMatch(text, query, out int[] positions) || positions == null)
                return text;

            StringBuilder builder = new StringBuilder(text.Length + positions.Length * 26);
            int current = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (current < positions.Length && positions[current] == i)
                {
                    builder.Append("<b><color=#FFB300>").Append(text[i]).Append("</color></b>");
                    current++;
                }
                else
                {
                    builder.Append(text[i]);
                }
            }

            return builder.ToString();
        }

        private static string EmptyHint()
        {
            if (RevGMEditorCatalog.IsPlaying)
                return "还没有注册任何 GM 命令。\n在启动期用一行注册：\nRevGM.Register(\"经济/加金币\", \"给玩家加金币\", args => AddGold(args.Int(0)), RevGMArg.Int(\"数量\", 1000));";

            return "编辑模式下暂无命令清单。\n给注册方法加 [RevGMEntry]（方法里只做 RevGM.Register），面板就能在编辑期读到命令；\n或者按 Play 进游戏后回来看（那时读的是运行期真实注册表）。";
        }

        private static void ShowHelp()
        {
            EditorUtility.DisplayDialog(
                "GM 指令面板 · 用法",
                "① 搜索框里边打边看联想（前缀 / 连续子串 / 字符级缩写都能命中，命中字高亮）\n" +
                "② ↑/↓ 选择联想项；Tab 用它补全（自动补一个空格，接着打参数）\n" +
                "③ 回车执行；Ctrl+回车 跳过高危确认；Esc 清空输入\n" +
                "④ 左树点命令看详情（双击 = 填入输入框）；右侧可直接执行 / 复制命令名\n" +
                "⑤ 枚举参数会列出候选值，点一下就填进输入框\n\n" +
                "编辑模式只查看与联想；进 Play 后才能真的执行（那时被测环境就是游戏本身）。",
                "知道了");
        }
    }
}
