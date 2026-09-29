// ============================================================
// RevUIDefine.cs —— UI 系统的公共词汇（纯 C#，不引用 UnityEngine）
//
// 位置：Runtime\RevUISystem\Core\
//
// 【为什么单独一个文件】
//   层级 / 状态 / 缓存模式 / 遮罩模式 是"整个 UI 系统的公共词汇"：
//   面板基类、绑定器、管理器、业务侧都要用它们。散在各处的结果是
//   "同一个概念两套名字"，改一次要全局搜索替换。
//
// 【本文件不引用 UnityEngine】所以能被工程外的断言直接链接编译（和 RevResPathUtil 同理）——
//   "按层级推断要不要遮罩"这类规则就能在工程外验，不必开 Unity。
// ============================================================
namespace Revolution
{
    /// <summary>
    /// UI 层级：决定"面板挂在哪个根节点下、谁盖住谁"。
    ///
    /// ★ 与原框架（唐老师框架 Bottom / Middle / Top / System）的对应关系写在每项注释里，迁移时一一对上即可。
    /// ★ 枚举值必须是"从下到上"的连续序号：管理器按它建根节点、推断遮罩、决定排序。
    /// </summary>
    public enum RevUILayer
    {
        /// <summary>场景级（原 Bottom）：主界面、大厅、全屏场景面板</summary>
        Scene = 0,

        /// <summary>普通（原 Middle）：二级界面、背包、商店</summary>
        Normal = 1,

        /// <summary>弹窗（原 Top）：确认框、奖励结算 —— 默认需要遮罩挡住下面的操作</summary>
        Popup = 2,

        /// <summary>提示（不参与返回栈）：飘字、跑马灯 —— 不挡操作</summary>
        Toast = 3,

        /// <summary>引导：新手引导遮罩、手指提示 —— 需要挡住下面的操作</summary>
        Guide = 4,

        /// <summary>系统级（原 System）：断线重连、Loading、公告</summary>
        Top = 5,
    }

    /// <summary>
    /// 面板状态机。
    /// ★ 只有 <see cref="Opened"/> 是"可交互"状态；其余状态下的重复请求都由管理器负责合并或忽略，
    ///   业务侧永远不需要自己去判断"现在能不能点"。
    /// </summary>
    public enum RevUIPanelState
    {
        /// <summary>尚未初始化（刚 new 出来 / 还没装配）</summary>
        None = 0,

        /// <summary>正在加载预制体（异步在途）</summary>
        Loading = 1,

        /// <summary>预制体已到、正在播放打开转场</summary>
        Opening = 2,

        /// <summary>已打开，可交互</summary>
        Opened = 3,

        /// <summary>正在播放关闭转场</summary>
        Closing = 4,

        /// <summary>已关闭（可能进了实例池，也可能已被销毁）</summary>
        Closed = 5,
    }

    /// <summary>关闭后的处置方式</summary>
    public enum RevUICacheMode
    {
        /// <summary>未声明 —— 用 <c>RevUISetting.DefaultCacheMode</c>（默认 KeepAlive）</summary>
        Unspecified = 0,

        /// <summary>关闭即销毁：适合很少打开、或占内存很大的界面（如战斗内的全屏界面）</summary>
        DestroyOnClose = 1,

        /// <summary>
        /// 关闭后进实例池复用（推荐）：下次打开直接取出，不重新加载/实例化/绑定。
        /// ★ 代价是复用时必须清掉上一次的残留状态 —— 框架会把"该清了"这件事变成必写钩子（<c>OnReuse</c>）。
        /// </summary>
        KeepAlive = 2,
    }

    /// <summary>遮罩（挡住下层点击）</summary>
    public enum RevUIMaskMode
    {
        /// <summary>未声明 —— 按层级推断：Popup / Guide / Top 层默认挡，其余不挡</summary>
        Auto = 0,

        /// <summary>不挡：下面的界面照常可点（如 Toast）</summary>
        None = 1,

        /// <summary>挡点击：在该面板下方自动铺一层全屏透明挡板</summary>
        ClickBlock = 2,
    }

    /// <summary>
    /// UI 根 Canvas 的渲染模式（对应 <c>UnityEngine.RenderMode</c>）。
    ///
    /// 【为什么单独定义一个枚举，而不是直接用 UnityEngine.RenderMode】
    ///   ① 本文件保持"纯 C#"（能工程外断言）；
    ///   ② 多了一个 <see cref="Auto"/>：让"跟随预制体 / 没预制体就用 Overlay"成为**默认**，
    ///      这样升级到"可配置渲染模式"不会改变任何既有工程的行为。
    ///
    /// 【怎么选】
    ///   · Auto（默认）—— 配了 Canvas 预制体就跟随预制体里选的模式；没配就 Overlay（永远最上层、不需要相机）；
    ///   · ScreenSpaceOverlay —— 不需要相机，UI 永远盖在场景之上（大多数游戏界面用这个就够）；
    ///   · ScreenSpaceCamera —— **需要一台 UI 相机**：UI 可被 3D 物体遮挡、可进 RenderTexture、
    ///     可让 3D 模型 / 粒子渲染在 UI 之上（详见表内的四个必配项，文档 4.9 / 4.11）；
    ///   · WorldSpace —— UI 当作 3D 平面（VR、世界内 UI）。
    /// </summary>
    public enum RevUICanvasMode
    {
        /// <summary>跟随 Canvas 预制体；没有预制体时 = Overlay（默认，行为与旧版一致）</summary>
        Auto = 0,

        /// <summary>屏幕叠加：不需要相机，UI 永远在最上层</summary>
        ScreenSpaceOverlay = 1,

        /// <summary>屏幕空间-相机：需要一台 UI 相机（可被 3D 遮挡 / 可进 RenderTexture）</summary>
        ScreenSpaceCamera = 2,

        /// <summary>世界空间：UI 是 3D 场景里的平面</summary>
        WorldSpace = 3,
    }

    /// <summary>
    /// 层级相关的纯规则（不依赖 UnityEngine，可单独断言）。
    /// </summary>
    public static class RevUILayerUtil
    {
        /// <summary>所有层级（按从下到上排序）—— 管理器按它建根节点，别自己再写一遍</summary>
        public static readonly RevUILayer[] All =
        {
            RevUILayer.Scene, RevUILayer.Normal, RevUILayer.Popup,
            RevUILayer.Toast, RevUILayer.Guide, RevUILayer.Top,
        };

        /// <summary>解析"到底要不要挡点击"：声明了就用声明的，声明 Auto 就按层级推断</summary>
        public static RevUIMaskMode ResolveMask(RevUIMaskMode declared, RevUILayer layer)
        {
            if (declared != RevUIMaskMode.Auto) return declared;

            // 这三层天然是"打断式"的：弹窗、引导、系统提示
            return layer == RevUILayer.Popup || layer == RevUILayer.Guide || layer == RevUILayer.Top
                ? RevUIMaskMode.ClickBlock
                : RevUIMaskMode.None;
        }

        /// <summary>a 是否在 b 之上（同层不算"之上"）</summary>
        public static bool IsAbove(RevUILayer a, RevUILayer b) => (int)a > (int)b;
    }
}
