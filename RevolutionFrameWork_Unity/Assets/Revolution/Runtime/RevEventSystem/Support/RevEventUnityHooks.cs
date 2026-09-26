// ============================================================
// RevEventUnityHooks.cs —— 事件系统的 Unity 生命周期钩子
//
// 位置：Runtime\EventSystem\Support\
//
// 【为什么需要这个文件】
//   事件系统的核心（RevEvent / RevEventCenter / RevEventHandlerGroup / RevEventListener）
//   是**纯 C#**，不引用 UnityEngine —— 好处是它能在工程外跑单元测试（本书的事件断言就是这么验的）。
//   代价是"日志往哪打、进 Play 要清什么"这类 Unity 相关的事没人做，就由这个薄薄的文件补上。
//
// 【它做两件事】
//   ① 装上日志出口：没接日志的话，事件系统的告警/异常会输出到标准错误，
//      在 Unity 里不够显眼。这里接到 Debug.LogWarning / Debug.LogError + Debug.LogException。
//      ★ 只在自己没被设置过的时候接管 —— 业务自定义了出口就不覆盖。
//
//   ② 重置静态数据：静态字段在"关闭 Domain Reload 的快速进入 Play 模式"下**不会**被清空，
//      上一次运行留下的监听者会原封不动地活到这一次（表现为"我明明重新开始了，怎么还有监听"）。
//      SubsystemRegistration 阶段重置一次，彻底避免这种鬼故事。
//
// 【注意】本文件依赖 UnityEngine / UnityEditor，所以不参与工程外的单元测试。
// ============================================================
using UnityEngine;

namespace Revolution
{
    /// <summary>把事件系统接到 Unity 的生命周期上（日志出口 + 进 Play 重置）。</summary>
    internal static class RevEventUnityHooks
    {
#if UNITY_EDITOR
        // 编辑器里（没运行游戏时）也要能用事件系统，所以额外接一次
        [UnityEditor.InitializeOnLoadMethod]
        private static void InstallInEditor()
        {
            Install();
        }
#endif

        // SubsystemRegistration：进入 Play 后最先执行的阶段之一（早于场景加载、早于任何业务代码）
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InstallInPlayer()
        {
            Install();

            // 关闭 Domain Reload 时静态字段会残留 → 必须手动清一次
            RevEvent.ResetAll();
        }

        /// <summary>装上默认日志出口；业务已经设过就不动它。</summary>
        private static void Install()
        {
            if (RevEvent.Log == null)
            {
                RevEvent.Log = message => Debug.LogWarning(message);
            }

            if (RevEvent.OnException == null)
            {
                // LogError 给出"哪个事件、哪个监听者"，LogException 给出可点击跳转的原始堆栈
                RevEvent.OnException = (e, message) =>
                {
                    Debug.LogError(message);
                    Debug.LogException(e);
                };
            }
        }
    }
}
