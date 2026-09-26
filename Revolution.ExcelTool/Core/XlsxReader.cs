// ============================================================
// XlsxReader.cs —— 极简 xlsx 读取器（零第三方依赖）
//
// 【为什么自己写？】
//   项目要求"零依赖"。常见方案都要引第三方：
//     NPOI / EPPlus / ExcelDataReader → 引 NuGet 包
//     Microsoft.Office.Interop.Excel   → 要求机器装 Excel（服务端不可用）
//     OLEDB (ACE 驱动)                  → 要求装 Access 数据库引擎
//   而 xlsx 本质就是一个 zip 包，里面的数据是 XML —— 用 BCL 的
//   ZipArchive + XDocument 完全够用，还顺带支持 Linux/macOS。
//
// 【xlsx 的结构（只关心这三样）】
//   xl/sharedStrings.xml            字符串池（单元格里的文本都存这里，单元格只存下标）
//   xl/workbook.xml                 工作表清单（名字 + r:id）
//   xl/_rels/workbook.xml.rels      r:id → 工作表 XML 的真实路径
//   xl/worksheets/sheetN.xml        单元格数据
//
// 【不支持什么】
//   .xls（97-2003 老格式，是 OLE 复合文档，不是 zip）；样式、图表、批注；
//   公式取它的"缓存值"（Excel 保存时会把计算结果写进 <v>，够导表用）。
// ============================================================
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Revolution.ExcelTool.Core
{
    /// <summary>一张工作表：解析后的等宽文本矩阵（行 0 = Excel 第 1 行）</summary>
    public sealed class XlsxSheet
    {
        public string Name;

        /// <summary>每一行的单元格（已按列号补齐空串，长度 = 该行列数）</summary>
        public readonly List<string[]> Rows = new List<string[]>();

        /// <summary>整张表的最大列数</summary>
        public int ColumnCount;

        /// <summary>取单元格；越界返回空串（导表时"少配了列"是常态，不该抛异常）</summary>
        public string Get(int row, int col)
        {
            if (row < 0 || row >= Rows.Count) return string.Empty;
            string[] line = Rows[row];
            return (col < 0 || col >= line.Length) ? string.Empty : (line[col] ?? string.Empty);
        }

        /// <summary>某一行是否整行为空（用于跳过尾部空行）</summary>
        public bool IsRowEmpty(int row)
        {
            if (row < 0 || row >= Rows.Count) return true;
            foreach (string cell in Rows[row])
                if (!string.IsNullOrWhiteSpace(cell)) return false;
            return true;
        }
    }

    /// <summary>xlsx 读取器</summary>
    public static class XlsxReader
    {
        /// <summary>读出一个工作簿里的全部工作表</summary>
        public static List<XlsxSheet> ReadWorkbook(string filePath)
        {
            var sheets = new List<XlsxSheet>();

            using (ZipArchive zip = ZipFile.OpenRead(filePath))
            {
                List<string> sharedStrings = ReadSharedStrings(zip);

                foreach (var (name, target) in ReadSheetRefs(zip))
                {
                    ZipArchiveEntry entry = zip.GetEntry(target);
                    if (entry == null) continue;

                    using (Stream stream = entry.Open())
                        sheets.Add(ParseSheet(stream, name, sharedStrings));
                }
            }

            return sheets;
        }

        // ==================== 字符串池 ====================

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();

            ZipArchiveEntry entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return list;      // 全数字的表不会有这个文件

            XDocument doc;
            using (Stream stream = entry.Open()) doc = XDocument.Load(stream);

            XNamespace ns = doc.Root.Name.Namespace;
            foreach (XElement si in doc.Root.Elements(ns + "si"))
                list.Add(CollectText(si, ns));   // 富文本（<r><t>…）要拼起来，不能只取第一个 <t>

            return list;
        }

        // ==================== 工作表清单 ====================

        private static List<(string Name, string Target)> ReadSheetRefs(ZipArchive zip)
        {
            var list = new List<(string, string)>();

            ZipArchiveEntry wbEntry = zip.GetEntry("xl/workbook.xml");
            if (wbEntry == null) return list;

            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Dictionary<string, string> rels = ReadRels(zip);

            XDocument wb;
            using (Stream stream = wbEntry.Open()) wb = XDocument.Load(stream);

            XNamespace ns = wb.Root.Name.Namespace;
            XElement sheetsEl = wb.Root.Element(ns + "sheets");

            int fallbackIndex = 0;
            foreach (XElement sheet in sheetsEl == null
                                        ? Enumerable.Empty<XElement>()
                                        : sheetsEl.Elements(ns + "sheet"))
            {
                fallbackIndex++;

                string name = (string)sheet.Attribute("name") ?? ("Sheet" + fallbackIndex);
                string rid = (string)sheet.Attribute(relNs + "id");

                // rels 里没有就按 Excel 的默认命名猜一个，尽量别整张表丢掉
                string target = (rid != null && rels.TryGetValue(rid, out string t))
                    ? t
                    : "xl/worksheets/sheet" + fallbackIndex + ".xml";

                list.Add((name, target));
            }

            return list;
        }

        private static Dictionary<string, string> ReadRels(ZipArchive zip)
        {
            var map = new Dictionary<string, string>();

            ZipArchiveEntry entry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (entry == null) return map;

            XDocument doc;
            using (Stream stream = entry.Open()) doc = XDocument.Load(stream);

            foreach (XElement rel in doc.Root.Elements())
            {
                string id = (string)rel.Attribute("Id");
                string target = (string)rel.Attribute("Target");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target)) continue;

                map[id] = NormalizeTarget(target);
            }

            return map;
        }

        /// <summary>把 rels 里的 Target 规范成 zip 内的路径</summary>
        private static string NormalizeTarget(string target)
        {
            target = target.Replace('\\', '/');

            if (target.StartsWith("/")) return target.Substring(1);          // 绝对路径：/xl/worksheets/sheet1.xml
            if (target.StartsWith("xl/")) return target;                    // 已经是完整路径
            return "xl/" + target;                                          // 相对路径：worksheets/sheet1.xml
        }

        // ==================== 单元格 ====================

        private static XlsxSheet ParseSheet(Stream stream, string name, List<string> sharedStrings)
        {
            var sheet = new XlsxSheet { Name = name };

            XDocument doc = XDocument.Load(stream);
            XNamespace ns = doc.Root.Name.Namespace;

            XElement sheetData = doc.Root.Element(ns + "sheetData");
            if (sheetData == null) return sheet;

            foreach (XElement rowElement in sheetData.Elements(ns + "row"))
            {
                var cells = new List<(int Col, string Val)>();
                int maxCol = -1;

                foreach (XElement c in rowElement.Elements(ns + "c"))
                {
                    // 单元格的 r 属性（如 "C7"）才是指向哪一列的依据 ——
                    // 空单元格会被 Excel 直接省略，只靠顺序数格子会整体错列
                    int col = ColumnFromRef((string)c.Attribute("r"));
                    if (col < 0) col = maxCol + 1;      // 没有 r 属性：退化成"按顺序排"

                    cells.Add((col, ReadCellValue(c, ns, sharedStrings)));
                    if (col > maxCol) maxCol = col;
                }

                var line = new string[maxCol + 1];
                for (int i = 0; i < line.Length; i++) line[i] = string.Empty;
                foreach (var (col, val) in cells)
                    if (col >= 0 && col < line.Length) line[col] = val;

                sheet.Rows.Add(line);
                if (line.Length > sheet.ColumnCount) sheet.ColumnCount = line.Length;
            }

            return sheet;
        }

        private static string ReadCellValue(XElement c, XNamespace ns, List<string> sharedStrings)
        {
            string type = (string)c.Attribute("t");

            switch (type)
            {
                case "s":       // 共享字符串：值是字符串池的下标
                    if (int.TryParse(c.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)
                        && idx >= 0 && idx < sharedStrings.Count)
                        return sharedStrings[idx];
                    return string.Empty;

                case "inlineStr":   // 内联字符串：直接写在单元格里
                    XElement isEl = c.Element(ns + "is");
                    return isEl == null ? string.Empty : CollectText(isEl, ns);

                case "b":           // 布尔：1 / 0
                    return c.Value.Trim() == "1" ? "TRUE" : "FALSE";

                case "e":           // 错误值（#N/A、#REF!）→ 当空处理，让导表校验去报
                    return string.Empty;

                default:            // 数字 / 公式缓存值（t="str" 也走这里）
                    XElement v = c.Element(ns + "v");
                    return v == null ? string.Empty : v.Value;
            }
        }

        /// <summary>把元素下所有 &lt;t&gt; 的文本拼起来（处理富文本多段的情况）</summary>
        private static string CollectText(XElement parent, XNamespace ns)
        {
            var sb = new StringBuilder();
            foreach (XElement t in parent.Descendants(ns + "t"))
                sb.Append(t.Value);
            return sb.ToString();
        }

        /// <summary>把 "C7" 这种单元格引用换算成 0 基列号（C → 2）</summary>
        private static int ColumnFromRef(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return -1;

            int col = 0;
            for (int i = 0; i < cellRef.Length; i++)
            {
                char ch = cellRef[i];
                if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
                else break;                          // 碰到数字就结束（A→1、Z→26、AA→27）
            }
            return col - 1;
        }
    }
}
