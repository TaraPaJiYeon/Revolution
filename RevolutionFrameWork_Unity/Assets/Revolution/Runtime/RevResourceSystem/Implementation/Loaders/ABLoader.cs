// ============================================================
// ABLoader.cs —— AB 加载器
// 承担三件事：
//   ① 从 streamingAssetsPath 加载 AB（本框架不做热更新，没有 persistentDataPath 覆盖路径）
//      · Android：包在 APK 内，File.Exists 不可用 → 直接 LoadFromFile
//      · WebGL （含微信/QQ 小游戏）：不能阻塞 + 路径是 URL → 同步加载不可用，必须走 LoadAsync
//   ② 主包 Manifest 依赖解析
//   ③ 包级引用计数（归零卸载）
// 资源级引用计数不在这里，由 ResManager 统一管。
//
// 异步统一用自研 RevTask（见 Runtime\RevTask），不依赖 UniTask、也不用协程。
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Revolution
{
    
    public class ABLoader : IResLoader
    {
        /// <summary>
        /// 一个已加载 AB 包的登记记录（_bundles 的 value）。
        /// 把「包本体」和「引用计数」捆成一个对象 —— 两者永远同步，
        /// 不会出现拆成两个字典时"一边有值、一边没值"的不同步脏状态。
        /// </summary>
        private class BundleEntry
        {
            /// <summary>
            /// 包本体：加载资源、Unload 卸载都靠它
            /// </summary>
            public AssetBundle bundle;

            /// <summary>
            /// 引用计数：Acquire +1、Release -1，减到 0 才真正卸载
            /// </summary>
            public int refCount;        
        }

        /// <summary>
        ///已加载的包：包名 → (bundle, 引用计数)
        /// </summary>
        private readonly Dictionary<string,BundleEntry> _bundles = new Dictionary<string,BundleEntry>();

        /// <summary>
        /// 正在加载中的包：同一个包被并发请求时复用同一个 RevTask，避免重复加载
        /// </summary>
        private readonly Dictionary<string, RevTask<AssetBundle>> _loading = new Dictionary<string, RevTask<AssetBundle>>();

        //主包
        private AssetBundle _mainBundle;
        //配置文件
        private AssetBundleManifest _manifest;

        // AB 的根目录：StreamingAssets/<平台名>/
        //   ★ 必须带平台子目录 —— 打包工具把产物拷到 Assets/StreamingAssets/<平台名>/（见 ABBuilderCore.CopyToStreamingAssets），
        //     而 Unity 又拿 BuildAssetBundles 的输出目录名当主包名，所以"平台目录名"同时也是"主包文件名"。
        //     少了这段，主包和所有分包都会找不到（报 BundleLoadFail）。
        private static string StreamingRoot => Application.streamingAssetsPath + "/" + MainName + "/";

        /// <summary>
        /// 主包名 = 打包产物目录名。
        /// Unity 会用 BuildAssetBundles 的输出目录名给 Manifest 主包命名，
        /// 而目录名由 ABBuildSetting.GetPlatformName(target) 决定
        /// —— ★ 两边必须一致，改这里要同步改那边。
        ///   iOS / Android / WebGL（微信、QQ 等小游戏同为 WebGL 构建）
        ///   桌面 Windows / macOS / Linux → 统一叫 "PC"
        /// </summary>
        private static string MainName
        {
            get
            {
#if UNITY_IOS
                return "iOS";
#elif UNITY_ANDROID
                return "Android";
#elif UNITY_WEBGL
                return "WebGL";
#else
                return "PC";        // 桌面（Windows / macOS / Linux）统一 PC；其他平台按需在此补分支
#endif
            }
        }

        // ==================== 同步路径 ====================

        /// <summary>确保主包 + Manifest 已加载</summary>
        private bool EnsureManifest()
        {
            if (_manifest != null) 
                return true;
            //加载主包
            _mainBundle = LoadBundle(MainName);
            // 主包缺失（AB 未构建 / 未拷到 StreamingAssets）
            if (_mainBundle == null) 
                return false;
            //通过主包加载Manifest
            _manifest = _mainBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            return _manifest != null;
        }


        /// <summary>从 StreamingAssets 同步加载指定 AB 包（WebGL 走不通，见下方注释</summary>
        private static AssetBundle LoadBundle(string abName)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL（含小游戏）：单线程、不能阻塞，File.Exists / LoadFromFile 都不可用 → 同步加载无解。
            // 只能走 LoadBundleAsync（UnityWebRequest）。返回 null 让上层报 BundleLoadFail，而不是假死。
            return null;
#elif UNITY_ANDROID && !UNITY_EDITOR
            // Android：streamingAssetsPath 在 APK 内，File.Exists 不可用，直接尝试加载
            return AssetBundle.LoadFromFile(StreamingRoot + abName);
#else
            string path = StreamingRoot + abName;
            if (File.Exists(path)) 
                return AssetBundle.LoadFromFile(path);
            return null;
#endif
        }

        /// <summary>加载包（含依赖），并给每个包 +1 引用</summary>
        private AssetBundle AcquireBundle(string abName)
        {
            if (!EnsureManifest()) 
                return null;
            // 先加载依赖（依赖必须先于主包就绪）
            foreach (var dep in _manifest.GetAllDependencies(abName))
                AcquireSingle(dep);

            return AcquireSingle(abName);
        }

        /// <summary>
        /// 取用「单个」AB 包：包级引用计数的唯一 +1 入口（与 ReleaseSingle 的 -1 配对）。
        ///
        ///   · 已在 _bundles 里 → 不重复加载，只 refCount++，复用同一个 bundle；
        ///   · 还没有           → 真加载一次，并登记为 refCount = 1。
        ///
        /// 复用的意义：同一个包被 N 个资源用到时，磁盘只读一次、内存只存一份；
        /// 而且只要还有人在用，计数就压不到 0，包就不会被 Unload 卸掉。
        ///
        /// 不查依赖（那是 AcquireBundle 的事），只管这个包本身。
        /// </summary>
        private AssetBundle AcquireSingle(string abName)
        {
            if (_bundles.TryGetValue(abName, out BundleEntry e) && e.bundle != null)
            {
                // 已有 → 复用，仅计数 +1
                e.refCount++;
                return e.bundle;
            }
            AssetBundle bundle = LoadBundle(abName);
            // 该 AB 包在 StreamingAssets 下不存在
            if (bundle == null) 
                return null;

            // 首次加载 → 登记为 1
            _bundles[abName] = new BundleEntry { bundle = bundle, refCount = 1 };
            return bundle;
        }

        /// <summary>释放包（引用归零则 Unload）</summary>
        public void ReleaseBundle(string abName)
        {
            if (!EnsureManifest()) return;
            //先去释放这个包的依赖包
            foreach (string dep in _manifest.GetAllDependencies(abName))
                ReleaseSingle(dep);

            ReleaseSingle(abName);
        }

        /// <summary>
        /// 归还「单个」AB 包：与 AcquireSingle 的 +1 配对的 -1 出口。
        ///
        ///   · 减完不为0 → 还有人在用，什么都不做；
        ///   · 减到0 → 没人用了 → Unload 卸载，并从 _bundles 移除
        ///                  （若之后又要用，会走 AcquireSingle 重新加载）。
        ///
        /// ★ Unload(false) 的 false 是重点：「不销毁从该包里 Load 出来的资源对象」。
        ///   传 true 会连别人正在用的贴图 / 预制体一起销毁（表现为空白、空引用）；
        ///   而资源实例的生命周期由 ResManager 统一管（它另有一套资源级引用计数），
        ///   所以这里只卸"包"这一层：省的是包的内存，不碰资源的内存。
        /// </summary>
        private void ReleaseSingle(string abName)
        {
            if (!_bundles.TryGetValue(abName, out BundleEntry e)) 
                return;

            if (--e.refCount <= 0)
            {
                // false：不销毁已加载对象（对象生命周期由 ResManager 管理）
                e.bundle.Unload(false);
                //从缓存当中移除
                _bundles.Remove(abName);
            }
        }

        /// <summary>全部释放（切场景 / 退出时兜底）</summary>
        public void ReleaseAll()
        {
            //遍历字典卸载ab包
            foreach(BundleEntry e in _bundles.Values)
                if(e.bundle != null)
                    e.bundle.Unload(false);

            //清空缓存
            _bundles.Clear();
            _loading.Clear();

            //卸载主包
            if (_mainBundle != null)
            {
                _mainBundle.Unload(false);
                _mainBundle = null;
            }
            _manifest = null;
        }

        /// <summary>
        /// 获取ab包的引用计数
        /// </summary>
        /// <param name="abName"></param>
        /// <returns></returns>
        public int GetBundleRefCount(string abName) => _bundles.TryGetValue(abName, out BundleEntry e) ? e.refCount : 0;


        // ==================== IResLoader接口实现 ====================

        public object Load(ResHandle handle, out ResLoadErrorReason err)
        {
            // RealPath 约定为 "包名|资源名"
            //   · 包名可能是"生效包名"，即带变体时形如 "ui_login.hd"（与构建出的文件名一致）
            //   · 资源名不含扩展名
            string[] parts = handle.RealPath.Split('|');
            // 拆不出两段 → 句柄本不该进 ABLoader（映射表坏了 / 策略匹配漏了）
            if (parts.Length != 2) 
            { 
                err = ResLoadErrorReason.PathNotMapped; 
                return null; 
            }
            //加载ab包
            AssetBundle bundle = AcquireBundle(parts[0]);
            if (bundle == null)
            {
                err = ResLoadErrorReason.BundleLoadFail;
                return null;
            }
            //加载ab包中的资源
            UnityEngine.Object asset = bundle.LoadAsset(parts[1], handle.ContentType);
            err = asset != null ? ResLoadErrorReason.None : ResLoadErrorReason.AssetLoadFail;
            return asset;

        }

        public void LoadAsync(ResHandle handle, Action<ResHandle> onFinished, RevCancellationToken token = null)
        {
            // 用 RevTask 串行加载；异常/取消都会被 catch + finally 兜住，
            // 保证 onFinished 一定被调用 —— 这是异步泵不死锁的前提。
            LoadAsyncInternal(handle, onFinished, token).Forget();
        }

        /// <summary>
        /// 异步加载一个句柄的完整流程（本类的核心）。调用链：
        ///   LoadAsync → LoadAsyncInternal
        ///                → EnsureManifestAsync（主包 + Manifest）
        ///                → AcquireSingleAsync（先依赖包，再主包）
        ///                → bundle.LoadAssetAsync（取资源本身）
        ///
        /// 【为什么每步之间都插一次 ThrowIfCancelled】
        ///   加载是"多段串行等待"，切场景时要能尽早中断；取消会直接跳到 catch，
        ///   状态记为 Cancelled —— 不会留下一个"加载到一半"的句柄。
        ///
        /// 【依赖为什么逐个串行 await，而不是并行】
        ///   ① 顺序不能乱：依赖必须先于主包就绪；
        ///   ② 串行才能让 AcquireSingleAsync 的 _loading 合并生效，
        ///      避免同一个包被多个资源并发请求时重复加载。
        ///
        /// 【异常一律不外抛】失败原因全部落在 handle.ErrorReason + MarkError()。
        ///   唯一不能省的是 finally：无论成功 / 失败 / 取消，onFinished 必须被调用一次
        ///   —— 异步泵正是靠这个回调推进队列，漏一次就会整队卡死。
        /// </summary>
        private async RevTask LoadAsyncInternal(ResHandle handle, Action<ResHandle> onFinished, RevCancellationToken token)
        {
            try
            {
                // RealPath 是策略查映射表后填的，约定为 "包名|资源名"（见同步版 Load 的说明）
                // parts[0] → 包名：查依赖、加载主包；parts[1] → 资源名：从包里取资源
                string[] parts = handle.RealPath.Split('|');
                // 拆不出两段 → 句柄本不该进 ABLoader（映射表坏了 / 策略匹配漏了）
                if (parts.Length != 2)      
                {
                    handle.ErrorReason = ResLoadErrorReason.PathNotMapped;
                    handle.MarkError();
                    return;                                   // 交给 finally 回调
                }

                // 前置条件：Manifest（依赖查询表）必须先就绪，下面 GetAllDependencies 才用得上
                // （懒加载：只有进程内首次会真去读主包，之后近乎零开销）
                await EnsureManifestAsync();
                // 取消检查点：上面这次 await 期间可能已被取消（切场景 / CancelAll）
                // 取消会抛 RevOperationCanceledException → 跳到 catch 记为 Cancelled，不算异常逃逸
                token?.ThrowIfCancelled();

                // 依赖包：逐个串行加载（顺序重要：依赖必须先于目标包就绪）
                foreach (string dep in _manifest.GetAllDependencies(parts[0]))
                    await AcquireSingleAsync(dep);

                token?.ThrowIfCancelled();

                //加载目标包
                AssetBundle bundle = await AcquireSingleAsync(parts[0]);
                if (bundle == null)
                {
                    handle.ErrorReason = ResLoadErrorReason.BundleLoadFail;
                    handle.MarkError();
                    return;
                }
                // 从包里取资源（Unity 的异步操作可以直接 await）
                AssetBundleRequest req = bundle.LoadAssetAsync(parts[1], handle.ContentType);
                await req;
                //设置句柄的资源
                handle.ErrorReason = ResLoadErrorReason.None;
                handle.SetContent(req.asset);
            }
            catch (RevOperationCanceledException)
            {
                handle.ErrorReason = ResLoadErrorReason.Cancelled;
                handle.MarkError();
            }
            catch (Exception)
            {
                // 本方案统一不打日志：失败原因记在 handle.ErrorReason 上
                handle.ErrorReason = ResLoadErrorReason.BundleLoadFail;
                handle.MarkError();
            }
            finally
            {
                onFinished?.Invoke(handle);     // ★ 契约：成功 / 失败 / 取消，一律回调
            }
        }

        /// <summary>
        /// 确保主包 + AssetBundleManifest 就绪，异步版。
        /// </summary>
        private async RevTask EnsureManifestAsync()
        {
            if (_manifest != null) return;

            _mainBundle = await LoadBundleAsync(MainName);
            if (_mainBundle != null)
                _manifest = _mainBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }

        /// <summary>
        /// 从 StreamingAssets 异步加载指定 AB 包（平台分派：WebGL 走 UnityWebRequest，其余走 LoadFromFileAsync）。
        ///
        /// 失败一律返回 null、不抛异常 —— 由调用方统一记成 BundleLoadFail，
        /// 让"文件不存在 / 下载失败 / 平台不匹配"在业务层是同一种可预期结果。
        /// 这里也不做 File.Exists 预判：WebGL 没有本地文件概念，Android 的包在 APK 内同样查不到。
        /// </summary>
        private static async RevTask<AssetBundle> LoadBundleAsync(string abName)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL（含小游戏）：streamingAssetsPath 是 URL/虚拟路径，只能用 UnityWebRequest 下载。
            // await 直接吃 UnityWebRequestAsyncOperation（它继承 AsyncOperation，见 RevTaskUnityExtensions）。
            using (UnityWebRequest www = UnityWebRequestAssetBundle.GetAssetBundle(StreamingRoot + abName))
            {
                await www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success) return null;   // 上层报 BundleLoadFail
                return DownloadHandlerAssetBundle.GetContent(www);              // using 释放后 bundle 依然有效
            }
#else
            //调用unity官方的API来异步加载ab包
            AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(StreamingRoot + abName);
            await req;
            return req.assetBundle;
#endif
        }

        /// <summary>
        /// 取用「单个」AB 包（同步版 AcquireSingle 的异步版），多了一层"并发合并"。
        /// 三种情况：
        ///   ① 已在 _bundles        → 直接复用，refCount++；
        ///   ② 正在加载（_loading） → 搭车等同一个 RevTask 完成，再 refCount++（不重复发请求）；
        ///   ③ 都没命中             → 由自己发起加载，完成后登记为 refCount = 1。
        ///
        /// 【为什么需要 _loading】
        ///   同一帧里多个资源常常要同一个包（尤其依赖包）。没有它，N 个请求会同时触发
        ///   N 次加载 —— 既浪费 IO，又可能把同一个包加载出多份实例、计数也对不上。
        ///
        /// 【计数为什么恰好等于请求数】
        ///   发起者登记 1，每个搭车者补 1 → 合计 N 次请求 = N 次引用。
        ///   这里有个隐式的顺序依赖：搭车者是被"同一个 Promise"唤醒的，
        ///   而 Promise 的续体按注册先后执行 —— 发起者先注册、就先跑完登记，
        ///   搭车者醒来时才一定能在 _bundles 里查到条目并 +1。（改这里务必保持这个顺序）
        /// </summary>
        private async RevTask<AssetBundle> AcquireSingleAsync(string abName)
        {
            // 已在内存 → 复用
            if (_bundles.TryGetValue(abName, out BundleEntry e) && e.bundle != null)
            {
                e.refCount++;                              
                return e.bundle;
            }

            // 有人正在加载同一个包 → 搭他的车，等同一个 RevTask，避免重复加载
            if (_loading.TryGetValue(abName, out RevTask<AssetBundle> task))
            {
                AssetBundle existed = await task;

                if (existed != null && _bundles.TryGetValue(abName, out BundleEntry entry))
                    entry.refCount++; // 搭车也要占一份引用

                return existed;
            }

            // 首个请求 → 自己发起；先把任务挂到 _loading，供后来的请求搭车
            RevTask<AssetBundle> newTask = LoadBundleAsync(abName);
            _loading[abName] = newTask;

            AssetBundle bundle = await newTask;
            // 加载已结束（成功失败都撤掉搭车点）
            _loading.Remove(abName);

            if (bundle != null)
                _bundles[abName] = new BundleEntry { bundle = bundle, refCount = 1 };   // 发起者这份算 1

            return bundle;
        }
    }
}