// RevSequenceServices.cs —— 服务容器（把业务依赖挡在框架外）
// 【用法】runner.Services.Add<ISoundService>(实现) → 步骤里 ctx.Get<ISoundService>()。
// 【要点】泛型要写「接口类型」，否则 ctx.Get<IXxx>() 取不到（本项目易踩的坑）。

using System;
using System.Collections.Generic;

namespace Revolution
{
    /// <summary>
    /// 动作序列的服务容器：按"类型"注入/取出业务服务（具体是什么服务由业务自己决定，框架不预设）。
    /// <para>框架本身不认识任何业务接口；步骤通过 <c>context.Get&lt;T&gt;()</c> 取得需要的服务。</para>
    /// </summary>
    public sealed class RevSequenceServices
    {
        private readonly Dictionary<Type, object> _map;

        /// <summary>建一个服务容器</summary>
        /// <param name="capacity">预估服务数量（只是初始容量，不影响功能）</param>
        public RevSequenceServices(int capacity = 8)
            => _map = new Dictionary<Type, object>(capacity);

        /// <summary>已注册服务数量（框架内部 / 调试用）</summary>
        internal int Count => _map.Count;

        /// <summary>
        /// 注册服务（键 = 泛型参数 T，所以<b>要显式写接口类型</b>：
        /// <c>services.Add&lt;ISoundService&gt;(mySound)</c>）。
        /// </summary>
        /// <returns>返回自身，支持链式注册</returns>
        public RevSequenceServices Add<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            _map[typeof(T)] = service;      // 后注册覆盖先注册（便于测试时用 Fake 覆盖真实现）
            return this;
        }

        /// <summary>取服务；没注册返回 null（步骤里请自行判空，或改用 TryGet）</summary>
        public T Get<T>() where T : class
            => _map.TryGetValue(typeof(T), out object service) ? service as T : null;

        /// <summary>尝试取服务</summary>
        public bool TryGet<T>(out T service) where T : class
        {
            if (_map.TryGetValue(typeof(T), out object value))
            {
                service = value as T;
                return service != null;
            }

            service = null;
            return false;
        }

        /// <summary>移除服务（框架内部：测试替换实现、宿主销毁时清理）</summary>
        internal bool Remove<T>() where T : class => _map.Remove(typeof(T));

        /// <summary>清空所有服务（框架内部：宿主销毁时调用，避免持有已销毁的业务对象）</summary>
        internal void Clear() => _map.Clear();
    }
}
