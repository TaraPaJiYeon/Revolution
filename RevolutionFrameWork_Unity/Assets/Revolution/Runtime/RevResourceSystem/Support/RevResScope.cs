// ============================================================
// RevResScope.cs —— 资源域（using 自动回收）
//
// 位置：Runtime\资源加载\
//
// 【解决什么痛点？】
//   如果每次 Load 都手动记 key、用完手动 DecRef，
//   一旦某条分支提前 return（异常、条件判断），就会漏掉释放 → 内存泄漏。
//   资源域把"加载"和"释放"绑到一起：进入 using 块加载的东西，离开时自动全部 DecRef。
//
// 【用法】
//   using (var scope = RevResManager.OpenScope())
//   {
//       var prefab = scope.Load<GameObject>(RevResPath.UI_MainForm, "MainForm", RevResGroup.UI);
//       var icon   = scope.Load<Sprite>(RevResPath.UI_Icon, "Hero_1001", RevResGroup.UI);
//   }   // ← 出大括号，上面两个资源自动 DecRef，即便中途 return 也保证执行
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution
{
    public class RevResScope : IDisposable
    {
        private readonly List<ulong> _keys = new List<ulong>();
        private bool _disposed;

        /// <summary>
        /// 在域内加载资源（自动登记，出域统一释放）。
        /// 参数是"根目录 + 资源名"两段，与 <see cref="RevResManager.Load{T}"/> 保持一致。
        /// </summary>
        public T Load<T>(string rootPath, string resName, RevResGroup group = RevResGroup.Unknown) where T : UnityEngine.Object
        {
            RevResHandle handle = RevResManager.Load(rootPath, resName, typeof(T), group);
            if (handle != null && handle.Key != 0) _keys.Add(handle.Key);
            return handle?.Content as T;
        }

        /// <summary>把外部已加载的句柄纳入本域管理</summary>
        public void Track(RevResHandle handle)
        {
            if (handle != null && handle.Key != 0) _keys.Add(handle.Key);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = 0; i < _keys.Count; i++)
                RevResManager.DecRef(_keys[i]);

            _keys.Clear();
        }
    }
}
