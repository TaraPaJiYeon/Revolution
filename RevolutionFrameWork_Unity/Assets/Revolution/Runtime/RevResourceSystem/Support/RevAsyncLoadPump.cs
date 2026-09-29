// ============================================================
// RevAsyncLoadPump.cs —— 异步加载泵
//
// 位置：Runtime\资源加载\
//
// 【为什么不能"每个请求各加载各的"？】
//   一个界面可能要加载上百个图标，如果同时发起上百个 IO 请求：
//   磁盘/内存峰值飙升，反而拖慢所有加载，还可能卡帧。
//   加载泵的做法是"排队 + 限流"：同时最多只跑 MaxConcurrent 个。
//
// 【双队列】
//   _waiting —— 排队中（还没开始加载）
//   _loading —— 正在加载（数量 ≤ MaxConcurrent）
//
// 【回调合并】同一资源被多处同时请求时只加载一次，回调挂在同一个 RevLoadJob 上。
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution
{
    public static class RevAsyncLoadPump
    {
        private class RevLoadJob
        {
            public RevResHandle handle;
            public IRevResLoader loader;
            public int priority;
            public readonly List<Action<RevResHandle>> callbacks = new List<Action<RevResHandle>>();
        }

        private static readonly List<RevLoadJob> _waiting = new List<RevLoadJob>();
        private static readonly List<RevLoadJob> _loading = new List<RevLoadJob>();
        private static readonly Dictionary<ulong, RevLoadJob> _jobByKey = new Dictionary<ulong, RevLoadJob>();

        /// <summary>最大并发加载数（可按设备性能调整）</summary>
        public static int MaxConcurrent = 4;

        private static bool _pumping;

        /// <summary>取消源：CancelAll() 时把"已取消"信号传给每个 loader，用于中断在途加载</summary>
        public static readonly RevCancellationTokenSource Cancellation = new RevCancellationTokenSource();

        public static int WaitingCount => _waiting.Count;
        public static int LoadingCount => _loading.Count;

        // ==================== 提交 ====================

        public static void Submit(RevResHandle handle, IRevResLoader loader, Action<RevResHandle> onFinished, int priority = 0)
        {
            if (handle == null) { onFinished?.Invoke(RevResHandle.Empty); return; }

            // 同资源已有任务：合并回调
            if (_jobByKey.TryGetValue(handle.Key, out RevLoadJob exist))
            {
                if (onFinished != null) exist.callbacks.Add(onFinished);
                return;
            }

            var job = new RevLoadJob { handle = handle, loader = loader, priority = priority };
            if (onFinished != null) job.callbacks.Add(onFinished);

            _waiting.Add(job);
            _jobByKey[handle.Key] = job;
            _waiting.Sort((a, b) => b.priority.CompareTo(a.priority));   // 优先级大者先加载

            EnsurePumping();
        }

        /// <summary>把回调挂到"正在加载中"的资源上</summary>
        public static void AddCallback(ulong key, Action<RevResHandle> cb)
        {
            if (cb == null) return;
            if (_jobByKey.TryGetValue(key, out RevLoadJob job)) job.callbacks.Add(cb);
        }

        // ==================== 驱动 ====================

        private static void EnsurePumping()
        {
            if (_pumping) return;
            _pumping = true;
            PumpLoop().Forget();
        }

        private static async RevTask PumpLoop()
        {
            while (_waiting.Count > 0 || _loading.Count > 0)
            {
                // ① 把等待队列按并发上限送进加载队列
                while (_waiting.Count > 0 && _loading.Count < MaxConcurrent)
                {
                    RevLoadJob job = _waiting[0];
                    _waiting.RemoveAt(0);
                    _loading.Add(job);
                    RunJob(job).Forget();
                }

                // ② 零分配等一帧（不用协程，也不用 RevTask.Yield()）
                await RevTaskScheduler.NextFrame();
            }
            _pumping = false;
        }

        private static async RevTask RunJob(RevLoadJob job)
        {
            bool done = false;

            // 把取消令牌一并传给加载器：切场景时它能尽早中断
            job.loader.LoadAsync(job.handle, h => done = true, Cancellation.Token);

            // 兜底等待：即便某个 loader 违反约定没回调，也只影响这一个 job，不会污染全局
            while (!done) await RevTaskScheduler.NextFrame();

            // ★ 顺序很重要：先把自己从两个队列/表里摘掉，再通知回调。
            //   否则"加载失败 → 回调里换下一条策略再 Submit(同一个 handle.Key)"
            //   会被误判成"同 key 正在加载"而被合并掉，兜底就永远不会执行。
            _loading.Remove(job);
            _jobByKey.Remove(job.handle.Key);

            RevResManager.OnAsyncLoaded(job.handle.Key, job.handle);
            foreach (Action<RevResHandle> cb in job.callbacks) cb?.Invoke(job.handle);
        }

        // ==================== 清理 ====================

        /// <summary>
        /// 强制中断：切场景 / 退出时调用。
        /// ① 通知所有在途 loader「已取消」（它们在关键节点会提前结束）
        /// ② 清空排队任务
        /// </summary>
        public static void CancelAll()
        {
            Cancellation.Cancel();
            _waiting.Clear();
            _jobByKey.Clear();
        }

        /// <summary>清空队列（不改变取消状态）</summary>
        public static void Clear()
        {
            _waiting.Clear();
            _loading.Clear();
            _jobByKey.Clear();
        }
    }
}
