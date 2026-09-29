// ============================================================
// RevSceneLoader.cs —— 真正干活的那个（Unity 侧）
//
// 位置：Runtime\RevScene\Support\
//
// 【一次切换的四步，顺序固定】
//   ① 拒绝并发：正在切场景时，新请求直接忽略并告警（两个加载互相踩 = 黑屏 / 回不去）
//   ② 切之前清理：默认清空对象池（旧场景的池化实例不该跟着新场景走）
//   ③ 加载并盯进度：allowSceneActivation = false —— 先让进度条走完，再真正激活
//   ④ 收尾：进度置 1、状态置 Done、广播已进入的场景名
//
// 【为什么关掉自动激活（allowSceneActivation = false）】
//   Unity 在激活前只把 progress 报到 0.9；直接等 isDone，进度条会卡在 90% 再瞬间跳满。
//   这里改成：进度由 RevSceneProgressTracker 换算成 0~1，
//   到 100%（且满足最短展示时长）才放行激活 —— 进度条连续且不会跳。
//
// 【怎么等一帧】await RevTaskScheduler.NextFrame()：零分配，**不用新起 MonoBehaviour 宿主**。
//   这也是本模块"轻量"的关键：没有隐藏的 GameObject，没有 Update 轮询。
//
// 【场景名写错怎么办】LoadSceneAsync 会返回 null（名字不在 Build Settings 里），
//   这里当场判掉并给出人话原因，绝不让你对着黑屏猜。
// ============================================================
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Revolution
{
    /// <summary>场景加载的实际执行者（门面 <see cref="RevScene"/> 调它，业务不直接碰）。</summary>
    internal static class RevSceneLoader
    {
        /// <summary>是否正在切换（门面用它 / 业务查 RevScene.IsLoading）。</summary>
        internal static bool IsLoading { get; private set; }

        /// <summary>当前状态。</summary>
        internal static RevSceneLoadState State { get; private set; } = RevSceneLoadState.Idle;

        /// <summary>当前显示进度（0~1；不在加载中时保持上一次的值）。</summary>
        internal static float Progress { get; private set; }

        /// <summary>当前（活动）场景名。</summary>
        internal static string CurrentName => SceneManager.GetActiveScene().name;

        /// <summary>当前（活动）场景的 buildIndex。</summary>
        internal static int CurrentIndex => SceneManager.GetActiveScene().buildIndex;

        private static RevSceneProgressTracker _tracker;

        // ==================== 异步（推荐）====================

        /// <summary>按场景名异步切换。</summary>
        internal static RevTask LoadAsync(string sceneName, Action<float> onProgress, float minSeconds)
            => RunAsync(sceneName, -1, onProgress, minSeconds);

        /// <summary>按 buildIndex 异步切换。</summary>
        internal static RevTask LoadAsync(int buildIndex, Action<float> onProgress, float minSeconds)
            => RunAsync(null, buildIndex, onProgress, minSeconds);

        private static async RevTask RunAsync(string sceneName, int buildIndex, Action<float> onProgress, float minSeconds)
        {
            bool useIndex = sceneName == null;
            string label = useIndex ? ("buildIndex " + buildIndex) : sceneName;

            // ① 拒绝并发
            if (IsLoading)
            {
                RevSceneLog.Warning("[RevScene] 正在切换中，忽略本次请求：" + label + "（等这次完成后重调即可）");
                return;
            }

            // ② 切之前清理 + 广播"要开始了"
            Prepare(label, minSeconds);

            // ③ 发起异步加载（名字不在 Build Settings 时这里就是 null）
            AsyncOperation op = useIndex
                ? SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single)
                : SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            if (op == null)
            {
                Fail(label, "场景不存在，或没有加进 Build Settings（菜单 File / Build Settings 的 Scenes In Build）");
                return;
            }

            op.allowSceneActivation = false;      // 先让进度条走完，再激活
            RevSceneLog.Info("[RevScene] 开始异步切换 → " + label);

            // ④ 每帧推进：换算进度 → 广播 → 到 100% 才放行激活
            while (!op.isDone)
            {
                float value = _tracker.Tick(op.progress, Time.unscaledDeltaTime);
                Report(value, onProgress);
                if (_tracker.CanActivate) op.allowSceneActivation = true;
                await RevTaskScheduler.NextFrame();
            }

            _tracker.Complete();
            Report(1f, onProgress);

            IsLoading = false;
            State = RevSceneLoadState.Done;
            string loaded = CurrentName;
            RevSceneLog.Info("[RevScene] 已进入场景：" + loaded);
            RevScene.RaiseLoaded(loaded);
        }

        // ==================== 同步（会卡帧）====================

        /// <summary>同步切换：调用这一帧就换成新场景（大场景会明显卡顿）。</summary>
        internal static void Load(string sceneName)
        {
            if (IsLoading)
            {
                RevSceneLog.Warning("[RevScene] 正在切换中，忽略本次同步请求：" + sceneName);
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                Fail("(空场景名)", "场景名不能为空");
                return;
            }

            Prepare(sceneName);
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

            Progress = 1f;
            IsLoading = false;
            State = RevSceneLoadState.Done;
            RevScene.RaiseProgress(1f);
            RevSceneLog.Info("[RevScene] 已同步进入场景：" + CurrentName);
            RevScene.RaiseLoaded(CurrentName);
        }

        // ==================== 公共步骤 ====================

        /// <summary>切之前：清理 + 状态就位 + 广播开始。</summary>
        /// <param name="label">场景名或 buildIndex 描述（只用于日志 / 事件）</param>
        /// <param name="minSeconds">进度条最短展示时长（秒）；同步切换用不到，默认 0</param>
        private static void Prepare(string label, float minSeconds = 0f)
        {
            if (RevScene.AutoClearPool)
            {
                int cleared = RevPool.ClearAll();       // 旧场景的池化实例不该跟着走
                if (cleared > 0) RevSceneLog.Info("[RevScene] 已清理对象池：" + cleared + " 个实例");
            }

            _tracker = new RevSceneProgressTracker(minSeconds);
            IsLoading = true;
            State = RevSceneLoadState.Loading;
            Progress = 0f;

            RevSceneLog.Info("[RevScene] 准备切换 → " + label);
            RevScene.RaiseStart(label);
            RevScene.RaiseProgress(0f);
        }

        /// <summary>进度广播（同时喂给门面事件与本次调用的回调；回调抛异常只报错，不中断加载）。</summary>
        private static void Report(float value, Action<float> onProgress)
        {
            Progress = value;
            RevScene.RaiseProgress(value);

            if (onProgress == null) return;
            try
            {
                onProgress(value);
            }
            catch (Exception e)
            {
                RevSceneLog.Error("[RevScene] 进度回调抛异常（已隔离，加载继续）：" + e);
            }
        }

        /// <summary>失败收尾：状态置 Failed + 广播原因（人话，不用你猜）。</summary>
        private static void Fail(string label, string reason)
        {
            IsLoading = false;
            State = RevSceneLoadState.Failed;
            RevSceneLog.Error("[RevScene] 切换失败：" + label + " —— " + reason);
            RevScene.RaiseFailed(label + "：" + reason);
        }
    }
}
