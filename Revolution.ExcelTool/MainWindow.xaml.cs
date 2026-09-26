// ============================================================
// MainWindow.xaml.cs —— 主界面逻辑
//
// 【界面只做三件事】
//   ① 选源（.xlsx 文件，或装着一批 .xlsx 的目录）→ 读取 → 逐表校验
//   ② 选输出（数据结构类目录 / 容器类目录 / TXT 数据目录）→ 导出
//   ③ 把每一步的结果写进日志（成功与失败都要看得见，不弹窗打断）
//
// （源与输出目录会记在本机设置里：下次启动自动加载源、自动填回目录 —— 见 ToolSettings.cs）
//
// 【为什么业务逻辑一律放 Core/，界面只做转发？】
//   导表规则是最该被复用和测试的部分。界面只负责"取值 → 调用 → 显示"，
//   换 UI（控制台 / CI 命令行）时 Core 一行都不用改。
// ============================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Revolution.ExcelTool.Core;

namespace Revolution.ExcelTool
{
    /// <summary>表列表里的一项（给 ListBox 绑定用）</summary>
    public sealed class TableItem
    {
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public bool HasError { get; set; }
        public ExcelTable Table { get; set; }
    }

    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<TableItem> _items = new ObservableCollection<TableItem>();
        private List<ExcelTable> _tables = new List<ExcelTable>();

        public MainWindow()
        {
            InitializeComponent();
            LstTables.ItemsSource = _items;

            // 源与输出目录：本工具**与 Unity 完全解耦** —— 不预填、也不去读 Unity 那边的任何配置，
            //   只恢复"上次自己用过的"（存在本机 APPDATA 里，见 ToolSettings）。
            ToolSettings saved = ToolSettings.Load();
            TxtSource.Text = saved.Source;
            TxtStructDir.Text = saved.StructDir;
            TxtContainerDir.Text = saved.ContainerDir;
            TxtDataDir.Text = saved.DataDir;

            bool restored = saved.Source.Length > 0
                         || saved.StructDir.Length > 0
                         || saved.ContainerDir.Length > 0
                         || saved.DataDir.Length > 0;

            Log(restored
                ? "Revolution 导表工具已就绪（已恢复上次用的 Excel 源与输出目录）。"
                : "Revolution 导表工具已就绪（源与输出目录请自行选择，选过就会被记住）。");

            Log("Excel 规则：第 1 行字段名 / 第 2 行类型(int,float,string,bool) / 第 3 行描述 / 第 4 行起数据；第一个字段为主键。");

            // 自动加载上次的源。★ 挂在 Loaded 上而不是直接在这里扫：
            //   读表有 IO，放在构造函数里会让窗口"半天画不出来"；挂 Loaded 就是先出窗口、再加载。
            string lastSource = saved.Source;
            Loaded += (s, e) => AutoLoadLastSource(lastSource);
        }

        // ==================== 源 ====================

        /// <summary>
        /// 启动时自动加载上次的源（由构造函数挂在 Loaded 上触发，那时窗口已经画出来了）。
        ///
        /// ★ 只在"确实扫得到 .xlsx"时才走正常读取流程；扫不到就只写一行日志 ——
        ///   启动时因为一个过期路径刷出红色状态，比"没加载"更让人困惑。
        /// </summary>
        private void AutoLoadLastSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;

            if (CollectExcelFiles(source).Count == 0)
            {
                Log("上次的 Excel 源里没找到任何 .xlsx（可能已被删 / 改名 / 移走）：" + source);
                Log("请点「选择文件」/「选择目录」重新选一个。");
                SetStatus("请选择 Excel 数据源", false);
                return;
            }

            Log("自动加载上次的 Excel 源：" + source);
            OnScan(this, null);
        }

        private void OnPickExcelFile(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 Excel 文件",
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*",
                Multiselect = true
            };

            string start = StartDirOf(TxtSource.Text);      // 上次用过的目录 → 对话框直接落那儿
            if (start != null) dialog.InitialDirectory = start;

            if (dialog.ShowDialog(this) == true)
            {
                TxtSource.Text = dialog.FileNames.Length == 1
                    ? dialog.FileNames[0]
                    : string.Join(";", dialog.FileNames);
                SaveSettings();                             // 选完立刻记下（含源路径）
                OnScan(sender, e);
            }
        }

        private void OnPickExcelFolder(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "选择装着一批 .xlsx 的目录" };

            string start = StartDirOf(TxtSource.Text);
            if (start != null) dialog.InitialDirectory = start;

            if (dialog.ShowDialog(this) == true)
            {
                TxtSource.Text = dialog.FolderName;
                SaveSettings();
                OnScan(sender, e);
            }
        }

        /// <summary>
        /// "打开对话框该从哪开始"：源是目录就用它，是文件就取它所在目录。
        /// 取不到（源已删 / 空）→ 返回 null，让系统用默认位置。
        /// </summary>
        private static string StartDirOf(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;

            string first = source.Split(';')[0].Trim();      // 多选是分号拼的，看第一项就够
            if (Directory.Exists(first)) return first;

            string dir = Path.GetDirectoryName(first);
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
        }

        private void OnScan(object sender, RoutedEventArgs e)
        {
            string input = (TxtSource.Text ?? string.Empty).Trim();
            if (input.Length == 0) { SetStatus("请先选择 Excel 文件或目录", true); return; }

            List<string> files = CollectExcelFiles(input);
            if (files.Count == 0) { SetStatus("没有找到 .xlsx 文件", true); return; }

            _items.Clear();
            _tables = new List<ExcelTable>();
            GridPreview.ItemsSource = null;
            TxtPreviewInfo.Text = "在左侧选择一张表，这里会显示字段与数据前 50 行";

            int bad = 0;
            foreach (string file in files)
            {
                try
                {
                    foreach (XlsxSheet sheet in XlsxReader.ReadWorkbook(file))
                    {
                        ExcelTable table = ExcelTableBuilder.Build(sheet, Path.GetFileName(file));
                        _tables.Add(table);

                        if (!table.IsValid) bad++;

                        _items.Add(new TableItem
                        {
                            Table = table,
                            Title = table.Name,
                            HasError = !table.IsValid,
                            Subtitle = table.IsValid
                                ? $"{table.Fields.Count} 字段 · {table.Rows.Count} 行"
                                : $"{table.Errors.Count} 个错误（点开左侧查看）"
                        });
                    }
                }
                catch (Exception ex)
                {
                    bad++;
                    Log($"[错误] 读取 {Path.GetFileName(file)} 失败：{ex.Message}");
                }
            }

            Log($"读取完成：{files.Count} 个文件，{_tables.Count} 张表" + (bad > 0 ? $"，其中 {bad} 张有问题" : ""));
            SetStatus(bad > 0
                ? $"读取完成，但有 {bad} 张表未通过校验，导出时会自动跳过（详情见日志）"
                : $"读取完成：{_tables.Count} 张表全部通过校验", bad > 0);

            if (_items.Count > 0) LstTables.SelectedIndex = 0;
        }

        /// <summary>输入可能是文件、也可能是目录（目录取顶层 .xlsx，跳过 Excel 的临时文件 "~$xxx.xlsx"）</summary>
        private static List<string> CollectExcelFiles(string input)
        {
            var files = new List<string>();

            if (File.Exists(input)) { files.Add(input); return files; }

            if (Directory.Exists(input))
            {
                files.AddRange(Directory.GetFiles(input, "*.xlsx")
                    .Where(f => !Path.GetFileName(f).StartsWith("~$"))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                return files;
            }

            // 支持 "文件1;文件2" 这种多选后拼起来的字符串
            foreach (string part in input.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (File.Exists(part)) files.Add(part);

            return files;
        }

        // ==================== 预览 ====================

        private void OnTableSelected(object sender, SelectionChangedEventArgs e)
        {
            var item = LstTables.SelectedItem as TableItem;
            if (item == null) return;

            ExcelTable table = item.Table;

            // 字段摘要
            string fields = string.Join("，", table.Fields.Select(f =>
                $"{(f.IsKey ? "★" : string.Empty)}{f.Name}:{f.TypeText}"));

            TxtPreviewInfo.Text = table.IsValid
                ? $"{table.StructName()} → 容器 {table.ContainerName()}　|　主键：{table.KeyField?.Name}　|　{table.Rows.Count} 行\n{fields}"
                : $"结构体 {table.StructName()} 校验未通过：\n" + string.Join("\n", table.Errors.Take(8));

            GridPreview.ItemsSource = BuildPreviewTable(table).DefaultView;
        }

        /// <summary>用 System.Data.DataTable 做预览（DataGrid 直接吃 DefaultView，自动生成列）</summary>
        private static System.Data.DataTable BuildPreviewTable(ExcelTable table)
        {
            var dt = new System.Data.DataTable(table.Name);

            foreach (ExcelField field in table.Fields)
                dt.Columns.Add(field.Name, typeof(string));

            foreach (ExcelRow row in table.Rows.Take(50))
            {
                var values = new object[table.Fields.Count];
                for (int i = 0; i < values.Length; i++) values[i] = row.Get(i);
                dt.Rows.Add(values);
            }

            return dt;
        }

        // ==================== 输出路径 ====================

        private void OnPickStructDir(object sender, RoutedEventArgs e) => PickFolder(TxtStructDir, "选择数据结构类的输出目录");
        private void OnPickContainerDir(object sender, RoutedEventArgs e) => PickFolder(TxtContainerDir, "选择容器类的输出目录");
        private void OnPickDataDir(object sender, RoutedEventArgs e) => PickFolder(TxtDataDir, "选择 TXT 数据文件的输出目录");

        private void PickFolder(TextBox target, string title)
        {
            var dialog = new OpenFolderDialog { Title = title };
            if (Directory.Exists(target.Text)) dialog.InitialDirectory = target.Text;

            if (dialog.ShowDialog(this) == true)
            {
                target.Text = dialog.FolderName;
                SaveSettings();          // 选完立刻记下：不依赖"正常关窗"，被强杀也不丢
            }
        }

        /// <summary>把当前的源与三个输出目录记到本机设置里（存不下也不影响使用，见 ToolSettings）</summary>
        private void SaveSettings()
        {
            new ToolSettings
            {
                Source = (TxtSource.Text ?? string.Empty).Trim(),
                StructDir = (TxtStructDir.Text ?? string.Empty).Trim(),
                ContainerDir = (TxtContainerDir.Text ?? string.Empty).Trim(),
                DataDir = (TxtDataDir.Text ?? string.Empty).Trim()
            }.Save();
        }

        // ==================== 导出 ====================

        private void OnExport(object sender, RoutedEventArgs e)
        {
            if (_tables.Count == 0) { SetStatus("请先读取 Excel 数据", true); Log("[错误] 还没有读取任何表"); return; }

            // 三个输出目录都不预填了（工具与 Unity 解耦），所以这里必须替使用者把关
            if (string.IsNullOrWhiteSpace(TxtStructDir.Text) ||
                string.IsNullOrWhiteSpace(TxtContainerDir.Text) ||
                string.IsNullOrWhiteSpace(TxtDataDir.Text))
            {
                SetStatus("请先选择三个输出目录", true);
                Log("[错误] 还有输出目录没选：数据结构类目录 / 容器类目录 / TXT 数据目录");
                return;
            }

            var options = new ExportOptions
            {
                StructDir = TxtStructDir.Text,
                ContainerDir = TxtContainerDir.Text,
                DataDir = TxtDataDir.Text
            };

            ExportResult result;
            try
            {
                result = ExportService.Export(_tables, options);
            }
            catch (Exception ex)
            {
                Log($"[错误] 导出异常：{ex.Message}");
                SetStatus("导出失败：" + ex.Message, true);
                return;
            }

            foreach (string line in result.Log) Log("  " + line);
            foreach (string w in result.Warnings) Log("[提示] " + w);
            foreach (string err in result.Errors) Log("[错误] " + err);

            if (result.Success)
            {
                Log($"导出成功：{result.TableCount} 张表，共 {result.RowCount} 条数据。");
                SetStatus($"导出成功：{result.TableCount} 张表 / {result.RowCount} 条数据（回 Unity 会自动刷新编译）", false);
            }
            else
            {
                Log("导出失败，未写入任何数据文件。");
                SetStatus($"导出失败：{result.Errors.Count} 个错误，详见日志", true);
            }
        }

        // ==================== 小工具 ====================

        private void Log(string message)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            TxtLog.ScrollToEnd();
        }

        private void SetStatus(string text, bool error)
        {
            TxtStatus.Text = text;
            TxtStatus.Foreground = error
                ? System.Windows.Media.Brushes.Firebrick
                : (System.Windows.Media.Brush)FindResource("TextSub");
        }

        /// <summary>
        /// 关窗时再记一次：手工敲进文本框的路径（不是通过「选择…」按钮选的）也能被记住。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            SaveSettings();
            base.OnClosing(e);
        }

    }
}
