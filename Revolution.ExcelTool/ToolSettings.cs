// ============================================================
// ToolSettings.cs —— 本工具自己的"上次用过的目录"记忆（纯本地，与 Unity 无关）
//
// 【为什么需要它？】
//   本工具与 Unity 完全解耦（不预填、也不读 Unity 那边的配置），
//   所以三个输出目录每次启动都得手选一遍 —— 太烦。
//   这里把上次用过的记在自己这儿：既不猜、也不耦合，下次启动自动填回去。
//
// 【为什么存在 APPDATA，而不放在 exe 旁边？】
//   ① exe 可能被放在只读目录（如 Program Files），写旁边会失败；
//   ② "每个人一套目录"本来就是更合理的行为 —— 同机不同人不用互相覆盖。
//   取不到 APPDATA 时退化成"不记忆"，绝不因此崩掉。
//
// 【文件格式】自己解析的 key=value，一行一个（零依赖，和工具整体风格一致）：
//       source=D:\策划表\Hero.xlsx           ← 上次的 Excel 源（多选就是分号拼起来）
//       structDir=D:\Proj\Assets\Revolution\Generation\DataStructures
//       containerDir=...
//       dataDir=...
//   只按【第一个】'=' 切分，所以路径里含 '=' 也不会解析错。
//   # 开头的行是注释，随便删整个文件也只是"下次重新选一遍"。
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;

namespace Revolution.ExcelTool
{
    /// <summary>工具的本机设置（目前就是"上次用过的三个输出目录"）</summary>
    internal sealed class ToolSettings
    {
        /// <summary>上次的 Excel 源：一个 .xlsx 路径 / 一个目录 / 多选后按 ";" 拼起来的串</summary>
        public string Source { get; set; } = "";

        public string StructDir { get; set; } = "";
        public string ContainerDir { get; set; } = "";
        public string DataDir { get; set; } = "";

        private const string FileName = "settings.txt";

        /// <summary>设置文件完整路径；取不到 APPDATA → 返回 null（表示本次"不记忆"）</summary>
        private static string FilePath()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return string.IsNullOrEmpty(appData)
                    ? null
                    : Path.Combine(appData, "Revolution", "ExcelTool", FileName);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>读上次的设置。没有文件 / 读坏了 → 返回空设置（不影响使用）</summary>
        public static ToolSettings Load()
        {
            var settings = new ToolSettings();

            try
            {
                string path = FilePath();
                if (path == null || !File.Exists(path)) return settings;

                foreach (string raw in File.ReadAllLines(path))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;                       // 空行 / 注释 / 坏行 → 跳过

                    string key = raw.Substring(0, eq).Trim();
                    string value = raw.Substring(eq + 1).Trim();

                    if (key == "source") settings.Source = value;
                    else if (key == "structDir") settings.StructDir = value;
                    else if (key == "containerDir") settings.ContainerDir = value;
                    else if (key == "dataDir") settings.DataDir = value;
                }
            }
            catch
            {
                // 读坏了就当没记住 —— 不能因为"记目录"这点便利把工具搞崩
            }

            return settings;
        }

        /// <summary>写回设置。失败不抛：最坏结果也只是下次重新选一遍</summary>
        public void Save()
        {
            try
            {
                string path = FilePath();
                if (path == null) return;

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllLines(path, new List<string>
                {
                    "# Revolution 导表工具的本机设置（可以随便删，删了就是下次重新选一遍）",
                    "source=" + Source,
                    "structDir=" + StructDir,
                    "containerDir=" + ContainerDir,
                    "dataDir=" + DataDir,
                });
            }
            catch
            {
                // 存不下就算了 —— 只是少了个便利，不该打扰使用者
            }
        }
    }
}
