// ============================================================
// RevCancellation.cs —— 轻量取消令牌
//
// 位置：Runtime\RevTask\
//
// 【和 System.Threading.CancellationTokenSource 的区别】
//   后者自带 Timer、线程同步、链接令牌等一堆东西；
//   资源加载全在主线程、只需"标记取消 + 通知回调"，自己写更小更直接。
// ============================================================
using System;

namespace Revolution
{
    /// <summary>取消源：谁想取消，就持有它</summary>
    public sealed class RevCancellationTokenSource
    {
        private readonly RevCancellationToken _token = new RevCancellationToken();

        public RevCancellationToken Token => _token;

        /// <summary>标记取消：所有已注册的回调立刻执行</summary>
        public void Cancel() => _token.Cancel();

        /// <summary>复位（供下一个场景复用同一个源）</summary>
        public void Reset() => _token.Reset();
    }

    /// <summary>取消令牌：加载流程在关键节点检查它</summary>
    public sealed class RevCancellationToken
    {
        private Action _callbacks;

        public bool IsCancelled { get; private set; }

        /// <summary>注册取消回调；若已取消则立即回调</summary>
        public void Register(Action callback)
        {
            if (callback == null) return;
            if (IsCancelled) { callback(); return; }
            _callbacks = (Action)Delegate.Combine(_callbacks, callback);
        }

        /// <summary>已取消就抛异常，用来"中断"加载流程</summary>
        public void ThrowIfCancelled()
        {
            if (IsCancelled) throw new RevOperationCanceledException();
        }

        internal void Cancel()
        {
            if (IsCancelled) return;
            IsCancelled = true;

            Action c = _callbacks;
            _callbacks = null;
            c?.Invoke();
        }

        internal void Reset()
        {
            IsCancelled = false;
            _callbacks = null;
        }
    }

    /// <summary>取消异常（独立类型，便于和业务异常区分）</summary>
    public class RevOperationCanceledException : Exception
    {
        public RevOperationCanceledException() : base("RevTask 已取消") { }
    }
}
