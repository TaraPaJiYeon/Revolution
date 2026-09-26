// ============================================================
// RevDemoUiStates.cs —— 轻量级【栈状态机】Demo：UI 界面栈的 4 个状态
//
// 位置：Revolution.Demo\Unity\RevStateMachine\LightweightUiStack\
//
// 【王者业务映射】
//   王者③ StackStateMachine<T> 的官方示例场景就是 **UI 界面栈**：
//     主界面 → 背包 → 设置弹窗 → 帮助弹窗（逐级返回、逐级恢复）
//   栈语义的灵魂是 OnSuspend / OnResume：
//     · 被压住的状态【不销毁、只暂停】—— 返回时不需要重建（不用重新拉数据、不丢滚动位置）
//     · 只有栈顶被 Update 驱动 —— 被压住的界面不该继续跑逻辑（省性能、也避免"看不见的界面还在响应"）
//
// 【本文件的 4 个状态特意覆盖两种注册方式】
//   RevDemoUiLobbyState / RevDemoUiBagState / RevDemoUiSettingsState → 注册【实例】
//     （同一时刻只可能有一个，复用实例最省）
//   RevDemoUiTipsState                                            → 注册【工厂】
//     （帮助/提示弹窗可以叠多层：需要每次新实例，否则同一实例连压两次会被框架拒绝）
//
// 【怎么验证"只有栈顶被驱动"】
//   每个状态都自己数了 UpdatedFrames（在 OnUpdate 里 +1），
//   Demo 面板会把每个状态的帧数摊出来 —— 你会看到：被压住的那个数字停住不动了。
// ============================================================
using UnityEngine;

namespace Revolution.Demo
{
    /// <summary>主界面：栈底，被压在下面时只暂停、不销毁。</summary>
    public sealed class RevDemoUiLobbyState : RevLightStateBase
    {
        /// <summary>被 Update 驱动的帧数（用来证明"只有栈顶在被驱动"）</summary>
        public int UpdatedFrames { get; private set; }

        public override void OnEnter()
        {
            UpdatedFrames = 0;
            Debug.Log("[UI栈] 打开【主界面】：初始化大厅数据、启动背景音乐");
        }

        public override void OnSuspend()
        {
            // ★ 栈语义的核心：不销毁、只暂停。
            //   王者里的真实用途："大厅输入"被"战斗输入"压住时禁用点击，但保留状态，
            //   打完一局回来直接 OnResume 就能继续用 —— 不需要重建输入系统。
            Debug.Log("[UI栈] 主界面 OnSuspend：★ 禁用大厅点击，但不销毁（保留数据/滚动位置）");
        }

        public override void OnResume()
        {
            Debug.Log("[UI栈] 主界面 OnResume：恢复大厅点击（数据一直都在，无需重新拉取）");
        }

        public override void OnExit()
        {
            Debug.Log("[UI栈] 关闭【主界面】：销毁界面、释放资源");
        }

        public override void OnUpdate(float deltaTime) => UpdatedFrames++;
    }

    /// <summary>背包：典型"被压住就暂停刷新"的界面。</summary>
    public sealed class RevDemoUiBagState : RevLightStateBase
    {
        /// <summary>被 Update 驱动的帧数（证明"只有栈顶被驱动"）</summary>
        public int UpdatedFrames { get; private set; }

        public override void OnEnter()
        {
            UpdatedFrames = 0;
            Debug.Log("[UI栈] 打开【背包】：请求背包数据（首次进入才拉）");
        }

        public override void OnSuspend()
        {
            Debug.Log("[UI栈] 背包 OnSuspend：暂停列表刷新（数据保留，返回时不用重拉）");
        }

        public override void OnResume()
        {
            Debug.Log("[UI栈] 背包 OnResume：恢复列表刷新（只刷新变化的格子）");
        }

        public override void OnExit()
        {
            Debug.Log("[UI栈] 关闭【背包】：释放背包数据");
        }

        public override void OnUpdate(float deltaTime) => UpdatedFrames++;
    }

    /// <summary>设置弹窗：同一时刻只会有一个 → 用实例注册。</summary>
    public sealed class RevDemoUiSettingsState : RevLightStateBase
    {
        /// <summary>被 Update 驱动的帧数（证明"只有栈顶被驱动"）</summary>
        public int UpdatedFrames { get; private set; }

        public override void OnEnter()
        {
            UpdatedFrames = 0;
            Debug.Log("[UI栈] 弹出【设置】：读取玩家设置（画质/音量）");
        }

        public override void OnSuspend()
        {
            Debug.Log("[UI栈] 设置 OnSuspend：暂停设置项的实时预览");
        }

        public override void OnResume()
        {
            Debug.Log("[UI栈] 设置 OnResume：恢复设置项预览");
        }

        public override void OnExit()
        {
            Debug.Log("[UI栈] 关闭【设置】：保存设置");
        }

        public override void OnUpdate(float deltaTime) => UpdatedFrames++;
    }

    /// <summary>
    /// 帮助/提示弹窗：**可以叠多层**（设置里点"帮助"，帮助里又点"帮助"）。
    /// <para>所以它必须用<b>工厂注册</b>：每次按类型压栈都拿新实例。
    /// 若用实例注册，第二次压同一个对象会被框架直接拒绝
    /// （栈里出现同一个对象两份，Pop 时会对它先 OnExit 再 OnResume）。</para>
    /// </summary>
    public sealed class RevDemoUiTipsState : RevLightStateBase
    {
        private readonly int _layer;        // 第几层帮助（由工厂在创建时传入）

        /// <summary>被 Update 驱动的帧数（证明"只有栈顶被驱动"）</summary>
        public int UpdatedFrames { get; private set; }

        public RevDemoUiTipsState(int layer) => _layer = layer;

        public override void OnEnter()
        {
            UpdatedFrames = 0;
            Debug.Log($"[UI栈] 打开【帮助·第 {_layer} 层】：加载帮助文本");
        }

        public override void OnSuspend() => Debug.Log($"[UI栈] 帮助·第 {_layer} 层 OnSuspend：留在栈里等回来");

        public override void OnResume() => Debug.Log($"[UI栈] 帮助·第 {_layer} 层 OnResume：恢复显示");

        public override void OnExit() => Debug.Log($"[UI栈] 关闭【帮助·第 {_layer} 层】");

        public override void OnUpdate(float deltaTime) => UpdatedFrames++;
    }
}
