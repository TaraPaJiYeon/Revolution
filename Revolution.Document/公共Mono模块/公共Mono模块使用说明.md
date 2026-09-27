# 公共 Mono 模块 · 使用说明（手把手教程）

> 使用说明 · 从零到能写业务
>
> 这份文档只回答一个问题：**我该怎么用它？**（让纯 C# 类也能每帧跑、也能跑协程）
> 读完你能做到：3 分钟给纯 C# 管理器加上 Update · 分清三个相位（选错会抖/会飘）· 一行防泄漏 · 不生效时 3 分钟定位

## 目录

- 〇、它是干什么的
- 一、3 分钟跑起来（可粘贴）
- 二、"我要做 X" 对照表（全部 API）
- 三、三个相位怎么选（执行顺序）
- 四、别选错：和 RevTimer / RevTask 的分工
- 五、协程：纯 C# 类也能跑
- 六、一行防泄漏：owner 与作用域
- 七、去重与上限（为什么我的监听没加进去）
- 八、新手最容易踩的 6 个坑
- 九、不生效怎么查（3 分钟）
- 十、附：文件清单 / 验收 / 与旧 MonoMgr 对照

---


## 〇、它是干什么的

*先花 30 秒建立直觉，再动手写。*

> [!TIP]
> **只学 3 个东西就能干活（真的）**
>
> RevMono.AddUpdate(OnTick);                              // ① 让一个纯 C# 方法每帧被调
> RevMono.AddLateUpdate(FollowCam, owner: this);       // ② 每帧最后（相机/UI 跟随）
> RevMono.StartCoroutine(Run());                          // ③ 纯 C# 类也能跑协程
> 要停就 `RevMono.RemoveUpdate(OnTick)`；随对象销毁一行 `RevMono.RemoveAllOf(this)`。  
>   
>  剩下的（FixedUpdate、作用域、读数、事件……）都是**用到再看**的增值项。

> **人话** 公共 Mono 模块 = 借一个隐藏的 MonoBehaviour 给你的纯 C# 类用

| 它替你解决的问题 | 怎么做的 |
|---|---|
| 纯 C# 类没有 Update | `RevMono.AddUpdate` / `AddLateUpdate` / `AddFixedUpdate`（三个相位） |
| 纯 C# 类不能 StartCoroutine | `RevMono.StartCoroutine(IEnumerator)`（自动创建隐藏宿主去跑） |
| 同一个方法加两次 → 每帧跑两次 | **去重**：重复加返回 false，不会重复执行 |
| 一个监听者抛异常 → 后面全不跑 | **逐监听者隔离**：后面的照常，异常走 `RevMono.OnException`（默认进日志系统） |
| 忘记移除 → 永久泄漏 | `owner` 一行批量清理 / `OpenScope()` 出块全停（连协程一起停） |
| 协程抛异常静默中断 | 薄包装捕获 → 报到统一日志 + 结束该协程（其余不受影响） |

---


## 一、3 分钟跑起来（可粘贴）

*不用配置、不用继承 MonoBehaviour、不用摆物体。*

**典型场景：一个纯 C# 的心跳管理器**

*它没有 Update、也不能 StartCoroutine —— 这正是本模块存在的理由*

public sealed class
NetworkHeartbeat
// ← 注意：纯 C# 类，没有继承 MonoBehaviour
{
public void
Start()
    {
        RevMono.AddUpdate(Tick, owner:
this
);
// 每帧推进（owner 用于一行收尾）
RevMono.StartCoroutine(PingLoop());
// 跑协程（隐藏宿主替它跑）
}
public void
Stop() =
>
RevMono.RemoveAllOf(
this
);
// ★ 一行：摘监听（协程请配合作用域，见第五节）
private void
Tick()
    {
// 每帧要做的事（轻活；重活交给 RevTask / 线程）
}
private
IEnumerator PingLoop()
    {
while
(
true
)
        {
// 发心跳 ...
yield return new
WaitForSecondsRealtime(
5f
);
        }
    }
}
> 宿主 `[RevMono]` 是隐藏的（不进 Hierarchy、不随场景销毁），第一次用到时自动创建。


```csharp
// 用起来就一行：
var heartbeat = new NetworkHeartbeat();
heartbeat.Start();        // 之后每帧自动 Tick，心跳每 5 秒一次
```

---


## 二、"我要做 X" 对照表（全部 API）

*左边找需求，右边抄一行。**所有方法签名都写在一行里**，参数不用换行。*

| 我想… | 这么写 |
|---|---|
| 每帧回调 | `RevMono.AddUpdate(action, owner)` / `RevMono.RemoveUpdate(action)` |
| 每帧最后的回调（相机/UI 跟随） | `RevMono.AddLateUpdate(action, owner)` / `RemoveLateUpdate(action)` |
| 物理帧回调 | `RevMono.AddFixedUpdate(action, owner)` / `RemoveFixedUpdate(action)` |
| 按变量选相位 | `RevMono.Add(RevMonoPhase.Update, action, owner)` / `RevMono.Remove(phase, action)` |
| 跑一段协程 | `var co = RevMono.StartCoroutine(MyRoutine());` |
| 停一条 / 全停协程 | `RevMono.StopCoroutine(co)` / `RevMono.StopAllCoroutines()` |
| 随对象销毁清干净 | `RevMono.RemoveAllOf(this)` |
| 一块加、一块停（含协程） | `using (var s = RevMono.OpenScope()) { s.AddUpdate(OnTick); s.StartCoroutine(Run()); }` |
| 清空全部监听者 | `RevMono.Clear()` |
| 看数量 / 宿主好了没 | `RevMono.Count` · `RevMono.UpdateCount` · `RevMono.LateUpdateCount` · `RevMono.FixedUpdateCount` · `RevMono.IsRunning` |
| 知道出事了 | `RevMono.Failed += (reason, detail) => ...`（`Overflow` / `CallbackThrew` / `NotPlaying`） |
| 接管异常上报 | `RevMono.OnException = (e, msg) => 你的埋点.Post(e, msg);` |

> [!NOTE]
> **返回值是有意义的**
> AddXxx
> bool
> false = 没加进去

---


## 三、三个相位怎么选（执行顺序）

*它们就是 MonoBehaviour 的 Update / LateUpdate / FixedUpdate，语义完全一致。*

→ →

| 相位 | 适合什么 | 选错的典型症状 |
|---|---|---|
| `Update` | 常规每帧逻辑：输入采样、状态推进、UI 刷新（不依赖别人位置时） | —— |
| `LateUpdate` | **相机跟随**、UI 跟随角色、任何"必须在别人动完之后"才做的事 | 放 Update 里会**抖**（跟着上一帧的位置算，画面追不上） |
| `FixedUpdate` | 物理、帧率无关的推进（力、速度积分）、和帧同步逻辑对齐 | 放 Update 里会**飘**（帧率一变结果就变） |

> [!TIP]
> **不确定时怎么选**
> Update
> 只要涉及"跟随另一个物体的位置/旋转"，就用 `LateUpdate`
> FixedUpdate

---


## 四、别选错：和 RevTimer / RevTask 的分工

*框架里有三个"看起来都能做定时/异步"的东西，选错了要么写得更累，要么性能吃亏。*

| 设施 | 它负责的语义 | 什么时候用 |
|---|---|---|
| `RevTimer` | "**多久之后** / 每隔多久 / 到某个时刻" | 延迟关界面、每秒刷一次、活动结束倒计时（还带四时间域与句柄） |
| `RevTask` | "**await** 一帧 / 等资源加载完" | 异步流程编排、加载链、服务器请求回调 |
| **`RevPublicMono`** | "给我一个**每帧回调**" / "跑一段**协程**" | 需要持续每帧推进（跟随、插值、扫描）；或手上就是一段 yield 协程 |

**✗ ✗ 用循环计时器凑"每帧"**

```text
RevTimer.Every(0.016f, Tick);
// 间隔和帧长对不齐 → 有时一帧跑两次、有时一帧不跑
// 而且每帧都要过一次到期判定
```

**✓ ✓ 直接用每帧回调**

```text
RevMono.AddUpdate(Tick, owner: this);
// 一帧一次，语义就是"每帧"
```


> [!NOTE]
> **三者可以混用，各做各的**
> RevTimer.After
> RevMono.AddUpdate
> RevTask

---


## 五、协程：纯 C# 类也能跑

*协程真正的价值是"能 yield 一帧 / 等一秒 / 等条件"，写流程比状态机直观。*


```csharp
// 起：返回 Unity 的 Coroutine 句柄（可单独停）
Coroutine co = RevMono.StartCoroutine(OpenPanelStep());

private IEnumerator OpenPanelStep()
{
    panel.PlayOpen();
    yield return new WaitForSecondsRealtime(0.3f);   // 等真实时间（不受 timeScale 影响）
    while (!resourceReady) yield return null;            // 等条件（每帧检查）
    panel.ShowContent();
}

// 停：单条 / 全停
RevMono.StopCoroutine(co);
RevMono.StopAllCoroutines();
```

| 特性 | 说明 |
|---|---|
| 谁来跑 | 隐藏宿主 `[RevMono]`（`DontDestroyOnLoad`）—— 所以纯 C# 类也能用，且不随场景卸载而中断 |
| 返回值 | Unity 标准的 `Coroutine`：谁起谁存，要停就传它回去（没有字符串 id，也没有内部字典） |
| **异常隔离** | 协程里抛异常 → 走 `RevMono.OnException`（默认进框架日志系统，带原始堆栈）→ 结束这条协程；**其余协程与监听者不受影响** |
| 非运行态 | 编辑器里没 Play 时启动协程会失败并告警（不会偷偷建宿主） |

> [!WARNING]
> **协程 vs RevTask 怎么选**
> yield return
> await
> RevTask
> 别在同一段流程里两种混着写

---


## 六、一行防泄漏：owner 与作用域

*"忘记移除监听"是这类模块唯一的泄漏方式，两种方式根治。*

**方式一：owner（推荐给"跟随对象"的每帧逻辑）**

*管理器、面板、角色：只要它还活着，这些每帧逻辑才有意义*

RevMono.AddUpdate(Tick, owner:
this
);
RevMono.AddLateUpdate(FollowCam, owner:
this
);
public void
Dispose() =
>
RevMono.RemoveAllOf(
this
);
// ← 一行，全摘
> `owner` 是显式参数（`ReferenceEquals` 匹配）：闭包、静态方法、多个委托都不会漏。


**方式二：作用域（推荐给"一段流程/一个界面"）**

*打开一个面板、进入一段玩法：这段时间内的监听与协程都归它*

using
(
var
scope = RevMono.OpenScope())
{
    scope.AddUpdate(OnTick);
// 这块里加的监听归 scope 管
scope.StartCoroutine(RunSequence());
// 这块里起的协程也归 scope 管
}
// ← 出块：监听全摘、协程全停

> [!TIP]
> **为什么不用"记得手动 Remove"**
> 不会有任何报错

---


## 七、去重与上限（为什么我的监听没加进去）

*两个刻意的防线，都会通过返回值告诉你。*

| 情况 | 行为 | 怎么办 |
|---|---|---|
| 同一个委托加两次 | 返回 **false**，只生效一次 | 这是防"每帧跑两次"的陷阱（旧实现会静默跑两次） |
| 两个不同委托指向同一个方法 | 都加上（它们是两个委托实例） | 如果要防重，请缓存同一个委托变量再加 |
| 某个相位超过 256 个 | 返回 **false** + `Failed(Overflow)` + 日志告警 | 查是不是漏了 `RemoveAllOf`（`RevMono.Count` 看总量） |

> [!NOTE]
> **为什么要有上限**


### 7.1 派发期间增删的语义（可依赖）

| 你在回调里做的事 | 什么时候生效 |
|---|---|
| 新增一个监听者 | **下一帧**开始跑（本帧名单在派发前已经锁定） |
| 移除自己 / 移除别人 | 本帧仍会被调到（快照里还在），**下一帧**起不再跑 |

这个语义是刻意的：名单在本帧内不会变，所以回调里想做什么都不会"改坏"遍历（旧实现用 C# 事件，也无法保证这一点）。

---


## 八、新手最容易踩的 6 个坑

*每条都会让人白干一阵子。*

| # | 坑 | 正解 |
|---|---|---|
| 1 | 相机/UI 跟随用了 `AddUpdate` | 用 `AddLateUpdate`（否则会抖：跟着上一帧的位置算） |
| 2 | 物理积分用了 `AddUpdate` | 用 `AddFixedUpdate`（否则帧率一变结果就变） |
| 3 | 忘了移除 → 每帧还在跑、数量涨到上限 | `owner` + `RemoveAllOf`，或 `OpenScope()` |
| 4 | 在监听里做重活（大循环、同步加载） | 它会直接吃掉这一帧：重活交给 `RevTask` / 分帧做 |
| 5 | 以为监听者抛异常会被自动摘掉 | **不会**：异常被隔离并上报，但那个监听者每帧还会继续抛 —— 要停请自己 `Remove` |
| 6 | 在场景里手动挂了一个 `[RevMono]` 同名宿主 | 框架会自动关闭多余的驱动并告警（同帧跑两次会让所有监听者每帧执行两遍） |

---


## 九、不生效怎么查（3 分钟）

*按顺序排除，每步都能立刻验证。*

→ → →

| 现象 | 查这里 |
|---|---|
| 回调根本没被调 | `RevMono.UpdateCount` 是不是 0？（0 = 没加进去：重复或超上限） |
| 数量有，但没跑 | 相位对不对？`RevMono.IsRunning`（宿主是否就绪 —— 首次加监听会自动创建，所以正常为 true） |
| 只在编辑器里不跑 | 没进 Play：非运行状态不创建宿主（这是刻意的，避免把隐藏物体写进场景） |
| 协程启动了但没后续 | 协程里抛异常了 → 查 `RevMono.OnException`（默认打在框架日志系统里，tag = `Mono`） |
| 跑着跑着不跑了 | 有人调了 `RevMono.Clear()` / 作用域 `Dispose()` / `RemoveAllOf(owner)` |
| 每帧跑两次 | 同一个委托加了两次？（返回 false 就是没加）或场景里多挂了一个宿主（会有告警） |

> [!NOTE]
> **一条能救命的命令**
> RevLog.Info($"update={RevMono.UpdateCount} late={RevMono.LateUpdateCount} fixed={RevMono.FixedUpdateCount} running={RevMono.IsRunning}");

---


## 十、附：文件清单 / 验收 / 与旧 MonoMgr 对照

*想深入看代码或判断可信度时读这一节。*


### 10.1 文件都在哪（你只需要读一个）

| 文件 | 行数 | 说明 |
|---|---|---|
| `Facade\RevMono.cs` | 138 | ★ **唯一入口**：三相位 + 协程 + 清理 + 读数 + 事件 |
| `Core\RevMonoDefines.cs` | 74 | 三相位 / 失败原因码 / 上限 / 监听者结构（纯 C#） |
| `Implementation\RevMonoCore.cs` | 188 | 内核：去重、上限、快照派发、异常隔离（纯 C#，可工程外断言） |
| `Support\RevMonoDriver.cs` | 140 | 隐藏宿主：三相位派发 + 协程宿主（含异常隔离）（不用读） |
| `Support\RevMonoUnityHooks.cs` | 44 | 出口接管 + 进 Play 复位（不用读） |
| `Support\RevMonoScope.cs` | 77 | `using` 一块：出块摘监听、停协程 |


### 10.2 验收情况

| 项 | 结果 |
|---|---|
| 工程外行为断言（无 Unity，假宿主驱动） | **25 / 25 通过**：去重、相位隔离（Update/LateUpdate/FixedUpdate 互不干扰）、**异常隔离**（后面的监听者照跑 + 出错者不被自动摘掉）、派发期间增删的快照语义、owner 清理（跨相位）、上限拒绝、Clear |
| 派发开销 | 10 万次派发额外分配 **32 B**（快照脏了才重建 → 稳态零分配） |
| 编译 | `Revolution.Runtime` **0 错 0 警**；`Revolution.Editor` 0 错 |
| 依赖 | 无（内核纯 C#；只有宿主适配层用 UnityEngine）；异常与告警统一走 `RevLog`（tag = `Mono`） |


### 10.3 与早期实现 `MonoMgr` 的对照

| 旧实现（231 行） | 现在 |
|---|---|
| 三个相位监听 + 协程能力 ✓ | 保留（命名改为 `AddUpdate` / `AddLateUpdate` / `AddFixedUpdate`） |
| `SingletonAutoMono<MonoMgr>` 单例 + `MonoMgr.Instance.xxx` | 静态门面 `RevMono.xxx`（少一层单例、可测、不占单例名额） |
| **`updateEvent?.Invoke()` 一锅端**：一个监听者抛异常，它后面全部不跑（异常还会冒到 Unity 的 Update 里逐帧刷屏） | **逐监听者 try/catch 隔离**：后面的照常；异常走 `OnException`（默认进日志系统）+ `Failed(CallbackThrew)` |
| **`coroutineDict` 只增不减**（跑完 / Stop / StopAll 都不移除）→ 长期运行持续泄漏，重复 id 还会自动加后缀越滚越长 | **不建字典**：返回 Unity 的 `Coroutine` 句柄，谁起谁存（没有泄漏面） |
| `StartCoroutine(routine, string coroutineId)` 魔法字符串 | 去掉（要按语义管理就用句柄或作用域） |
| 同一个方法加两次 → 每帧跑两次，毫无提示 | **去重**（返回 false）+ 每相位上限 256（超了报 `Overflow`） |
| 没有异常出口、没有 owner 清理 | `Failed` / `OnException` / `owner` / `RemoveAllOf` / `OpenScope` |
| 协程抛异常静默中断（只报一次 Console） | 薄包装捕获 → 统一日志 + 结束该协程（其余协程与监听者不受影响） |
| 宿主 = 单例 MonoBehaviour（帧派发与协程耦合在一起） | 隐藏宿主 `[RevMono]`（`HideAndDontSave` + `DontDestroyOnLoad`）+ **重复驱动防御** |

> [!NOTE]
> **配套文档**
> Revolution.Document
> Assets/Revolution/Runtime/RevPublicMono/

---

Assets/Revolution/Runtime/RevPublicMono/ RevMono .cs
