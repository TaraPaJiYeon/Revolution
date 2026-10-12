---
name: revolution-framework
description: 本指南适用于 Revolution（RevolutionFramework / Rev 前缀）Unity 游戏框架的开发与代码审查。当任务涉及以下任何内容时使用：Revolution 框架、Assets/Revolution、RevInitialize（启动入口 / Boot）、RevLog / RevTimer / RevMono / RevPool（含 [RevPoolPrefab] 按类型取池）/ RevEvent / RevServiceLocator / RevTask / RevScene / RevInput / RevSound / RevStateMachine / RevUI / RevResManager / RevDataTableManager / RevGM / RevSequence / RevAppLifecycle（切后台 / 回前台）/ RevStripGuard（代码裁剪自检）等模块，资源加载与 AssetBundle（RevAB 打包工具、ResMap.txt、逻辑路径、引用计数、编辑器直读 vs AB 模式），UI 面板（RevUIPanel / RevUIPanel&lt;TData&gt; / [RevBind] / 控件事件方法特性 / RevUIAnim 动画 / 面板池 / Back 语义 / SetData 与 RefreshView 的刷新时机），事件与计时器清理（owner / scope），异步 RevTask（await / 取消 / 分帧），服务定位器装配，GM 指令注册，配置表（导表工具），安卓与微信/抖音小游戏/WebGL 适配、IL2CPP 代码裁剪与 link.xml，以及扩展包 RevHotUpdate 资源热更（清单 / 差量下载 / 版本目录 / 两个钩子 / 本地 CDN demo）。也适用于：按本框架规范生成或修改代码（Rev 前缀、方法括号不换行、方法必须带大括号、核心处写小白向注释）、排查本框架的运行时问题、工程外纯 C# 断言验证、以及 Revolution 项目的架构决策（要不要新增分层、用什么模块）。即使提问者没有明确说出“Revolution”或“框架”，只要代码里出现上述类型/目录，也应使用本 skill。
---

# Revolution 框架开发 Skill

Revolution 是一套**从零手写、可读可测、无魔法**的 Unity 游戏框架（C#）：**19 个功能模块 + 1 个启动门面（`RevInitialize`），共 20 个 Runtime 目录**（Runtime 约 186 个 `.cs` / 约 3 万行）
+ **Editor 工具**（约 44 个 `.cs`：RevAB 打包 / 导表 / GM 面板 / UI 面板代码生成 / 平台工具）+ **18 个可运行 Demo 场景**，
另外有一个**扩展包** `RevHotUpdate`（资源热更，独立程序集、按需安装，约 18 个 `.cs`）。

> 规模提示：这个体量**不需要通读源码**。模块用法读 `Facade/` 文件头（每个门面文件头就是迷你教程）+ 对应 `Demo` 场景脚本（能编译能运行，最可信）；
> 不要为了"了解框架"去读 `Core` / `Implementation`。本节往下的"模块总览"一张表就是全部地图。
> **数字会过时**：权威统计以仓库根 `README.md` 与 `Assets/Revolution/CHANGELOG.md` 为准，改了模块数/规模请顺手同步这里与 `Skills/README.md`。

它的三条设计取向直接决定你该怎么写代码：

1. **模块化、门面唯一**：每个模块只暴露一个门面（`RevTimer` / `RevResManager` / `RevUI` …），业务只碰门面，内核（`Core` / `Implementation`）不改也不读。
2. **失败必须可见**：不吞异常、不静默 return —— 失败一律给原因枚举 / 句柄字段 / 返回值，日志有唯一出口 `RevLog`。
3. **内核尽量不依赖引擎**：路径、句柄、槽位表、池引擎、日志/计时器内核是纯 C#，**能链接进普通 .NET 工程跑断言**（见 `references/verify-and-test.md`）。

## 模块总览（20 个 Runtime 目录，一张表找全）

> "我想做 X → 用哪个门面"。签名与最小示例在对应参考文件里；**没有合适模块就先用"服务 + 面板"实现，不要先动框架**。

| 我想… | 门面 | 一句话 | 速查位置 |
|---|---|---|---|
| 启动游戏、初始化框架 | `RevInitialize` | 启动入口调一次，装配资源系统策略 | `startup-and-platform.md` §1 |
| 切后台 / 回前台处理 | `RevAppLifecycle` | `Paused` / `Resumed` / `Quitting` 事件 + 后台时长 | `startup-and-platform.md` §2 |
| 确认代码裁剪没误伤 | `RevStripGuard` | 真机启动自检关键类型，缺了报人话 | `startup-and-platform.md` §3 |
| 打日志 | `RevLog` | 五级 + tag + 重复抑制 + 环形缓冲 | `modules-core.md` §1 |
| 多久之后 / 每隔多久 | `RevTimer` | 四时间域，owner/scope 防泄漏 | `modules-core.md` §2 |
| 纯 C# 类要每帧回调 / 协程 | `RevMono` | 不摆物体拿 Update/LateUpdate/FixedUpdate | `modules-core.md` §3 |
| 频繁创建销毁的对象 | `RevPool` / `RevRefPool` | GameObject 池（可 `[RevPoolPrefab]` 按类型取）/ 纯 C# 池 | `modules-core.md` §4 |
| 一对多解耦通知 | `RevEvent` | 字符串事件名，派发中可安全增删 | `modules-core.md` §5 |
| 跨界面共享的业务能力 | `RevServiceLocator` | 组合根显式装配，三档取服务 | `modules-core.md` §6 |
| await 一帧 / 等待 / 取消 | `RevTask` | 轻量单线程异步（框架唯一异步基建） | `modules-core.md` §7 |
| 切场景 | `RevScene` | 进度永远 0~1，切前按约定清理 | `modules-core.md` §8 |
| 键鼠 / 触屏 / 手柄 / 手势 | `RevInput` | 动作名绑定 + 屏蔽栈 | `modules-core.md` §9 |
| 播音效 / BGM | `RevSound` | 一行播放，2D/3D/BGM，音量总线 | `modules-content.md` §1 |
| 状态切换（流程 / AI） | `RevLightStateMachine` 等 | 轻量 / 栈 / 重量三形态 | `modules-content.md` §2 |
| 加载资源 | `RevResManager` | 逻辑路径 + 引用计数 + 分组卸载 | `modules-content.md` §3、`resource-and-ab.md` |
| 单例 | `RevSingleton*` | 三个基类（优先服务定位器） | `modules-content.md` §4 |
| 做界面 | `RevUI` / `RevUIPanel` | 声明式面板、数据单向流、动画库 | `modules-content.md` §5、`ui-and-layering.md` |
| 一串动作（播放→等待→并行） | `RevSequence` | 不可变蓝图 + 取消收尾契约 | `modules-content.md` §6 |
| 运行时调试指令 | `RevGM` | 一行注册，游戏内 / 编辑器共用 | `modules-content.md` §7 |
| 读配置表 | `RevDataTableManager` | 表也是资源，走资源系统 | `modules-content.md` §8 |
| 资源热更（扩展包） | `RevHotUpdate` | 清单 + 差量下载 + 版本目录 | `modules-content.md` §9、`hot-update.md` |

## 第一步：30 秒分诊——先判断"用户要的是哪一类事"，再只读对应那一个文件

> **读文件的原则（渐进式）**：先读本文件的总览表 + 铁律；**只读分诊命中的那一个文件**；不要为了"了解框架"通读 `references/` 或源码。
> 需要大范围搜索源码之前，先问自己：这个问题在下表的哪一行？

| 用户在说什么 | 类型 | **先读这个**（读完通常就够了） |
|---|---|---|
| "帮我写个 XX 功能 / 怎么实现 XX / 我想做 XX" | **写功能** | `references/playbooks.md`（18 个任务配方，自带选型 + 标准写法 + 坑 + 抄哪个 Demo） |
| "XX 和 YY 用哪个 / 该不该用事件 / 要不要新建 System" | **选型** | `references/decision-guide.md` §1 对照表 |
| "框架有没有 XX 能力 / 能不能 XX"（尤其是你不确定 API 存不存在时） | **查边界** | `references/decision-guide.md` §2「不存在 / 不做」清单（防幻觉） |
| "XX 不工作 / 出现 XX 现象 / 报错 XX / 编辑器好真机坏" | **排错** | `references/troubleshooting.md`（症状 → 原因 → 先查什么） |
| "帮我审查这段代码 / review" | **审查** | `REFERENCE_GUIDELINES.md`（四段式输出） |
| 要完整签名 / 全部重载 / 精确参数 | **查 API** | `references/modules-core.md` 或 `references/modules-content.md`（门面签名 + 铁律） |
| 启动入口 / 切后台 / 出包 / 小游戏 / 代码裁剪 | **平台与真机** | `references/startup-and-platform.md` + `templates/RevBoot.cs.txt` |
| 加载资源 / 打包 AB / ResMap / 编辑器直读差异 | **资源专题** | `references/resource-and-ab.md` |
| 面板传数据 / 刷新时机 / 事件接法 / 动画 / Part / Back | **UI 专题** | `references/ui-and-layering.md` |
| 接热更 / 排查热更 | **热更专题** | `references/hot-update.md` |
| 编辑器菜单（打包 / 导表 / GM 面板 / UI 生成器 / 平台工具） | **工具** | `references/editor-tools.md` |
| 代码 / 注释规范（**本项目硬规范**） | **规范** | `references/code-style.md` |
| 怎么证明"改对了" / 提交推送 | **验证交付** | `references/verify-and-test.md` |
| 权威文档 / 全局文档在哪 | **找文档** | `references/docs-map.md` |

## 不确定时怎么办：反幻觉协议（写代码前必过）

这个框架的 API 有些**看起来该有、其实没有**（例如 `RevEvent.AddOnce`、`RevHeavyFsmGroup`、`IRevHotSink`、`RevSound` 的"在播数量"查询）。写代码前按顺序做：

1. **用到的每个 `Rev*` API，都能在 `references/` 里找到签名或 Demo 原样代码**——找不到就**不要凭印象写**；
2. 找不到时，**精确 grep 对应的 `Facade/` 文件**（门面在 `Assets/Revolution/Runtime/<模块>/Facade/`，文件头注释就是迷你教程），确认存在再写；
3. 你想用的能力如果疑似"框架没做"，先查 `references/decision-guide.md` §2；确认不做就用文档给的替代做法，**不要自己发明一个同名 API**；
4. **文档与代码冲突时以代码为准**（已知的几处冲突列在 `decision-guide.md` §2 末尾）；
5. **写的是 Demo 里出现过的写法就直接抄**——Demo 能编译、能运行，是最可信的用法（`playbooks.md` 里标"Demo 原样"的片段都是真实存在的）；
6. **统计数字 / 版本号 / 模块数会过时**，以仓库根 `README.md` 与 `CHANGELOG.md` 为准。

## 技术铁律（写代码必须遵守）

1. **只用门面**：`RevInitialize` / `RevLog` / `RevTimer` / `RevMono` / `RevPool` / `RevEvent` / `RevTask` / `RevScene` / `RevInput` / `RevSound` / `RevUI` / `RevResManager` / `RevDataTableManager` / `RevGM` / `RevSequence` / `RevAppLifecycle` / `RevHotUpdate`。不要 `new` 内核对象、不要反射、不要绕过门面直接读文件。
2. **失败必带原因**：资源看 `handle.ErrorReason`，GM 看 `RevGMResult.Message`，热更看 `RevHotUpdateResult.Error` / `RevHotUpdate.LastError`；业务拒绝就抛 `RevGMUsageException("理由")` 这类"带人话"的异常，**不要静默 return**。
3. **清理纪律**：计时器 / 每帧回调 / 事件订阅，**能传 `owner: this` 就传**，销毁时一行 `RevTimer.CancelAllOf(this)` / `RevMono.RemoveAllOf(this)` / `RevEvent.RemoveAllByOwner(this)`；资源**成对** `Load` → `Release`。匿名 lambda 注册事件后**无法按委托移除**，这是泄漏的头号来源。
4. **资源只写逻辑路径**：用生成的常量（`RevResPath.*`，见 `Assets/Revolution/Generation/RevResPath.cs`）或"两段式" `Load(根目录, 资源名, 类型, 分组)`；**禁止**自己拼 `StreamingAssets` / `Application.dataPath` 路径读资源（缓存、引用计数、平台差异、热更都会失效）。
5. **UI = 一个面板类 + 职责钩子**：`[RevUIPanel]` 声明路径、`[RevBind]` 绑节点、`OnBindView` 只装配、`OnRefreshView` 只落屏、`OnClick` 只转发；数据**单向** `SetData → OnDataChanged → RefreshView`；**框架不提供 `System` / `BusinessLogic` 层**（结论见 `references/ui-and-layering.md`）——跨界面复用的能力交给**服务定位器**，只属于这一屏的流程留在面板里。
   **刷新时机三条（新手最易混）**：① 框架**不监听数据对象内容**——`OnRefreshView` 只在"打开 / 面板开着时 `SetData` / 手动 `RefreshView()`"这三个时机被调；② **原地改数据字段（`Data.Count++`）必须手动 `RefreshView()`**，换新对象走 `SetData` 才自动刷（`OnDataChanged` 里**不要**再手动 `RefreshView()`，会画两遍）；③ **`SetData` 写在业务侧"数据产生的地方"**（点击回调 / 网络回包），不写在面板里；面板是数据的消费方。详见 `references/ui-and-layering.md` §1.1。
6. **异步只用 `RevTask`**：`await RevTask.Delay/Yield`、`await RevScene.LoadAsync(...)`、`await RevHotUpdate.InitializeAsync(...)`；**不要**引入 `System.Threading.Tasks.Task`、不要用协程写业务（`RevMono.StartCoroutine` 只用于桥接旧代码）。取消用 `RevCancellationTokenSource` + `token.ThrowIfCancelled()`。
7. **事件**：`string` 事件名（常量集中在一个类里）+ **同一事件名只能有一种签名**；只在主线程注册/派发；派发过程中可以安全增删（框架做了标记删除），但回调里别做重活。
8. **时间**：`RevTimer.After/Every` 必须能停（`owner` 或 `RevTimer.OpenScope()`）；UI 倒计时 / 超时用 `RevTimeDomain.Unscaled`（`Scaled` 会被 `timeScale` 冻结）；`At(...)` 前必须 `SyncServerTime`。
9. **服务**：装配只写在一处（组合根 `RevServiceLocator.Create()...Build()`），装配后不可改；必需依赖用 `GetRequired<T>()`（漏注册启动期就炸），可选能力用 `Get<T>()`/`TryGet<T>()`；`AddScoped` 的服务**必须**放在 `CreateScope()` 的 `using` 里取。
10. **编辑器 vs 真机**：编辑器默认**只**注册 `RevEditorResPolicy`（AssetDatabase 直读，AB/Resources 完全不参与）；要看 AB / 热更效果，必须开 `RevResBootstrap.UseABInEditor = true`（菜单 `Revolution.Tools/资源/AB 加载模式（编辑器）`）。另外**编辑器里平台名恒为 `PC`**（`RevHotPlatform` 带 `!UNITY_EDITOR`）。
11. **不新增分层、不新增门面**：不要给框架加 `System` / `BusinessLogic` 这样的层，也不要给某个模块加"第二套加载 API"（会立刻长成第二套系统）。真需要扩展时，优先**服务 + 约定**，其次才是框架改动。
12. **改框架要照既有形状**：目录分层 `Core`（数据与契约）/ `Facade`（对外入口）/ `Implementation`（实现）/ `Interfaces`（接口）/ `Support`（宿主适配）；每个文件**开头写清"为什么这么写"**；新增资产要带 `.meta`（文件夹 meta 一律是**同级** `<文件夹名>.meta`）。
13. **启动只调一次 `RevInitialize.Initialize()`**：它只装配资源系统策略；**不要**再直接调 `RevResBootstrap.Instance.Init()`，也不要去"手动初始化"其它模块（`RevEvent` / `RevInput` / `RevTimer` / `RevUI` / `RevPool` / `RevAppLifecycle` 都已自动就位，重复启动反而会清监听）。骨架见 `templates/RevBoot.cs.txt`。
14. **切后台 / 回前台用 `RevAppLifecycle` 事件，不要自己写 `OnApplicationPause` / `OnApplicationFocus`**：框架已对"Pause 与 Focus 同时到达"去重；`Quitting` 移动端不保证触发，数据要随存随写。
15. **面向真机 / 小游戏 / WebGL 写代码**：不引入 `Thread` / `Task`（小游戏单线程）、不依赖同步加载（用 `LoadAsync` / `GetAsync` / `OpenAsync`）、不依赖文件日志；**主推关闭 Managed Stripping**（开了就生成 `link.xml` 并看 `RevStripGuard` 自检）。清单见 `references/startup-and-platform.md` §4–5。
16. **对象池按类型取优先**：预制体脚本上写 `[RevPoolPrefab(root, name, Group = ...)]`，业务 `RevPool.Get<T>()` / `GetAsync<T>(cb)` / `Clear<T>()` / `DestroyPool<T>()`，取用与清理处都不写路径字符串（特性**不继承**，子类要自己写；同一预制体只能被一个类声明）。

## 代码与注释规范（本项目硬规范，必须遵守）

1. **方法签名的 `(` 与参数不换行**：默认写一行；**只有太长（显示宽度 > 约 160 列）才换行**。长消息/长参数先提成局部变量，让"调用/抛出"本身保持单行。
2. **方法体必须带大括号**，哪怕只有一行（`if (x) return;` ✗ → `if (x) { return; }` ✓）。
3. **核心 / 关键 / 容易踩坑或被忽略的地方要写详细注释，面向小白**：解释"为什么这么做 / 不这么做会怎样"，而不是复述代码。
4. **类名一律 `Rev` 前缀**，文件名 = 类名；命名空间 `Revolution`（模块内部）或 `Revolution.Demo.<模块>`（示例）。
5. 中文注释、术语与文档保持一致（"门面 / 句柄 / 分组 / 逻辑路径 / 大版本锚定"…）。
6. 详细正反例与自检脚本思路见 `references/code-style.md`。

## 开发行为准则

以下四条偏向"稳"而不是"快"。琐碎任务自行判断。

### 1. 先想清楚再写

不要假设、不要藏疑惑、把取舍摆出来。动手前先明确：

- **模块选型**：这是"多久之后"（`RevTimer`）、"每帧"（`RevMono`）、"等一个异步结果"（`RevTask`）、还是"一次变化多处反应"（`RevEvent`）？
- **生命周期归属**：这段逻辑"关掉这个界面之后还该活着吗"？该 → 服务；不该 → 面板里。
- **失败出口**：失败时业务要拿到什么？（原因枚举 / 回调 / 异常 / 重试）
- **编辑器与真机差异**：这次改动在"编辑器直读"和"AB 模式"下都成立吗？

### 2. 最简优先

写解决问题的最少代码，不做没人要的抽象/配置/"以后可能用得上"。判断法：**加第四个监听者时，广播方要不要改代码？** 要改就别上事件。

### 3. 外科手术式改动

只动该动的：不顺手"美化"相邻代码、不重构没坏的、不删既有死代码（发现了**报告**即可）；匹配既有风格；自己改动造成的孤立引用要清掉。**改完要同步文档**（见下）。

### 4. 目标驱动、可验证

把任务变成可验证目标，别停在"能跑"：

```text
1. 读该模块《使用说明》→ 验证：写法与文档一致（门面、参数、清理方式）
2. 写代码 → 验证：Unity 编译 0 error（必要时用工程外桩工程先编译一遍纯 C# 部分）
3. 跑对应 demo 场景 → 验证：屏幕上"日志/结果"符合预期
4. 要动框架内核 → 验证：工程外纯 C# 断言全过（见 references/verify-and-test.md）
5. 交付 → 验证：文档 / demo / GM 命令 / change 说明同步
```

## 典型工作流

### A. 写新功能（7 步）

1. **定位模块 / 配方**：先在 `references/playbooks.md` 找对应任务（R1~R18）；没有就用上面的"模块总览"表；仍没有合适模块就先用"服务 + 面板"实现，**不要**先动框架。选型拿不准读 `references/decision-guide.md` §1。
2. **需要完整细节时读该模块的《使用说明》**（`references/docs-map.md` 给路径）——它是权威，本 skill 只是索引。
3. **找最近的 demo**（`Assets/Revolution.Demo/<模块>.Demo/`，18 个可运行场景；`playbooks.md` 每节都写了"抄哪个 Demo"）照着写。
   **要动框架本身（修 bug / 加功能）时，先查 `Assets/Revolution/CHANGELOG.md` 与 `Revolution.Document/bug修复日志/`**：这个模块以前修过什么、现行契约是什么——避免重复踩坑，也避免给"已修复的缺陷"写绕过代码（见 `references/pitfalls.md` 末尾"现行契约速查"）。
4. 写代码，守住上文的铁律与规范。
5. **自我审查**：过一遍 `REFERENCE_GUIDELINES.md` 的清单。
6. **验证**：编译 + demo 场景 / GM 面板 / 工程外断言。
7. **同步文档**：改了行为 → 改该模块《使用说明》；改了设计 → 改《架构解析》；同时更新 md 与 html（`Revolution.Document`，在线站会自动发布）。

### B. 审查代码

读 `REFERENCE_GUIDELINES.md`，按"严重问题 → 规范问题 → 潜在风险 → 优化建议"输出；重点看：owner/scope 清理、成对 Release、事件签名与重复订阅、时间域、编辑器/真机差异、异常是否被吞。

### C. 排错（先查表，再走 30 秒顺序）

**先在 `references/troubleshooting.md` §1 按现象找行**（按 UI / 输入 / 计时器 / 资源 / 对象池 / 场景 / 音效 / 状态机 / 序列 / 热更分组，60+ 条）；表里标"（已修）"的现象，在最新代码上仍出现 → 多半是**业务侧绕过了契约**，不是框架 bug。找不到对应行再走：

1. `RevLog` 里的 tag 过滤（模块都有自己的 tag：`Scene` / `ActionSequence` / `HotUpdate`…）。
2. 句柄原因：`handle.ErrorReason`（资源）、`RevGMResult.Message`（GM）、`RevHotUpdate.LastError` + `RevHotUpdate.Dump()`（热更）。
3. 能力自检接口：`RevTimer.Count` / `RevMono.Count` / `RevEvent.GetListenerCount(名字)` / `RevResManager.CachedCount` / `RevPool.DumpStats()` / `RevUI.DumpStats()` / `RevLog.Dump()` / `RevAppLifecycle.PauseCount`（老掉线先看它）/ `RevStripGuard.Dump()`（真机"静默失效"先看它）。
4. 复现：打开对应 demo 场景点按钮；或写一条 GM 命令直接在编辑器面板里跑。

### D. 交付

- 代码 + **文档（md & html）** + 必要时 demo/GM 命令；改了 `CHANGELOG.md` 的"未发布"段（新增 / 修复 / 变更）；
- 里程碑提交按仓库既有风格写提交信息（**统一中文**、说清"为什么"与验证数据）；
- ★ **Windows PowerShell 提交中文信息的坑**：`git commit -m "中文…"`（尤其多行）会被本地编码搞成乱码或直接解析失败。**正确做法**：把信息写进 UTF-8 文本文件，用 `git commit -F 文件` 提交，**提交后删掉该文件**（先 `git add` 再建临时文件，或把它放进 `.gitignore`，否则会被 `git add -A` 误带进提交）；
- 安装/分发走**四条**生成分支（改 `main`，不要往生成分支上提交）：`package`（框架本体）、`demo`（示例工程）、`hotupdate`（热更扩展包）、`skill`（本 skill）。

## 参考文件导航

"何时读哪个文件"已在上面的**分诊表**里给全了，这里只列清单（避免重复）：

- **按任务找**（优先）：`references/playbooks.md`（18 个配方）· `references/decision-guide.md`（选型 + 不存在清单）· `references/troubleshooting.md`（症状速查）
- **查 API 与专题**：`references/modules-core.md` · `references/modules-content.md` · `references/startup-and-platform.md` · `references/resource-and-ab.md` · `references/ui-and-layering.md` · `references/hot-update.md`
- **规范与交付**：`references/code-style.md` · `references/editor-tools.md` · `references/verify-and-test.md` · `references/docs-map.md` · `references/pitfalls.md`（坑位 + 已修复缺陷的现行契约）
- **审查**：`REFERENCE_GUIDELINES.md`
- **骨架模板**（`templates/`）：`RevBoot.cs.txt`（启动入口）· `RevXxxPanel.cs.txt`（UI 面板）· `RevXxxService.cs.txt`（业务服务）· `RevXxxDemo.cs.txt`（Demo 场景脚本）· `offline-harness/`（工程外断言工程）
