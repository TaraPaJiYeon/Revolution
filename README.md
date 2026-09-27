# Revolution

**一套从零手写的 Unity 游戏框架（C#）** —— 可读、可测、无魔法

[![Unity](https://img.shields.io/badge/Unity-2022.3.15f1c1-blue.svg?style=flat-square)](https://unity.com/)
[![License](https://img.shields.io/github/license/Yokino337088/Revolution?style=flat-square)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/Yokino337088/Revolution?style=flat-square)](https://github.com/Yokino337088/Revolution)
[![Issues](https://img.shields.io/github/issues/Yokino337088/Revolution?style=flat-square)](https://github.com/Yokino337088/Revolution/issues)
[![Top Language](https://img.shields.io/github/languages/top/Yokino337088/Revolution?style=flat-square)](https://github.com/Yokino337088/Revolution)
[![Runtime](https://img.shields.io/badge/Runtime%20模块-15%20个%20·%202.1%20万行-brightgreen?style=flat-square)](#-核心模块)

---

## 📖 简介

**Revolution** 是一套**从零手写**的 Unity 游戏框架：**15 个运行期模块 + 2 套编辑器工具**（Runtime 147 个 `.cs` / 约 2.1 万行），
覆盖资源加载 / UI / 动作序列 / 状态机 / 音效 / 计时器 / 日志 / 事件 / GM 指令 / 导表 等常规需求。

它跟大多数框架最大的不同是**"内核尽量不依赖引擎"**：路径、句柄、槽位表、对象池引擎、计时器内核这些纯 C# 部分
**可以链接进普通 .NET 工程直接跑断言**（本仓库已有 **145 条行为断言**在工程外跑通，不打开 Unity；见 [工程外验证](#-工程外验证纯-c-跑断言)）。
所以这里没有"猜它能不能工作"，只有"跑给看"。

### ✨ 核心特性

- 🧩 **模块化** - 每个模块一个门面（`RevSound` / `RevTimer` / `RevLog` …），可单独拿走、可整块删除
- 🚀 **一行就能用** - 播放音效、延迟 2 秒、每帧回调、分级日志都是**一行调用**，零配置（不摆物体、不挂脚本）
- 🧪 **可脱离 Unity 验证** - 内核是纯 C#，`dotnet run` 就能跑断言（误差契约 / 代际失效 / 异常隔离都钉死过）
- 🛡 **防漏防崩** - 句柄代际校验（过期句柄不会误伤别人）、循环计时器 `owner` / 作用域一行清理、逐回调异常隔离
- 📉 **性能有数字** - 1000 个计时器每帧 0.0139ms；10 万条日志 0 分配；对象池 10 万次创建 <4KB
- 🔍 **失败必带原因** - 框架不吞错误：失败一律给**原因枚举**（外加统一日志系统，可静默可按模块关）
- 📦 **编辑器工具齐** - LiteAB 打包窗口（分包浏览自动同步 / Project 窗口包名角标 / 体积依赖漏标检查 / 布局快照对比）+ 导表工具
- 🧭 **小白友好** - 每个模块一份《使用说明》（手把手）＋ 每个文件开头都写清"为什么这么写"

---

## 📚 目录

- [🚀 快速开始](#-快速开始)
- [🧩 核心模块](#-核心模块)
- [📁 项目结构](#-项目结构)
- [💻 系统要求](#-系统要求)
- [📚 文档导航](#-文档导航)
- [🧪 工程外验证（纯 C# 跑断言）](#-工程外验证纯-c-跑断言)
- [🎯 设计取向](#-设计取向)
- [💡 为什么用 Revolution？](#-为什么用-revolution)
- [🎮 示例项目](#-示例项目)
- [🤝 贡献与支持](#-贡献与支持)

---

## 🚀 快速开始

### 环境要求

- **Unity 版本**：2022.3.15f1c1（其它 2022.3.x 亦可）
- **依赖**：无第三方依赖，只用 Unity 官方包（见 `RevolutionFrameWork_Unity/Packages/manifest.json`）
- **平台**：Windows / macOS（编辑器）· Android / iOS / WebGL（运行期无平台相关代码）

### 装到你自己工程（三选一）

框架本体 = `Assets/Revolution/`。**在你的工程根目录**执行：

| 方式 | 命令 | 落在哪 |
|---|---|---|
| ① **推荐** | `git clone -b package --depth 1 https://github.com/Yokino337088/Revolution.git Assets/Revolution` | `Assets/Revolution` |
| ② 子模块 | `git submodule add -b package https://github.com/Yokino337088/Revolution.git Assets/Revolution` | `Assets/Revolution` |
| ③ Package Manager | Add package from git URL → `https://github.com/Yokino337088/Revolution.git?path=/RevolutionFrameWork_Unity/Assets/Revolution` | `Packages/` |

> **为什么推荐 ①/②**：Unity 的 `Resources.Load` 只保证加载**工程 `Assets` 下**的 `Resources` ——
> 框架自带 `ResMap.txt` 与两个 UI 预制体；另外两个**生成物**（`RevResPath.cs`、`RevSoundPath.cs`）必须写在框架目录里（包缓存只读）。
> 装在 `Packages/` 里也能跑（Canvas 用代码建、缺 ResMap 时编辑器直读），打包工具会**跳过生成并提示**。
>
> 更新：① `git -C Assets/Revolution pull`　② `git submodule update --remote Assets/Revolution`　③ Package Manager 选中包点 Update。
> `package` 分支由 GitHub Actions 自动同步（只同步 `Assets/Revolution/`）。

### 装完三步

1. 工程里准备**资源根目录**：新建 `Assets/GameRes`（或你已有的目录）
2. 菜单 `Revolution.Tools / 资源 / LiteAB 打包工具` → **① 打包配置** → 把「资源根目录」设为 `Assets/GameRes`
3. 开始写业务 👇

```csharp
RevSound.Play("ui_click");                                  // 播放音效（一行）
RevUI.Open<LoginPanel>();                                   // 打开面板
RevTimer.After(2f, () => RevUI.Close<LoadingPanel>());       // 2 秒后关掉
RevLog.Info("登录成功", "Login");                            // 打日志（分级 + 模块标签）
RevMono.AddUpdate(OnTick, owner: this);                     // 让纯 C# 类每帧跑一次
```

### 直接跑示例（克隆整个仓库）

```bash
git clone https://github.com/Yokino337088/Revolution.git
```

用 Unity 2022.3.15f1c1 打开 `RevolutionFrameWork_Unity/`（首次导入几分钟），Play `Assets/Scenes/SampleScene.unity`；
示例代码在 `Assets/Revolution.Demo/`（动作序列 / GM 指令 / 状态机）。

---

## 🧩 核心模块

> 完整清单见 [模块一览](#模块一览)；每个模块的目录里都有一份"3 分钟上手"的 `README.md`。

### 📦 资源加载（RevResourceSystem）

- ✅ **两条后端**：编辑器直读（不打包直接跑）＋ AssetBundle（`ResMap.txt` 逻辑路径 → 真实路径）
- ✅ **只走异步**：不为"看起来同步"的 API 付代价，不会出现"同步命中正在加载的句柄 → 拿到空内容"
- ✅ **引用计数 + 自动卸载**，失败带**原因枚举**（`handle.ErrorReason`）

### 🎨 UI 系统（RevUISystem）

- ✅ **声明式配置**：`[RevUIPanel(root, layer)]` 一个特性搞定层级与根节点
- ✅ **与对象池联动**：关闭即回收，重复打开不重建
- ✅ **纯代码路径可用**：没有 `Resources` 时 Canvas 用代码建（降级不崩）

### 🎬 动作序列（RevActionSequence）

- ✅ **链式 DSL** 表达"播放 → 等待 → 并行 → 嵌套"：

  ```csharp
  RevSequenceDefinition openChest = RevSequence.Create("开宝箱")
      .Do("播放开箱动画", ctx => Anim.Play("open"))
      .Wait(0.5f)
      .Parallel("一起飞", p => p.Do("飘字", ctx => Tip.Show("+10 钻石")))
      .OnCompleted(run => RefreshBag())
      .Build();                       // 建议加载期构建一次并缓存，运行期播放零构建成本
  ```
- ✅ **取消收尾契约**：中途取消也保证收尾（不会留下半截动画），`OnCancel` / `OnCompleted` / `OnCancelled` 三个回调齐
- ✅ **步骤库可扩展**：`Implementation/Steps/` 里 6 个现成步骤（等待 / 委托 / 并发 / 嵌套 / 发布 / 等待任务）

### 🎮 状态机（RevStateMachine）

- ✅ **两种形态**：轻量流程（GameFlow，进登录/进战斗/回大厅）＋ 重量级 AI（分层状态机）
- ✅ 状态切换有明确的生命周期回调与日志出口

### 🔊 音效系统（RevSoundSystem）

- ✅ **一行播放**：`RevSound.Play("ui_click")`（2D）/ `RevSound.PlayAt("爆炸", pos)`（3D）
- ✅ **BGM 与音量总线**：BGM 淡入淡出、总音量/分组音量
- ✅ **作用域**：`using (var s = RevSound.OpenScope()) { ... }` 出块全停
- ✅ 音效表 + 路径常量由工具生成（写错编译不过）

### ⏱ 计时器（RevTimer）

- ✅ **一行创建**：`RevTimer.After(2f, cb)` / `Every(1f, cb, times: 10)` / `NextFrame(cb)` / `At(服务器时刻, cb)`
- ✅ **四种时间域**：受 timeScale 影响 / 不受影响 / 逻辑帧 / **服务器绝对时刻**（切后台、改设备时间都不怕）
- ✅ **句柄可读**：`h.Left`（剩余秒）/ `h.Progress`（0~1）直接喂倒计时 UI；过期句柄所有操作都是安全空操作
- ✅ **一行防泄漏**：`owner: this` + `RevTimer.CancelAllOf(this)`，或 `using (RevTimer.OpenScope())`
- ✅ 数值实测：**1000 个计时器每帧 0.0139ms**；1 秒循环跑 10 分钟 = 恰好 600 次（无漂移）

### 📝 日志（RevLog）

- ✅ **级别就是成本契约**：`RevLog.Debug` 正式包**编译期删除**（连字符串拼接都不发生）
- ✅ **模块标签 + 静音**：`RevLog.Warn("msg", "Network")` / `RevLog.MuteTag("Network")`
- ✅ **刷屏抑制**：连续相同的日志只留首条 + 一条"重复 N 次"汇总（10 万条 0 分配）
- ✅ **出事前的现场**：环形缓冲常驻最近 2048 条，`RevLog.Dump(200)` 直接复制
- ✅ **异步落盘 + 上报可插拔**：`EnableFileLog(目录)` / `OnReport += ...`（全框架出口已统一到这里）

### 🧵 公共 Mono 模块（RevPublicMono）

- ✅ **让纯 C# 类也能每帧跑**：`RevMono.AddUpdate / AddLateUpdate / AddFixedUpdate`
- ✅ **协程宿主**：`RevMono.StartCoroutine(MyRoutine())`（含异常隔离，不再静默中断）
- ✅ 去重 / 上限 / owner 与作用域清理（避免"忘记移除 → 每帧还在跑"）

### 📣 事件系统（RevEventSystem）

- ✅ 强类型事件 + **订阅句柄**（取消订阅不用记委托）
- ✅ 派发期间增删订阅安全的语义（写在文档里，可依赖）

### 🕹 GM 指令（RevGMCommand）

- ✅ **一行注册**：给方法加个特性就行 —— `[RevGMEntry("给道具：give 1001 5")] static void Give(int id, int count)`
- ✅ 游戏内控制台 + 编辑器面板共用同一套指令

### 📊 配置表（DataLoad）

- ✅ **表 = 资源**：走资源系统加载，和 AB 体系天然一致
- ✅ 按类型取表，表结构由导表工具生成

### 🧭 服务定位器（RevServiceLocator）

- ✅ **把业务依赖挡在框架之外**：`RevServiceLocator.Create().AddSingleton<IAudioService, AudioService>().Build()`
- ✅ **三档取服务**：`GetRequired<T>()`（"必须有"，忘了注册启动期就炸出来）/ `Get<T>()`（可选能力，没有返回 null）/ `TryGet<T>()`（用在 `if` 里最顺）
- ✅ 生命周期可控（`RevIServiceInit` / `RevITickable`）

### 📦 对象池（RevObjectPool）

- ✅ GameObject 池 + **纯 C# 对象池**（`RevPoolCore<T>`）
- ✅ 与资源加载打通（池端着资源引用，不会被卸载掉）

### ⚡ 异步任务（RevTask）

- ✅ **`await` 一帧 / 等 N 毫秒 / 等一组**：`await RevTask.Yield();` · `await RevTask.Delay(500);` · `await RevTask.WhenAll(a, b);`
- ✅ **等资源加载完成**：`await asyncOperation;`（`AsyncOperation` 扩展方法）
- ✅ 不依赖 UniTask，零第三方依赖

### 🧱 单例基类（RevSingleton）

- ✅ 纯 C#（`RevSingleton<T>`）/ 自己摆（`RevSingletonMono<T>`）/ 自动创建（`RevSingletonAutoMono<T>`）
- ✅ 重复实例告警、销毁自动清引用、子类无法覆盖 `Awake` 漏掉赋值

### 模块一览

**运行期**（`Assets/Revolution/Runtime/`，15 个模块 / 147 个 `.cs` / 21,169 行）

| 模块 | 一句话 | 规模 |
|---|---|---|
| `RevResourceSystem` | 资源加载：编辑器直读 + AB 两条后端、引用计数、自动卸载、失败原因可查 | 17 / 2,646 |
| `RevUISystem` | UI：面板声明式配置、层级、与资源池联动 | 17 / 3,457 |
| `RevActionSequence` | 动作序列：一行 DSL 表达"播放 → 等待 → 并行 → 嵌套"，含取消收尾契约 | 22 / 2,234 |
| `RevStateMachine` | 状态机：轻量流程 / 重量级 AI 两种形态 | 10 / 1,577 |
| `RevSoundSystem` | 音效：一行播放、BGM、音量总线、作用域、音效表 | 10 / 1,651 |
| `RevTimer` | 计时器：四时间域、句柄代际、作用域、秒表 | 10 / 1,414 |
| `RevLog` | 日志：分级（Debug 编译期零成本）、tag 静默、重复抑制、环形缓冲、异步落盘、上报 | 8 / 1,053 |
| `RevPublicMono` | 公共 Mono：给纯 C# 类每帧回调与协程宿主 | 6 / 662 |
| `RevEventSystem` | 事件：强类型事件 + 订阅句柄 | 5 / 1,093 |
| `RevGMCommand` | GM 指令：一行注册，游戏内控制台 | 11 / 1,023 |
| `RevObjectPool` | 对象池：GameObject 池 + 纯 C# 对象池 | 10 / 2,037 |
| `DataLoad` | 配置表：表 = 资源，按类型取表 | 6 / 714 |
| `RevServiceLocator` | 服务定位器：把业务依赖挡在框架之外 | 8 / 702 |
| `RevTask` | 异步：`await` 一帧 / 等资源加载完成 | 4 / 639 |
| `RevSingleton` | 单例基类三件套（尽量少用） | 3 / 267 |

**编辑器**（`Assets/Revolution/Editor/`，2 套工具 / 22 个 `.cs` / 5,589 行）

| 工具 | 一句话 | 规模 |
|---|---|---|
| `RevResourceSystem` | **LiteAB 打包工具**：分包浏览（自动同步）/ Project 窗口包名角标 / 依赖 / 体积 / 漏标检查 / 布局快照对比；生成 `ResMap.txt` 与路径常量 | 20 / 4,769 |
| `RevGMCommand` | GM 指令的编辑器面板 | 2 / 820 |

---

## 📁 项目结构

```text
Revolution/
├── RevolutionFrameWork_Unity/            Unity 工程
│   ├── Assets/Revolution/                  ★ 框架本体（包分支的仓库根就是它）
│   │   ├── Runtime/                            15 个运行期模块（RevResourceSystem / RevUISystem / …）
│   │   ├── Editor/                             编辑器工具（LiteAB 打包 / GM 面板）
│   │   ├── Generation/                         生成的路径常量（RevResPath.cs）
│   │   ├── Resources/                          框架自带运行时资源（ResMap、UI 预制体）
│   │   ├── package.json / README.md            包描述与包内说明（UPM 用）
│   ├── Assets/Revolution.Demo/             示例（动作序列 / GM 指令 / 状态机）
│   ├── Assets/Scenes/                      示例场景（SampleScene）
│   ├── Assets/GameRes/                     资源根目录（放你的资源，LiteAB 从这里扫）
│   └── Packages/ · ProjectSettings/        Unity 工程配置
├── Revolution.Document/                  设计文档（每个模块：使用说明 + 使用指南）
├── Revolution.Demo/                      示例数据与源码
├── Revolution.ExcelTool/                 导表工具（WPF：Excel → C# 类 + 数据文件）
└── .github/workflows/                    CI（自动同步 package 分支）
```

> 仓库只收录**必要的代码与文档**：`Library/` `Temp/` `Logs/`、构建产物、IDE 工程文件（`*.csproj` `*.sln`）、
> 本机打包配置都不入库 —— 规则见 [`.gitignore`](.gitignore)。

---

## 💻 系统要求

- **Unity**：2022.3.15f1c1（推荐）· 2022.3.x 系列可用
- **平台支持**：Windows · macOS · Android · iOS · WebGL
- **开发环境**：.NET Standard 2.1（Unity 内置）· Visual Studio 2022 / Rider / VS Code
- **第三方依赖**：**无**（导表工具是独立 WPF 工程，可选）
- **内核验证环境**（可选）：.NET 8 SDK —— 用来跑纯 C# 断言，不装也能正常用框架

---

## 📚 文档导航

> 👉 **总入口（推荐从这里进）**：[`Revolution.Document/index.html`](Revolution.Document/index.html) ——
> 一页看全 13 个模块 28 份文档，带分组、搜索与阅读顺序建议。

### 各模块使用说明（手把手）

| 模块 | 文档 |
|---|---|
| 📖 日志系统 | [`Revolution.Document/日志系统/日志系统使用说明.html`](Revolution.Document) |
| ⏱ 计时器系统 | [`Revolution.Document/计时器系统/计时器系统使用说明.html`](Revolution.Document) |
| 🧵 公共 Mono 模块 | [`Revolution.Document/公共Mono模块/公共Mono模块使用说明.html`](Revolution.Document) |
| 📦 资源加载系统 | [`Revolution.Document/资源加载系统/`](Revolution.Document) |
| 🎨 UI 系统 / 事件系统 / 对象池 | [`Revolution.Document/`](Revolution.Document) 下的同名目录 |
| 🎬 动作序列 / 状态机 / 服务定位器 | 同上 |
| 🔊 音效系统 / 🕹 GM 指令 / 📊 导表工具 | 同上 |

### 代码里的"3 分钟上手"

`Assets/Revolution/Runtime/` 下这些模块自带 `README.md`：
`RevSoundSystem` · `RevActionSequence` · `RevGMCommand` · `RevTimer` · `RevLog` · `RevPublicMono`

---

## 🧪 工程外验证（纯 C# 跑断言）

框架里有一批**不引用 `UnityEngine`** 的文件，可以把它们链接进普通 .NET 控制台工程直接跑断言：

```text
RevResourceSystem/Core/ResPathUtil.cs            路径拼接 + 缓存键（含"两段键 == 完整路径键"不变量）
RevTimer/Core/RevTimerTable.cs                   计时器槽位表（代际号 / 延迟复用 / 句柄校验）
RevTimer/Core/RevServerClock.cs                  服务器时间（一次校准 + 本地 realtime 外推）
RevSoundSystem/Core/RevSoundVoiceTable.cs        声音槽位表（代际号 / 同帧去重 / 上限淘汰）
RevObjectPool/Core/RevPoolCore.cs                池引擎（重复归还拦截）
RevLog/Core/RevLogRing.cs                        日志环形缓冲（定长 / 零分配写入）
RevLog/Implementation/RevLogCore.cs              日志内核（过滤 / 重复抑制 / 通道隔离）
RevPublicMono/Implementation/RevMonoCore.cs      监听列表内核（去重 / 上限 / 快照派发 / 异常隔离）
Editor/RevResourceSystem/ABTool/ABLayoutSnapshot.cs   AB 布局快照差异（改名 / 换包 / 增删识别）
```

已验证的部分（都是跑出来的数字，不是估算）：

| 模块 | 断言 | 实测 |
|---|---|---|
| RevTimer | 55 / 55 | 1000 个计时器每帧 **0.0139ms**；10 万次创建+停止 <4KB |
| RevLog | 41 / 41 | 10 万条日志 **0 分配**；环形缓冲硬顶 2048 条 |
| RevPublicMono | 25 / 25 | 10 万次派发 **32B** |
| ABTool 布局快照 | 24 / 24 | 改名 / 换包 / 增删识别 |

---

## 🎯 设计取向

1. **内核尽量不依赖引擎**。纯 C# 部分可脱离 Unity 编译与断言；引擎相关收敛在适配层（`Support/`、`*Driver`/`*Player` 组件）。
2. **不静默**。框架不吞错误：失败一律给原因枚举，异常有统一出口（`RevLog`）。
3. **只走异步加载**。不为"看起来同步"的 API 付代价。
4. **能用生成代码就不手写常量**。目录前缀生成成 `const`，写错编译不过。
5. **默认值取安全侧**。不静默卸载、不停播、不静音；破坏性行为必须显式传参。
6. **门面只留一个**。每个模块一个入口文件，其余按"要不要读"分层（`Core` / `Facade` / `Implementation` / `Interfaces` / `Support`），小白只需读第一个。

---

## 💡 为什么用 Revolution？

### 1. 看得懂

每个模块一个门面、每个文件开头写清"为什么这么写"；`Core` / `Facade` / `Implementation` / `Interfaces` / `Support` 五层词汇表统一，
不需要先读完两万行才知道从哪下手。

### 2. 敢改

没有隐藏的反射魔法、没有第三方依赖、没有"只有作者知道"的约定；
编辑器工具（LiteAB / 导表）源码都在仓库里，改起来没有黑盒。

### 3. 有证据

内核能脱离 Unity 跑断言 —— 误差契约、句柄失效、异常隔离、零分配都不是"设计文档说的"，
而是**跑出来的**（145 条）。性能同样有数字（每帧 0.0139ms / 0 分配 / <4KB）。

### 4. 不埋雷

句柄带代际校验（过期句柄不会误伤新对象）、循环计时器有 `owner` 与作用域两套清理、
逐回调异常隔离、进 Play 自动清静态残留 —— 这些"上线才会痛"的点都在框架层堵住了。

---

## 🎮 示例项目

| 示例 | 位置 |
|---|---|
| 动作序列 / GM 指令 / 状态机 | `RevolutionFrameWork_Unity/Assets/Revolution.Demo/` |
| 导表 Demo（Excel + 生成的代码与数据） | `Revolution.Demo/ExcelTool/` |
| 状态机示例源码（Boss AI / 游戏流程 / UI 栈） | `Revolution.Demo/Unity/RevStateMachine/` |

---

## 🤝 贡献与支持

欢迎提交 [Issue](https://github.com/Yokino337088/Revolution/issues) 和 Pull Request！

> 提交代码前请留意两点：
> ① **`.meta` 文件必须一起提交**（里面存的是资产的 GUID 与导入设置，缺了会让引用断裂、设置回默认值，甚至丢分包标记）；
> ② 新增模块请沿用现有目录词汇（`Core` / `Facade` / `Implementation` / `Interfaces` / `Support`）与文件头注释格式。

---

**Made with ❤️ by Revolution**

[⭐ Star](https://github.com/Yokino337088/Revolution) | [🐛 Issues](https://github.com/Yokino337088/Revolution/issues) | [📖 文档](Revolution.Document) | [MIT License](LICENSE)
