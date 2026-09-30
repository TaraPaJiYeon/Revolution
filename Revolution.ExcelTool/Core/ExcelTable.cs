// ============================================================
// ExcelTable.cs —— Excel 表模型与配置规则校验
//
// 【Excel 配置规则（本工具与策划的唯一契约）】
//   第 1 行：字段名
//   第 2 行：字段类型（int / float / string / bool）
//   第 3 行：字段描述（只生成注释，不参与数据）
//   第 4 行起：真正的数据
//   默认第一个字段是主键
//
// 【为什么校验要这么细？】
//   导表是"一次性把错误集中暴露"的好机会：这里报出来是一行日志，
//   漏到运行时就是"某张表少了一行、某个 Id 变成 0"这种极难查的问题。
// ============================================================
using System.Collections.Generic;
using System.Globalization;

namespace Revolution.ExcelTool.Core
{
    /// <summary>字段类型（框架只支持这四种，与 RevDataFieldParser 一一对应）</summary>
    public enum FieldType
    {
        Int,
        Float,
        String,
        Bool
    }

    /// <summary>一个字段（= Excel 的一列）</summary>
    public sealed class ExcelField
    {
        public int Column;          // 在 Excel 里的列号（0 基）
        public string Name;         // 第 1 行
        public FieldType Type;      // 第 2 行（解析后的）
        public string TypeText;     // 第 2 行（原始文本，报错时给人看）
        public string Desc;         // 第 3 行
        public bool IsKey;          // 是否主键

        /// <summary>生成 C# 字段名：首字母大写、"Id" 而不是 "ID"（跟 .NET 命名习惯保持一致）</summary>
        public string CSharpName()
        {
            string n = (Name ?? string.Empty).Trim();
            if (n.Length == 0) return "Field";

            // 全大写的短名（ID、HP）转成 Id、Hp，避免生成 ID 这种不符合约定的名字
            if (n.Length <= 3 && n == n.ToUpperInvariant())
                return char.ToUpperInvariant(n[0]) + n.Substring(1).ToLowerInvariant();

            return char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

        /// <summary>生成用的 C# 类型名</summary>
        public string CSharpType()
        {
            switch (Type)
            {
                case FieldType.Int: return "int";
                case FieldType.Float: return "float";
                case FieldType.Bool: return "bool";
                default: return "string";
            }
        }

        /// <summary>生成的取值表达式（对应 RevDataFieldParser 的强类型方法）</summary>
        public string ParseExpression(string cellExpr)
        {
            switch (Type)
            {
                case FieldType.Int: return "RevDataFieldParser.ToInt(" + cellExpr + ")";
                case FieldType.Float: return "RevDataFieldParser.ToFloat(" + cellExpr + ")";
                case FieldType.Bool: return "RevDataFieldParser.ToBool(" + cellExpr + ")";
                default: return "RevDataFieldParser.ToStr(" + cellExpr + ")";
            }
        }
    }

    /// <summary>一条数据行</summary>
    public sealed class ExcelRow
    {
        public int ExcelRowNumber;      // 原始行号（1 基，报错时直接指给策划看）
        public string[] Cells;          // 与 Fields 一一对应的值

        public string Get(int fieldIndex)
            => (Cells != null && fieldIndex >= 0 && fieldIndex < Cells.Length) ? (Cells[fieldIndex] ?? string.Empty) : string.Empty;
    }

    /// <summary>一张表（= 一个工作表）</summary>
    public sealed class ExcelTable
    {
        public string Name;
        public string SourceFile;
        public readonly List<ExcelField> Fields = new List<ExcelField>();
        public readonly List<ExcelRow> Rows = new List<ExcelRow>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool IsValid => Errors.Count == 0;

        public ExcelField KeyField
        {
            get
            {
                foreach (ExcelField f in Fields)
                    if (f.IsKey) return f;
                return null;
            }
        }

        /// <summary>数据结构名：表名首字母大写（HeroSkin 表 → HeroSkin 结构体）</summary>
        public string StructName()
        {
            string n = (Name ?? "Table").Trim();
            return char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

        /// <summary>容器类名：HeroSkin → HeroSkinTable</summary>
        public string ContainerName() => StructName() + "Table";

        /// <summary>TXT 数据文件名</summary>
        public string DataFileName() => StructName() + ".txt";
    }

    /// <summary>把工作表按配置规则解析成表模型，并完成全部校验</summary>
    public static class ExcelTableBuilder
    {
        private const int RowFieldName = 0;      // 第 1 行：字段名
        private const int RowFieldType = 1;      // 第 2 行：类型
        private const int RowFieldDesc = 2;      // 第 3 行：描述
        private const int RowDataStart = 3;      // 第 4 行起：数据

        private static readonly Dictionary<string, FieldType> TypeMap = new Dictionary<string, FieldType>
        {
            { "int",    FieldType.Int    },
            { "float",  FieldType.Float  },
            { "string", FieldType.String },
            { "bool",   FieldType.Bool   }
        };

        public static ExcelTable Build(XlsxSheet sheet, string sourceFile)
        {
            var table = new ExcelTable
            {
                Name = sheet.Name?.Trim() ?? "Table",
                SourceFile = sourceFile
            };

            if (sheet.Rows.Count <= RowFieldDesc)
            {
                table.Errors.Add($"表 [{table.Name}] 行数不足 3 行，至少要有字段名、类型、描述三行");
                return table;
            }

            // ---------- ① 字段（列）----------
            int columnCount = sheet.ColumnCount;
            var usedNames = new HashSet<string>();

            for (int col = 0; col < columnCount; col++)
            {
                string name = sheet.Get(RowFieldName, col).Trim();
                if (name.Length == 0) continue;                  // 空列名的列直接忽略（Excel 里常有）
                if (name.StartsWith("#")) continue;              // 以 # 开头视为策划备注列

                string typeText = sheet.Get(RowFieldType, col).Trim();
                string desc = sheet.Get(RowFieldDesc, col).Trim();

                var field = new ExcelField
                {
                    Column = col,
                    Name = name,
                    TypeText = typeText,
                    Desc = desc
                };

                if (!TypeMap.TryGetValue(typeText.ToLowerInvariant(), out FieldType type))
                    table.Errors.Add($"字段 [{name}] 的类型 \"{typeText}\" 不支持（只支持 int / float / string / bool）");
                else
                    field.Type = type;

                if (!usedNames.Add(name))
                    table.Errors.Add($"字段名 [{name}] 重复");

                // 字段名要能直接当 C# 字段名用（汉字是合法标识符，空格和标点不是）
                if (!IsValidIdentifier(name))
                    table.Errors.Add($"字段名 [{name}] 不能作为 C# 标识符（不能有空格/标点，不能以数字开头）");

                table.Fields.Add(field);
            }

            if (table.Fields.Count == 0)
            {
                table.Errors.Add($"表 [{table.Name}] 一个有效字段都没有（第 1 行是字段名，空列名的列会被忽略）");
                return table;
            }

            // ---------- ② 主键：默认第一个字段 ----------
            table.Fields[0].IsKey = true;

            // 主键类型体检：int/string 是常规选择，float/bool 基本都配错了 —— 提前提醒，别等运行时查不到
            if (table.Fields[0].Type == FieldType.Float)
                table.Warnings.Add($"表 [{table.Name}] 用 float 做主键不推荐：浮点比较有精度风险，" +
                                   "会出现「表里明明有、却查不到」的怪问题，建议改用 int 或 string");

            if (table.Fields[0].Type == FieldType.Bool)
                table.Warnings.Add($"表 [{table.Name}] 用 bool 做主键：bool 只有 true/false 两个取值，" +
                                   $"整张表最多两行数据，请确认是否配错");

            // ---------- ③ 数据行 ----------
            var keyValues = new HashSet<string>();
            ExcelField keyField = table.KeyField;

            for (int r = RowDataStart; r < sheet.Rows.Count; r++)
            {
                string firstCell = sheet.Get(r, table.Fields[0].Column).Trim();

                if (sheet.IsRowEmpty(r)) continue;                 // 整行空
                if (firstCell.StartsWith("#")) continue;           // 以 # 开头视为注释行（表尾备注）

                var cells = new string[table.Fields.Count];
                for (int f = 0; f < table.Fields.Count; f++)
                    cells[f] = sheet.Get(r, table.Fields[f].Column).Trim();

                int excelRowNumber = r + 1;                        // 1 基，报错时和 Excel 左上角行号一致

                // 主键必填
                if (keyField != null && cells[0].Length == 0)
                {
                    table.Errors.Add($"第 {excelRowNumber} 行：主键 [{keyField.Name}] 为空");
                    continue;
                }

                // 主键唯一
                if (keyField != null && !keyValues.Add(cells[0]))
                {
                    table.Errors.Add($"第 {excelRowNumber} 行：主键 [{keyField.Name}] = {cells[0]} 重复");
                    continue;
                }

                // 数值列必须能解析（这里是"策划填错"最常见的形态）
                for (int f = 0; f < table.Fields.Count; f++)
                {
                    ExcelField field = table.Fields[f];
                    string cell = cells[f];
                    if (cell.Length == 0) continue;                // 空值取默认值，允许

                    if (field.Type == FieldType.Int &&
                        !int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        table.Errors.Add($"第 {excelRowNumber} 行 [{field.Name}]：\"{cell}\" 不是合法的 int");

                    if (field.Type == FieldType.Float &&
                        !float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        table.Errors.Add($"第 {excelRowNumber} 行 [{field.Name}]：\"{cell}\" 不是合法的 float");
                }

                table.Rows.Add(new ExcelRow { ExcelRowNumber = excelRowNumber, Cells = cells });
            }

            if (table.Rows.Count == 0)
                table.Warnings.Add($"表 [{table.Name}] 没有任何数据行");

            return table;
        }

        /// <summary>
        /// 是否是合法的 C# 标识符。
        /// 【注意】不能只允许 ASCII —— C# 允许 Unicode 字母，汉字字段名同样合法，
        /// 用 char.IsLetter 判断才能既不误杀汉字、又能拦住"英雄 id"这种带空格的写法。
        /// </summary>
        private static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (char.IsDigit(name[0])) return false;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return true;
        }
    }
}
