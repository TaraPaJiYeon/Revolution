// ============================================================
// ResourcesLoader.cs —— Resources 加载器
//
// 位置：Runtime\资源加载\Sub\
//
// 兜底用：只处理 "Res/" 前缀的特殊资源，以及 AB 加载失败后的兜底。
// ============================================================
using System;
using UnityEngine;

namespace Revolution
{
    public class ResourcesLoader : IResLoader
    {
        public object Load(ResHandle handle, out ResLoadErrorReason err)
        {
            UnityEngine.Object obj = Resources.Load(handle.RealPath, handle.ContentType);
            err = obj != null ? ResLoadErrorReason.None : ResLoadErrorReason.FileNotExist;
            return obj;
        }

        public void LoadAsync(ResHandle handle, Action<ResHandle> onFinished, RevCancellationToken token = null)
        {
            ResourceRequest req = Resources.LoadAsync(handle.RealPath, handle.ContentType);

            req.completed += _ =>
            {
                // 异步完成：写入内容并按契约回调
                handle.ErrorReason = req.asset != null ? ResLoadErrorReason.None : ResLoadErrorReason.FileNotExist;
                handle.SetContent(req.asset);
                onFinished?.Invoke(handle);
            };
        }
    }
}
