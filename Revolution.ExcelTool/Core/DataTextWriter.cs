// ============================================================
// DataTextWriter.cs —— 生成 TXT 数据文件
//
// 【文件长什么样】
//   #HeroSkin                      ← 表名（注释行）
//   #ID	HeroName	Quality        ← 字段名（注释行，方便肉眼核对）
//   #英雄ID	英雄名	品质          ← 字段描述（注释行）
//   1001	亚瑟	3                  ← 数据（一行一条）
//
// 【为什么用注释行写表头？】
//   运行时按"结构与顺序"解析，本不需要表头。但数据文件经常要被
//   策划/程序员直接打开看、或者用 git diff 看改了什么 ——
//   有表头一眼就知道第几列是什么，排查成本差一个数量级。
//   注释行以 "#" 开头，运行时整行跳过（见 DataTextFormat.IsSkippable）。
//
// 【拼接与转义交给 DataTextFormat】
//   它是与 Unity 运行时【共用同一份源码】的格式实现（见 csproj 里的 Link），
//   所以"写出来的东西运行时一定读得懂"，不存在两边各写一套规则跑偏的可能。
// ============================================================
using System.Collections.Generic;
using System.Text;
using Revolution;   // DataTextFormat（与运行时共用的那份源码）

namespace Revolution.ExcelTool.Core
{
    public static class DataTextWriter
    {
        public static string Generate(ExcelTable table)
        {
            var sb = new StringBuilder();

            // ① 表名
            sb.Append(DataTextFormat.Comment(table.Name)).Append(DataTextFormat.LineSeparator);

            // ② 字段名（原始名，方便和 Excel 对照）
            var names = new List<string>(table.Fields.Count);
            var descs = new List<string>(table.Fields.Count);

            foreach (ExcelField field in table.Fields)
            {
                names.Add(field.Name);
                descs.Add(field.Desc);
            }

            sb.Append(DataTextFormat.Comment(string.Join(DataTextFormat.FieldSeparator.ToString(), names)))
              .Append(DataTextFormat.LineSeparator);
            sb.Append(DataTextFormat.Comment(string.Join(DataTextFormat.FieldSeparator.ToString(), descs)))
              .Append(DataTextFormat.LineSeparator);

            // ③ 数据行（顺序 = Fields 顺序，与生成的 ParseRow 一一对应）
            foreach (ExcelRow row in table.Rows)
            {
                sb.Append(DataTextFormat.JoinFields(row.Cells)).Append(DataTextFormat.LineSeparator);
            }

            return sb.ToString();
        }
    }
}
