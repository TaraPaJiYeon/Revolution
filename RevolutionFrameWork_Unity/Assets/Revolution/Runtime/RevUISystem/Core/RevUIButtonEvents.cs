// ============================================================
// RevUIButtonEvents.cs —— 按钮事件的方法特性（纯 C#，不引用 UnityEngine）
//
// 位置：Runtime\RevUISystem\Core\
//
// 【它解决什么】
//   控件事件一共三种接法，这里是最省事的一种 —— **给方法标特性**：
//
//     [RevButtonClick("btnStart")]      void OnStartClicked()                 => StartGame();
//     [RevButtonClick("btnBuy")]        void OnBuyClicked(string nodeName)    => Buy(nodeName);
//     [RevButtonLongPress("btnSkill")]  void OnSkillHold()                    => ShowSkillTip();
//     [RevButtonLoosen("btnMove")]      void OnMovePickedUp()                 => StopMove();
//
//   ★ 节点名规则：写节点名即可（大小写敏感，和 [RevBind] 的匹配规则一致）；
//     写多级路径时只取最后一段（"Top/btnStart" 等价于 "btnStart"）。
//   ★ 一个方法标一个特性；同一个节点可以挂多个方法（全部都会被调用）。
//   ★ 支持两种签名：`void M()` 与 `void M(string nodeName)`；其它签名在扫描时明确报错（不静默）。
//
// 【什么时候被调用】
//   面板 / Part 按节点名派发控件事件时：
//     · 点击      → UGUI Button.onClick（或自定义控件的 d.Click）
//     · 长按 / 松开 → 框架给交互节点挂的指针继电器（按住超过 RevUISetting.ButtonLongPressSeconds
//                    再松开 = 长按；手指/鼠标抬起 = 松开）
//   同一次事件里，"重写的 OnClick(节点名)" 与 "方法特性" **两条路都会走到** ——
//   所以同一件事只在一处做（别两边都写）。
//
// 【为什么每类型只扫一次】
//   反射扫方法不便宜，而面板会被反复开合：结果按类型缓存（和 RevUIBindPlan 一个套路）。
//   调用时用 MethodInfo.Invoke —— 点击是**低频**事件（不是逐帧热路径），
//   用一次小分配换"缓存简单 + 签名校验明确"是划算的。
// ============================================================
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Revolution
{
    /// <summary>按钮事件种类（供派发与校验使用）</summary>
    public enum RevUIButtonEventKind : byte
    {
        /// <summary>点击（按下后在同一控件内抬起）</summary>
        Click = 0,

        /// <summary>长按（按住超过阈值再松开）</summary>
        LongPress = 1,

        /// <summary>松开（指针在控件上抬起，无论按了多久）</summary>
        Loosen = 2,
    }

    /// <summary>
    /// 按钮事件特性的共同基类：三种特性都只有一个"控件名"参数，
    /// 扫描器只认这个基类（业务也可以用它做统一处理，例如自研的监听注册器）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public abstract class RevButtonEventAttribute : Attribute
    {
        /// <summary>控件所在节点的名字（或路径，取最后一段）</summary>
        public string Name;

        protected RevButtonEventAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// 点击：写在方法上，控件被点击时调用。
    /// <code>[RevButtonClick("btnStart")] void OnStartClicked() => StartGame();</code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class RevButtonClickAttribute : RevButtonEventAttribute
    {
        public RevButtonClickAttribute(string name) : base(name) { }
    }

    /// <summary>
    /// 长按：按住超过 <c>RevUISetting.ButtonLongPressSeconds</c> 后松开时调用。
    /// <code>[RevButtonLongPress("btnSkill")] void OnSkillHold() => ShowSkillTip();</code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class RevButtonLongPressAttribute : RevButtonEventAttribute
    {
        public RevButtonLongPressAttribute(string name) : base(name) { }
    }

    /// <summary>
    /// 松开：指针在控件上抬起时调用（无论按了多久）。
    /// <code>[RevButtonLoosen("btnMove")] void OnMovePickedUp() => StopMove();</code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class RevButtonLoosenAttribute : RevButtonEventAttribute
    {
        public RevButtonLoosenAttribute(string name) : base(name) { }
    }

    /// <summary>
    /// 按钮事件特性的扫描结果与派发口（按类型缓存；纯 C#，能被工程外断言直接链接编译）。
    /// </summary>
    public static class RevUIButtonEvents
    {
        private struct Handler
        {
            public MethodInfo Method;
            public bool TakesName;
            public string Describe;      // "BagPanel.OnBuyClicked"，出错时报出来
        }

        private sealed class KindMap
        {
            public readonly Dictionary<string, List<Handler>> ByNode = new Dictionary<string, List<Handler>>(8);
            public readonly List<string> Nodes = new List<string>(8);
        }

        private sealed class TypeMap
        {
            public readonly KindMap[] Kinds = new KindMap[3];
            public bool Any;

            public TypeMap()
            {
                for (int i = 0; i < Kinds.Length; i++) Kinds[i] = new KindMap();
            }
        }

        private static readonly Dictionary<Type, TypeMap> Cache = new Dictionary<Type, TypeMap>();
        private static readonly TypeMap Empty = new TypeMap();

        /// <summary>扫描 / 调用出问题时上报（Support 侧接到 RevUILog；框架本身不打日志）</summary>
        public static Action<Exception, string> OnException;

        /// <summary>这个类型有没有任何按钮方法特性</summary>
        public static bool WantsButtonEvents(Type type) => GetMap(type).Any;

        /// <summary>是否声明了"长按 / 松开"（这两个需要框架给交互节点挂指针继电器）</summary>
        public static bool WantsPressEvents(Type type)
        {
            TypeMap map = GetMap(type);
            return map.Kinds[(int)RevUIButtonEventKind.LongPress].Nodes.Count > 0
                || map.Kinds[(int)RevUIButtonEventKind.Loosen].Nodes.Count > 0;
        }

        /// <summary>某种类声明了哪些节点名（绑定器用它做"节点是否存在"的校验）</summary>
        public static string[] NodeNames(Type type, RevUIButtonEventKind kind)
            => GetMap(type).Kinds[(int)kind].Nodes.ToArray();

        /// <summary>这个类型 + 这个种类有没有特性</summary>
        public static bool Wants(Type type, RevUIButtonEventKind kind)
            => GetMap(type).Kinds[(int)kind].Nodes.Count > 0;

        /// <summary>
        /// 派发：把事件交给所有匹配 <paramref name="nodeName"/> 的方法。
        /// **逐条隔离**（一个方法抛异常不影响同节点的其它方法）。
        /// </summary>
        public static void Invoke(object target, string nodeName, RevUIButtonEventKind kind)
        {
            if (target == null || string.IsNullOrEmpty(nodeName)) return;

            KindMap map = GetMap(target.GetType()).Kinds[(int)kind];
            if (!map.ByNode.TryGetValue(nodeName, out List<Handler> handlers)) return;

            for (int i = 0; i < handlers.Count; i++)
            {
                Handler h = handlers[i];
                try
                {
                    if (h.TakesName) h.Method.Invoke(target, new object[] { nodeName });
                    else h.Method.Invoke(target, null);
                }
                catch (Exception e)
                {
                    OnException?.Invoke(e, h.Describe + "(" + nodeName + ")");
                }
            }
        }

        /// <summary>清缓存（域重载 / 断言用）</summary>
        public static void ClearCache() => Cache.Clear();

        // ── 扫描（每类型只做一次）────────────────────────────

        private static TypeMap GetMap(Type type)
        {
            if (type == null) return Empty;
            if (Cache.TryGetValue(type, out TypeMap map)) return map;

            map = Build(type);
            Cache[type] = map;
            return map;
        }

        private static TypeMap Build(Type type)
        {
            var map = new TypeMap();

            // 逐层取 DeclaredOnly：既能覆盖"基类里写特性、派生类继承"的常见写法，
            // 又不会把基类的字段/方法重复算两遍（和 RevUIBindPlan 收集字段同一个套路）。
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                MethodInfo[] methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m.IsStatic || m.IsAbstract) continue;

                    Add(map, m, typeof(RevButtonClickAttribute), RevUIButtonEventKind.Click);
                    Add(map, m, typeof(RevButtonLongPressAttribute), RevUIButtonEventKind.LongPress);
                    Add(map, m, typeof(RevButtonLoosenAttribute), RevUIButtonEventKind.Loosen);
                }
            }

            return map;
        }

        private static void Add(TypeMap map, MethodInfo method, Type attributeType, RevUIButtonEventKind kind)
        {
            object[] attrs = method.GetCustomAttributes(attributeType, false);
            if (attrs.Length == 0) return;

            var attr = (RevButtonEventAttribute)attrs[0];
            string raw = attr != null ? attr.Name : null;
            string node = NormalizeNode(raw);

            if (string.IsNullOrEmpty(node))
            {
                Report("特性没写控件名", method, raw);
                return;
            }

            // 签名校验：只支持 () 和 (string)；其它签名明确报错（不静默忽略）
            ParameterInfo[] ps = method.GetParameters();
            bool takesName;
            if (ps.Length == 0)
            {
                takesName = false;
            }
            else if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
            {
                takesName = true;
            }
            else
            {
                Report("方法签名不支持（只支持 M() 或 M(string)）", method, raw);
                return;
            }

            if (method.ReturnType != typeof(void))
            {
                Report("方法必须有 void 返回值", method, raw);
                return;
            }

            KindMap kindMap = map.Kinds[(int)kind];
            if (!kindMap.ByNode.TryGetValue(node, out List<Handler> list))
            {
                list = new List<Handler>(2);
                kindMap.ByNode[node] = list;
                kindMap.Nodes.Add(node);
            }

            list.Add(new Handler
            {
                Method = method,
                TakesName = takesName,
                Describe = method.DeclaringType != null ? method.DeclaringType.Name + "." + method.Name : method.Name,
            });
            map.Any = true;
        }

        private static void Report(string why, MethodInfo method, string raw)
        {
            string where = method.DeclaringType != null ? method.DeclaringType.Name + "." + method.Name : method.Name;
            OnException?.Invoke(new ArgumentException(why + "：" + where + "（控件名 \"" + (raw ?? "") + "\"）"),
                "按钮特性");
        }

        /// <summary>
        /// 控件名规范化：去空白；写多级路径时取最后一段（"Top/btnStart" → "btnStart"）。
        /// ★ 与 [RevBind] 一样**区分大小写**：规则越少越好预测，错了会明确报"找不到节点"。
        /// </summary>
        public static string NormalizeNode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            string name = raw.Trim();
            int slash = name.LastIndexOf('/');
            if (slash >= 0 && slash < name.Length - 1) name = name.Substring(slash + 1).Trim();

            return name.Length == 0 ? null : name;
        }

        /// <summary>已经扫描缓存了多少个类型（诊断用）</summary>
        public static int CachedTypeCount => Cache.Count;
    }
}
