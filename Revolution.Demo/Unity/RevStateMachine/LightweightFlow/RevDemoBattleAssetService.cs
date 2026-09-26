// ============================================================
// RevDemoBattleAssetService.cs —— 演示用的"战斗资源服务"（模拟层）
//
// 位置：Revolution.Demo\Unity\RevStateMachine\LightweightFlow\
//
// 【为什么需要它】
//   王者里"进战斗"要等资源：AB 包 / 场景 / 英雄模型。而**资源加载是异步的**，
//   这正是轻量级状态机唯一需要"异步切换"（ChangeToAsync）的地方。
//   真实项目里，这个类的位置就是资源系统：
//       await ResManager.LoadAsync<GameObject>("Battle/Scene", token);
//
// 【这里用最小实现模拟两件事】
//   ① 分帧加载（3 帧）：让"加载中 → 加载完"这件事在 Demo 里看得见；
//   ② 释放是幂等的：便于观察"兜底释放"到底有没有发生（ReleaseCount 会告诉你）。
// ============================================================
using UnityEngine;

namespace Revolution.Demo
{
    /// <summary>
    /// 演示用的战斗资源服务：模拟异步加载 / 释放（真实项目对应资源系统）。
    /// <para>纯演示用途，业务代码里不要照抄这个静态类。</para>
    /// </summary>
    public static class RevDemoBattleAssetService
    {
        /// <summary>分几帧加载完（数字大一点，方便在读条期间按"取消"键）</summary>
        private const int LoadSteps = 3;

        /// <summary>战斗资源是否已加载（对应真实项目里的"资源是否就绪"）</summary>
        public static bool IsLoaded { get; private set; }

        /// <summary>累计加载次数（看"进战斗"触发了几次加载）</summary>
        public static int LoadCount { get; private set; }

        /// <summary>累计释放次数（看"兜底释放"有没有生效）</summary>
        public static int ReleaseCount { get; private set; }

        /// <summary>
        /// 模拟异步加载：分 <see cref="LoadSteps"/> 帧加载完。
        /// <para>真实项目写法：<c>await ResManager.LoadAsync&lt;GameObject&gt;("Battle/Scene", token);</c></para>
        /// </summary>
        public static async RevTask LoadAsync()
        {
            LoadCount++;
            IsLoaded = false;

            for (int i = 1; i <= LoadSteps; i++)
            {
                await RevTask.Yield();                  // 等一帧（真实项目：加载进度回调 / 分帧切片）
                Debug.Log($"[资源] 战斗资源加载中… {i * 100 / LoadSteps}%");
            }

            IsLoaded = true;
            Debug.Log("[资源] 战斗资源加载完成");
        }

        /// <summary>释放战斗资源（幂等：没加载时调用只是记一次日志）</summary>
        public static void Release()
        {
            ReleaseCount++;

            if (!IsLoaded)
            {
                Debug.Log("[资源] 战斗资源未加载，无需释放");
                return;
            }

            IsLoaded = false;
            Debug.Log("[资源] ★ 战斗资源已释放");
        }

        /// <summary>重置演示统计（Demo 的"重置"按键会调它）</summary>
        public static void ResetStats()
        {
            LoadCount = 0;
            ReleaseCount = 0;
            IsLoaded = false;
        }
    }
}
