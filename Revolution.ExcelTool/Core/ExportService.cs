// ============================================================
// ExportService.cs —— 导出编排（Excel → 代码 + TXT 数据）
//
// 【一次导出做四件事】
//   ① 只导出"校验通过"的表（有错的表宁可不出，也不要产出半对的数据）
//   ② 检查跨表重名（不同工作簿出现同名表 → 会生成重复类名，编译不过）
//   ③ 生成两个代码文件：数据结构类 / 容器类
//   ④ 逐表生成 TXT 数据文件
//
// 【编码为什么不一样？】
//   代码文件用 UTF-8【带 BOM】：IDE 靠 BOM 判定编码，中文注释才不会是乱码。
//   数据文件用 UTF-8【不带 BOM】：BOM 会在文件开头多出 \uFEFF，
//   运行时按行解析时第一行的 "#" 就不是第一个字符了，注释行会失效。
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Revolution.ExcelTool.Core
{
    /// <summary>导出选项（界面上的三个路径 + 两个文件名）</summary>
    public sealed class ExportOptions
    {
        public string StructDir;                        // 数据结构类目录
        public string ContainerDir;                     // 容器类目录
        public string DataDir;                          // TXT 数据目录
        public string StructFileName = "RevDataStructures.cs";
        public string ContainerFileName = "RevDataTables.cs";
    }

    /// <summary>
    /// 导出模式：
    /// · <see cref="Full"/> —— 代码（数据结构类 + 容器类）+ 数据文件全都要（表结构有改动时用）；
    /// · <see cref="DataOnly"/> —— 只写数据 txt，完全不动代码文件
    ///   （只改了 Excel 里的数值、没动表结构时用：不生成代码，Unity 那边也就不会触发脚本重编译）。
    /// </summary>
    public enum ExportMode
    {
        Full,
        DataOnly,
    }

    /// <summary>导出结果</summary>
    public sealed class ExportResult
    {
        public readonly List<string> Log = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public int TableCount;
        public int RowCount;

        public bool Success => Errors.Count == 0;
    }

    public static class ExportService
    {
        /// <summary>全量导出：代码（数据结构类 + 容器类）+ 数据文件。</summary>
        public static ExportResult Export(IReadOnlyList<ExcelTable> allTables, ExportOptions options)
            => Export(allTables, options, ExportMode.Full);

        /// <summary>
        /// 导出。
        /// </summary>
        /// <param name="mode">
        /// <see cref="ExportMode.DataOnly"/> = 只写数据 txt、完全不碰代码文件
        /// （只改了 Excel 数值、没动表结构时用：不生成代码，Unity 那边就不会触发脚本重编译）。
        /// 此时数据结构类 / 容器类目录不参与（不校验、不创建、不写入）。
        /// </param>
        public static ExportResult Export(IReadOnlyList<ExcelTable> allTables, ExportOptions options,
            ExportMode mode)
        {
            var result = new ExportResult();

            // ---------- ① 过滤 ----------
            // 【为什么是警告不是错误？】某张表填错了，不该拖着其它正确的表一起不出 ——
            // 导表是日常高频操作，一张坏表就全批失败会把开发效率拖死。坏表跳过并点名，其余照常导出。
            var tables = new List<ExcelTable>();
            foreach (ExcelTable t in allTables)
            {
                if (t.IsValid) tables.Add(t);
                else result.Warnings.Add($"表 [{t.Name}] 校验未通过，已跳过导出（{t.Errors.Count} 个错误）");
            }

            if (tables.Count == 0)
            {
                result.Errors.Add(result.Warnings.Count > 0
                    ? "所有表都未通过校验，没有任何可导出的表"
                    : "没有读取到任何表，请先选择 Excel 文件");
                return result;
            }

            // ---------- ② 跨表重名 ----------
            var seen = new HashSet<string>();
            foreach (ExcelTable t in tables)
            {
                if (!seen.Add(t.StructName()))
                    result.Errors.Add($"存在同名表 [{t.StructName()}]（来自 {t.SourceFile}）：会生成重复的结构体 / 容器类名");
            }

            if (!result.Success) return result;

            // ---------- ③ 目录准备（仅数据模式不碰代码目录） ----------
            string structDir = null, containerDir = null;
            if (mode == ExportMode.Full)
            {
                structDir = PrepareDir(options.StructDir, "数据结构类目录", result);
                containerDir = PrepareDir(options.ContainerDir, "容器类目录", result);
            }

            string dataDir = PrepareDir(options.DataDir, "数据文件目录", result);
            if (!result.Success) return result;

            // ---------- ④ 代码（仅数据模式整段跳过：一个代码文件都不碰） ----------
            if (mode == ExportMode.Full)
            {
                var utf8Bom = new UTF8Encoding(true);

                string structPath = Path.Combine(structDir, options.StructFileName);
                string containerPath = Path.Combine(containerDir, options.ContainerFileName);

                File.WriteAllText(structPath, CodeGenerator.GenerateDataStructures(tables), utf8Bom);
                File.WriteAllText(containerPath, CodeGenerator.GenerateContainers(tables), utf8Bom);

                result.Log.Add($"数据结构类 → {structPath}");
                result.Log.Add($"容器类     → {containerPath}");
            }
            else
            {
                result.Log.Add("（仅数据模式：未生成代码文件）");
            }

            // ---------- ⑤ 数据 ----------
            var utf8NoBom = new UTF8Encoding(false);

            foreach (ExcelTable t in tables)
            {
                string path = Path.Combine(dataDir, t.DataFileName());
                File.WriteAllText(path, DataTextWriter.Generate(t), utf8NoBom);

                result.RowCount += t.Rows.Count;

                if (t.Warnings.Count > 0)
                    foreach (string w in t.Warnings)
                        result.Warnings.Add(w);
            }

            result.TableCount = tables.Count;
            result.Log.Add($"数据文件   → {dataDir}（{result.TableCount} 个表，{result.RowCount} 条数据）");

            return result;
        }

        private static string PrepareDir(string dir, string displayName, ExportResult result)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                result.Errors.Add($"未指定{displayName}");
                return null;
            }

            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.GetFullPath(dir);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"创建{displayName}失败：{ex.Message}");
                return null;
            }
        }
    }
}
