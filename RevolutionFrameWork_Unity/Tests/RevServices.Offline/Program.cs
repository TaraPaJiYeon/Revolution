// ============================================================
// Program.cs —— 用真实 RevServices 源码复现“懒创建错过首个事件”，并锁住 eager 的生命周期契约。
// 不引 Unity、不修改框架静态宿主；每个用例结束都注销服务，防止上一例污染下一例。
// ============================================================
using System;
using Revolution;

internal static class Program
{
    private static int _passed;

    private static void Main()
    {
        Check("默认懒创建，首次取用才订阅事件", LazyIsDefault);
        Check("eager 注册返回前订阅事件且只创建一次", EagerSubscribesImmediately);
        Check("eager 带 owner 注销并只释放自己的实例", EagerOwnerRelease);
        Check("eager 重复注册不会覆盖也不会创建第二个实例", DuplicateFailsBeforeConstruction);
        Check("eager 构造抛错不留下注册，之后可以重试", ConstructorFailureRollsBack);
        Check("eager OnInit 抛错且 Dispose 抛错时保留原始失败", InitFailureRollsBack);
        Check("默认懒创建 OnInit 失败仍保留登记可重试", LazyFailureCanRetry);
        Check("Tick 宿主安装抛错回滚 Tick 和实例", TickHookFailureRollsBack);
        Check("创建时自注销并注册同类型不误删新登记", ReplacementIsNotRemoved);
        Console.WriteLine("RevServices 工程外回归：通过 " + _passed + " 项，失败 0 项");
    }

    private static void Check(string name, Action test)
    {
        RevServices.UnregisterAll();
        RevServices.Hub.TickableCreated = null;
        Events.Changed = null;
        Events.Received = 0;
        EagerListener.Created = 0;
        EagerListener.Disposed = 0;
        InitFails.Disposed = 0;
        TickFails.Disposed = 0;
        try
        {
            test();
            _passed++;
            Console.WriteLine("通过 " + name);
        }
        finally
        {
            RevServices.Hub.TickableCreated = null;
            RevServices.UnregisterAll();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new Exception(message); }
    }

    private static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException error) { return error; }
        throw new Exception("期望抛出 " + typeof(TException).Name);
    }

    private static void LazyIsDefault()
    {
        RevServices.Register<IListener, EagerListener>();
        Require(EagerListener.Created == 0 && RevServices.Count == 1, "默认注册不应立刻 new");
        Events.Raise();
        Require(Events.Received == 0, "还没有实例时不能收到首个事件");
        IListener service = RevServices.GetRequired<IListener>();
        Require(EagerListener.Created == 1, "首次取用应创建");
        Events.Raise();
        Require(Events.Received == 1, "取用后应收到事件");
        Require(ReferenceEquals(service, RevServices.GetRequired<IListener>()), "应复用同一个实例");
    }

    private static void EagerSubscribesImmediately()
    {
        RevServices.Register<IListener, EagerListener>(eager: true);
        Require(EagerListener.Created == 1 && RevServices.Dump().Contains("已创建 1 个"), "返回前应已创建");
        Events.Raise();
        Require(Events.Received == 1, "注册返回后的第一次事件应已有人监听");
        RevServices.GetRequired<IListener>();
        Require(EagerListener.Created == 1, "后续取用不得重复构造");
        Require(RevServices.Unregister<IListener>() && EagerListener.Disposed == 1, "注销须释放一次");
        Events.Raise();
        Require(Events.Received == 1, "注销后事件必须退订");
    }

    private static void EagerOwnerRelease()
    {
        object owner = new object();
        object another = new object();
        RevServices.Register<IListener, EagerListener>(owner: owner, eager: true);
        RevServices.Register<IOther, Other>(owner: another);
        Require(RevServices.UnregisterAllOf(owner) == 1 && EagerListener.Disposed == 1, "只能释放自己的服务");
        Require(RevServices.IsRegistered<IOther>() && !RevServices.IsRegistered<IListener>(), "不应误删其他 owner");
        Require(RevServices.UnregisterAllOf(owner) == 0 && EagerListener.Disposed == 1, "重复清理不得二次释放");
    }

    private static void DuplicateFailsBeforeConstruction()
    {
        RevServices.Register<IListener, EagerListener>(eager: true);
        Throws<InvalidOperationException>(() => RevServices.Register<IListener, NeverConstruct>(eager: true));
        Require(EagerListener.Created == 1 && RevServices.Count == 1, "重复注册不得覆盖旧登记");
        Require(RevServices.GetRequired<IListener>() is EagerListener, "旧实例仍能取到");
    }

    private static void ConstructorFailureRollsBack()
    {
        // CLR 对泛型 new T() 的构造异常可能包一层 TargetInvocationException；框架应原样传播，不再包成别的异常。
        var error = Throws<System.Reflection.TargetInvocationException>(() => RevServices.Register<IListener, ConstructorFails>(eager: true));
        Require(error.InnerException is InvalidOperationException && error.InnerException.Message == "ctor failure", "应保留构造的真实失败原因");
        Require(RevServices.Count == 0 && !RevServices.IsRegistered<IListener>(), "失败后不应留下登记");
        RevServices.Register<IListener, EagerListener>(eager: true);
        Require(EagerListener.Created == 1, "失败后应允许正常重新注册");
    }

    private static void InitFailureRollsBack()
    {
        var error = Throws<InvalidOperationException>(() => RevServices.Register<IListener, InitFails>(eager: true));
        Require(error.Message == "init failure", "Dispose 的次生异常不能盖过 OnInit 原始异常");
        Require(InitFails.Disposed == 1, "失败实例应尝试 Dispose 恰好一次");
        Require(RevServices.Count == 0 && !RevServices.IsRegistered<IListener>(), "eager 初始化失败后不应留下登记");
    }

    private static void LazyFailureCanRetry()
    {
        RevServices.Register<IListener, InitFails>();
        Throws<InvalidOperationException>(() => RevServices.GetRequired<IListener>());
        Require(RevServices.IsRegistered<IListener>() && RevServices.Count == 1, "默认懒创建失败须保留登记");
        Throws<InvalidOperationException>(() => RevServices.GetRequired<IListener>());
        Require(InitFails.Disposed == 2, "再次取用应重试并释放失败实例");
    }

    private static void TickHookFailureRollsBack()
    {
        RevServices.Hub.TickableCreated = () => { throw new InvalidOperationException("hook failure"); };
        var error = Throws<InvalidOperationException>(() => RevServices.Register<ITicker, TickFails>(eager: true));
        Require(error.Message == "hook failure", "应保留 Tick 回调的原始异常");
        Require(TickFails.Disposed == 1 && !RevServices.Hub.HasTickable, "实例及 Tick 驱动都应回滚");
        Require(!RevServices.IsRegistered<ITicker>(), "失败登记应撤销");
    }

    private static void ReplacementIsNotRemoved()
    {
        Throws<InvalidOperationException>(() => RevServices.Register<IListener, ReplacesSelf>(eager: true));
        Require(RevServices.IsRegistered<IListener>() && RevServices.Count == 1, "异常收尾不能误删重入注册的新记录");
        Require(RevServices.GetRequired<IListener>() is EagerListener, "新登记应可正常取用");
    }

    private interface IListener { }
    private interface IOther { }
    private interface ITicker { }

    private static class Events
    {
        internal static Action Changed;
        internal static int Received;
        internal static void Raise() { Changed?.Invoke(); }
    }

    private sealed class EagerListener : IListener, IDisposable
    {
        internal static int Created;
        internal static int Disposed;
        public EagerListener() { Created++; Events.Changed += OnEvent; }
        private void OnEvent() { Events.Received++; }
        public void Dispose() { Disposed++; Events.Changed -= OnEvent; }
    }

    private sealed class NeverConstruct : IListener
    {
        public NeverConstruct() { throw new Exception("不该调用第二个构造函数"); }
    }

    private sealed class Other : IOther { }

    private sealed class ConstructorFails : IListener
    {
        public ConstructorFails() { throw new InvalidOperationException("ctor failure"); }
    }

    private sealed class InitFails : IListener, RevIServiceInit, IDisposable
    {
        internal static int Disposed;
        public void OnInit(RevIServiceLocator services) { throw new InvalidOperationException("init failure"); }
        public void Dispose() { Disposed++; throw new Exception("dispose failure"); }
    }

    private sealed class TickFails : ITicker, RevITickable, IDisposable
    {
        internal static int Disposed;
        public void OnTick(float deltaTime) { }
        public void Dispose() { Disposed++; }
    }

    private sealed class ReplacesSelf : IListener, RevIServiceInit, IDisposable
    {
        public void OnInit(RevIServiceLocator services)
        {
            RevServices.Unregister<IListener>();
            RevServices.Register<IListener, EagerListener>();
        }
        public void Dispose() { }
    }
}
