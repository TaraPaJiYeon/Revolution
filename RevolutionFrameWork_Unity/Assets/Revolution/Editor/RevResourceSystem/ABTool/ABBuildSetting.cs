// ============================================================
// ABBuildSetting.cs —— 工程级"技术路径"常量
//
// 位置：Editor\资源加载\ABTool\
//
// 【哪些放这里，哪些放配置？】
//   ★ 放这里：有"技术约束"、一般不该乱动的路径
//       - 产物输出目录 OutputRoot（相对工程根）
//       - ResMap.txt 路径（必须落在某个 Resources 文件夹下：运行时靠 Resources.Load 读它）
//       - RevResPath.cs 路径（生成的代码必须落在 Assets 下才会被编译）
//   ★ 放 ABBuildConfig：由"项目结构 / 使用者"决定的
//       - 资源根目录 resRoot（可在编辑器窗口里拖拽 / 选择，不用改代码）
//
// 【为什么单独抽一个文件？】
//   避免这些字符串散落在十几个文件里，改一次要全局搜索替换、极易漏改。
// ============================================================
using System.IO;

namespace Revolution.Editor
{
    public static class ABBuildSetting
    {
        // 【这里为什么没有"资源根目录"常量？】
        //   资源根目录由使用者决定（项目结构不同、随时可能改），所以它**只存在于配置资产里**
        //   （ABBuildConfig.resRoot），代码里一个默认路径都不留 ——
        //   否则"猜"一个 Assets/GameRes 出来，既会让人以为框架写死了它，
        //   又会把"没配置"悄悄变成"文件找不到"，把排查方向带偏。

        /// <summary>打包产物输出根目录（其下还会按平台再分子目录，如 AssetBundles/Android）</summary>
        public const string OutputRoot = "AssetBundles";

        /// <summary>
        /// 运行时映射表路径：放在**框架自己的 Resources 文件夹**里，
        /// 而不是工程根的 Assets/Resources —— 框架的东西归框架，不占用使用者的目录。
        ///
        /// ★ 它与运行时 ResBootstrap.LoadResMap 里的 Resources.Load 路径是一对：
        ///   Resources.Load 的路径"相对任意 Resources 文件夹"、且不带扩展名，
        ///   所以这里是 ".../Resources/ResourceSystem/ResMap.txt" → 那边写 "ResourceSystem/ResMap"。
        ///   改一个必须改另一个。
        /// </summary>
        public const string MapAssetPath = "Assets/Revolution/Resources/ResourceSystem/ResMap.txt";

        /// <summary>
        /// 自动生成的 RevResPath 常量类路径。
        /// 放在框架自带的 Generation 程序集里（Revolution.Generation.asmdef，
        /// 它已引用 Revolution.Runtime 且 autoReferenced，业务程序集能直接用到）。
        /// </summary>
        public const string ResPathCodePath = "Assets/Revolution/Generation/RevResPath.cs";

        /// <summary>
        /// 自动生成的音效路径常量类路径（音效 / BGM 的资源目录，在打包窗口①配置里选）。
        ///
        /// ★ 为什么单独生成一份、而不是复用 Generation 里的 RevResPath？
        ///   Revolution.Runtime.asmdef 的 references 是空的，而 Generation 反过来引用了 Runtime ——
        ///   音效系统的代码在 Runtime 程序集里，反向引用 Generation 会成环。
        ///   所以音效目录的常量必须生成在 **Runtime 程序集内部**（RevSoundSystem\Generated\）。
        ///   它与 RevResPath 的分工：RevResPath 是"整个资源根目录的目录表"（业务用），
        ///   这里是"音效系统要用的那两个目录"（框架自己用，业务也能顺手用）。
        /// </summary>
        public const string SoundPathCodePath = "Assets/Revolution/Runtime/RevSoundSystem/Generated/RevSoundPath.cs";

        /// <summary>打包配置资产路径（跟着工程走，团队成员共享同一份）</summary>
        public const string ConfigAssetPath = "Assets/Editor/ABBuildConfig.asset";

        /// <summary>取当前平台的产物目录，例如 "AssetBundles/PC"</summary>
        public static string GetOutputDir(UnityEditor.BuildTarget target)
            => Path.Combine(OutputRoot, GetPlatformName(target));

        /// <summary>
        /// 平台名：既是产物目录名，也是运行时主包名
        /// （Unity 用输出目录名给 AssetBundleManifest 主包命名）。
        /// ★ 必须与运行时 ABLoader.MainName 一致。
        /// </summary>
        public static string GetPlatformName(UnityEditor.BuildTarget target)
        {
            switch (target)
            {
                case UnityEditor.BuildTarget.StandaloneWindows:
                case UnityEditor.BuildTarget.StandaloneWindows64:
                case UnityEditor.BuildTarget.StandaloneOSX:
                case UnityEditor.BuildTarget.StandaloneLinux64:
                    return "PC";                        // 桌面统一 PC
                default:
                    return target.ToString();           // iOS / Android / WebGL ...
            }
        }
    }
}
