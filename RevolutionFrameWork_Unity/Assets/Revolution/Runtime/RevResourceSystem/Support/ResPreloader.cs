// ============================================================
// ResPreloader.cs —— 资源预加载器
//
// 位置：Runtime\ResourceSystem\Support\
//
// 【解决什么问题？】
//   进战斗 / 开界面之前，如果不提前把资源铺好，玩家会看到"按了技能卡一下"。
//   预加载就是"在空闲时，把后面大概率要用的资源先加载进内存"。
//
// 【关键设计：预加载"持有一份引用"】
//   预加载 = 正常异步加载 + 给句柄打一个 Preloaded 标志。
//   为什么要持有引用？—— 否则加载完没人引用，下一次自动卸载就被清掉了，等于白做。
//   这份引用由预加载系统记账，用 ReleaseGroup / ReleaseAll 显式归还。
//
// 【幂等】同一资源重复预加载不会重复占引用（第二次命中缓存，只 +1 引用；
//         若已带 Preloaded 标志则直接跳过，不再重复持有）。
//
// 【优先级】预加载默认走 Background，不抢玩家当前操作资源的 IO。
//
// 【参数口径】和其它地方一致："根目录 + 资源名"两段（根目录用生成的 RevResPath 常量）。
//   批量有三种写法：
//     · 同一目录一批：Preload(RevResPath.UI_Icon, new[] { "Hero_1001", "Hero_1002" }, ResGroup.UI)
//     · 多目录混合：  Preload(new[] { (RevResPath.Hero, "1001"), (RevResPath.Skill, "1001_Effect") }, ResGroup.Battle)
//     · 单个：        Preload(RevResPath.UI_Icon, "Hero_1001", ResGroup.UI)
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution
{
    /// <summary>
    /// 一次预加载任务的句柄：可以拿进度、判断完成、取消。
    /// </summary>
    public sealed class ResPreloadTask
    {
        internal ResPreloadTask(int total) { Total = total; }

        /// <summary>本次预加载涉及的资源总数（已去重）</summary>
        public int Total { get; private set; }

        /// <summary>已处理数量（无论成功失败）</summary>
        public int Finished { get; private set; }

        /// <summary>失败数量</summary>
        public int Failed { get; private set; }

        /// <summary>进度 0~1</summary>
        public float Progress => Total <= 0 ? 1f : (float)Finished / Total;

        /// <summary>是否已完成（全部回调结束，或被取消）</summary>
        public bool IsDone { get; private set; }

        /// <summary>是否被取消（取消后不再把新加载成功的资源标记为"预加载持有"）</summary>
        public bool IsCancelled { get; private set; }

        /// <summary>所属分组（便于整组释放）</summary>
        public ResGroup Group { get; internal set; }

        /// <summary>进度回调：每完成一个资源调用一次</summary>
        public event Action<ResPreloadTask> OnProgress;

        /// <summary>完成回调：全部处理完（或被取消）调用一次</summary>
        public event Action<ResPreloadTask> OnCompleted;

        /// <summary>
        /// 取消本次预加载。
        /// 【语义】只影响"后续结果"：不再把新加载成功的资源标记为预加载持有，并立即结束任务通知。
        ///        已经加载并持有的那部分，请调用 ResPreloader.Release(group) 归还。
        /// </summary>
        public void Cancel()
        {
            if (IsDone) return;
            IsCancelled = true;
            Complete();
        }

        internal void ReportOne(bool success)
        {
            Finished++;
            if (!success) Failed++;
            OnProgress?.Invoke(this);
        }

        internal void Complete()
        {
            if (IsDone) return;
            IsDone = true;
            OnCompleted?.Invoke(this);
        }
    }

    public static class ResPreloader
    {
        /// <summary>
        /// 预加载**一个**资源（异步、后台优先级）。
        /// 失败不会抛异常，最后可通过 task.Failed 查看失败数。
        /// </summary>
        public static ResPreloadTask Preload(string rootPath, string resName,
            ResGroup group = ResGroup.Unknown,
            ResLoadPriority priority = ResLoadPriority.Background,
            Action<ResPreloadTask> onProgress = null,
            Action<ResPreloadTask> onCompleted = null)
        {
            return Preload(new[] { (rootPath, resName) }, group, priority, onProgress, onCompleted);
        }

        /// <summary>
        /// 预加载**同一目录下的多个**资源。
        /// 例：Preload(RevResPath.UI_Icon, new[] { "Hero_1001", "Hero_1002" }, ResGroup.UI);
        /// </summary>
        public static ResPreloadTask Preload(string rootPath, IEnumerable<string> resNames,
            ResGroup group = ResGroup.Unknown,
            ResLoadPriority priority = ResLoadPriority.Background,
            Action<ResPreloadTask> onProgress = null,
            Action<ResPreloadTask> onCompleted = null)
        {
            var items = new List<(string rootPath, string resName)>();
            if (resNames != null)
            {
                foreach (string name in resNames)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    items.Add((rootPath, name));
                }
            }

            return Preload(items, group, priority, onProgress, onCompleted);
        }

        /// <summary>
        /// 预加载一批资源（可跨多个目录）。
        /// 例：Preload(new[] { (RevResPath.Hero, "1001"), (RevResPath.Skill, "1001_Effect") }, ResGroup.Battle);
        /// 资源不存在 / 加载失败不会中断整批，最后可通过 task.Failed 查看失败数。
        /// </summary>
        /// <param name="items">"根目录 + 资源名"列表</param>
        /// <param name="group">业务分组（决定归属，便于整组释放）</param>
        /// <param name="priority">加载优先级（默认 Background，不抢前台）</param>
        /// <param name="onProgress">进度回调（可为 null）</param>
        /// <param name="onCompleted">完成回调（可为 null）</param>
        public static ResPreloadTask Preload(IEnumerable<(string rootPath, string resName)> items,
            ResGroup group = ResGroup.Unknown,
            ResLoadPriority priority = ResLoadPriority.Background,
            Action<ResPreloadTask> onProgress = null,
            Action<ResPreloadTask> onCompleted = null)
        {
            // ① 去重：同一批里重复写同一个资源很常见，别重复占引用。
            //    用"两段的键"去重（不拼字符串），和资源系统的缓存键同一套算法。
            var unique = new List<(string rootPath, string resName)>();
            var seen = new HashSet<ulong>();

            if (items != null)
            {
                foreach ((string rootPath, string resName) item in items)
                {
                    if (!ResPathUtil.IsValidResName(item.resName)) continue;
                    if (seen.Add(ResPathUtil.ComputeKey(item.rootPath, item.resName))) unique.Add(item);
                }
            }

            var task = new ResPreloadTask(unique.Count) { Group = group };
            if (onProgress != null) task.OnProgress += onProgress;
            if (onCompleted != null) task.OnCompleted += onCompleted;

            if (unique.Count == 0)
            {
                task.Complete();
                return task;
            }

            // ② 逐个发起异步加载
            foreach ((string rootPath, string resName) item in unique)
            {
                // 用 typeof(UnityEngine.Object) 加载：预加载事先不知道具体类型，
                // 而三种加载器都能用"基类类型"把资源取出来（后续按具体类型取用不受影响）。
                ResManager.LoadAsync(item.rootPath, item.resName, typeof(UnityEngine.Object), h =>
                {
                    bool ok = h != null && h.IsLoaded;

                    // 成功且未被取消 → 打上"预加载持有"标志
                    if (ok && !task.IsCancelled) ResManager.MarkPreloaded(h);

                    task.ReportOne(ok);
                    if (task.Finished >= task.Total) task.Complete();
                }, group, priority);
            }

            return task;
        }

        /// <summary>按分组预加载（语义更清晰的别名：同一目录下一批）</summary>
        public static ResPreloadTask PreloadGroup(ResGroup group, string rootPath, IEnumerable<string> resNames,
            ResLoadPriority priority = ResLoadPriority.Background,
            Action<ResPreloadTask> onProgress = null,
            Action<ResPreloadTask> onCompleted = null)
            => Preload(rootPath, resNames, group, priority, onProgress, onCompleted);

        // ==================== 释放 ====================

        /// <summary>归还某一分组全部"预加载持有"的引用</summary>
        public static int Release(ResGroup group) => ResManager.ReleasePreloaded(group);

        /// <summary>归还全部"预加载持有"的引用</summary>
        public static int ReleaseAll() => ResManager.ReleasePreloadedAll();

        // ==================== 查询 ====================

        /// <summary>统计当前"预加载持有"的资源数量</summary>
        public static int GetPreloadedCount() => GetPreloadedCount(null);

        /// <summary>
        /// 统计某一分组下"预加载持有"的资源数量。
        /// ★ 参数是 ResGroup?（可空）：null = 不筛分组、统计全部 —— 无参重载就是这么调的，
        ///   所以下面必须用 group.HasValue / group.Value。
        /// </summary>
        public static int GetPreloadedCount(ResGroup? group)
        {
            int count = 0;
            List<ResHandle> all = ResManager.GetAllHandles();
            foreach (ResHandle h in all)
            {
                if (!h.HasFlag(ResInstanceFlag.Preloaded)) continue;
                if (group.HasValue && h.Group != group.Value) continue;
                count++;
            }
            return count;
        }
    }
}
