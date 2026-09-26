# Revolution · Unity 游戏框架

> 一套从零手写的 Unity 游戏框架（C#）：**可读、可测、无魔法**。
> 内核尽量不依赖引擎 —— 路径 / 句柄 / 表 / 池这些纯 C# 部分可以**脱离 Unity 编译与断言**；
> 编辑器工具链配套齐全（AB 打包窗口 / 导表工具 / GM 指令）。

- **规模**：Runtime **121 个 `.cs` / 约 1.8 万行**，Editor 工具 **17 个 `.cs` / 约 4.5 千行**
- **Unity**：2022.3.15f1c1（依赖均为 Unity 官方包，见 `Packages/manifest.json`）
- **文档**：每个模块一份《使用说明》（手把手）+ 多数模块一份《使用指南》（设计论证），见 [`Revolution.Document/`](Revolution.Document)
- **许可**：MIT

---

## 一、把框架装进你自己的工程（三种方式）

框架本体 = `Assets/Revolution/`（`package` 分支的**仓库根**就是它）。在你的**工程根目录**执行：

| 方式 | 命令 | 框架落在哪 | 适合 |
|---|---|---|---|
| ① **放进 `Assets/`**（推荐） | `git clone -b package --depth 1 https://github.com/Yokino337088/Revolution.git Assets/Revolution` | `Assets/Revolution` | 想直接改框架代码；需要 `Resources`（ResMap、UI 预制体）与两个生成物可写 |
| ② 子模块（可 `git submodule update --remote` 更新） | `git submodule add -b package https://github.com/Yokino337088/Revolution.git Assets/Revolution` | `Assets/Revolution` | 同样放 `Assets/`，但用 git 子模块管理版本 |
| ③ Package Manager（UPM） | Add package from git URL → `https://github.com/Yokino337088/Revolution.git?path=/RevolutionFrameWork_Unity/Assets/Revolution`（或 `#package`） | `Packages/` | 只当依赖用、不打算改代码 |

> **为什么推荐 ①/②**：Unity 的 `Resources.Load` 只保证加载**工程 `Assets` 下**的 `Resources` 文件夹 —— 框架自带 `Resources/ResourceSystem/ResMap.txt` 与两个 UI 预制体；另外 `Generation/RevResPath.cs`、`RevSoundSystem/Generated/RevSoundPath.cs` 这两个**生成物**必须写在框架目录里（Runtime 不能反向引用 Generation），包缓存只读写不了。
> 装在 `Packages/` 里框架仍有降级能力（Canvas 用代码建、缺 ResMap 时编辑器直读照常工作），打包工具会**跳过生成并提示**。

### 装完后的三步

1. 工程里准备**资源根目录**：新建 `Assets/GameRes`（或你已有的目录）；
2. 菜单 `Revolution.Tools / 资源 / LiteAB 打包工具` → **① 打包配置** → 把「资源根目录」设为 `Assets/GameRes`
   （「音效目录」「BGM 目录」默认 `Audio/Sfx`、`Audio/Bgm`，按自己的目录结构改；这两项会被工具写进生成常量）；
3. 开始写业务：`RevSound.Play("ui_click")` / `RevSequence.Create("开宝箱")` …

> 打包配置（`Assets/Editor/ABBuildConfig.asset`）属于**本机设置**，不在仓库里 —— 文件不存在时工具会自动生成一份默认的。

## 二、想直接跑示例（克隆整个仓库）

```bash
git clone https://github.com/Yokino337088/Revolution.git
```

用 **Unity 2022.3.15f1c1** 打开 `RevolutionFrameWork_Unity/`（首次导入需要几分钟），
然后 Play `Assets/Scenes/SampleScene.unity`；示例代码（动作序列 / GM 指令）在 `Assets/Revolution.Demo/`。

---

## 二、模块一览

### 运行期（`Assets/Revolution/Runtime/`）

| 模块 | 一句话 | 规模 |
|---|---|---|
| `RevResourceSystem` | 资源加载：编辑器直读 + AB 两条后端、引用计数、自动卸载、失败原因可查 | 17 个 `.cs` / 2,646 行 |
| `RevObjectPool` | 对象池：GameObject 池 + 纯 C# 对象池，与资源加载打通（池端着资源引用） | 10 / 2,039 |
| `RevUISystem` | UI 系统：面板声明式配置（`[RevUIPanel(root, layer)]`）、层级、与资源池联动 | 17 / 3,457 |
| `RevActionSequence` | 动作序列：一行 DSL 表达"播放 → 等待 → 并行 → 嵌套"，含取消收尾契约 | 22 / 2,234 |
| `RevStateMachine` | 状态机：轻量流程（GameFlow）/ 重量级 AI，两种形态 | 10 / 1,576 |
| `RevSoundSystem` | 音效系统：一行播放（2D 直接播 / 3D 挂到 GameObject）、BGM、音量总线、作用域、音效表 | 10 / 1,640 |
| `RevEventSystem` | 事件系统：强类型事件 + 订阅句柄 | 5 / 1,101 |
| `RevGMCommand` | GM 指令：游戏内控制台，业务一行注册一条指令 | 11 / 1,023 |
| `DataLoad` | 配置表加载：表 = 资源（走资源系统），支持按类型取表 | 6 / 714 |
| `RevServiceLocator` | 服务定位器：把业务依赖挡在框架之外 | 8 / 702 |
| `RevTask` | 异步任务：`await` 一帧 / 等资源加载完成 | 4 / 639 |
| `RevSingleton` | 单例基类（60 行，尽量少用） | 1 / 63 |

### 编辑器（`Assets/Revolution/Editor/`）

| 工具 | 一句话 | 规模 |
|---|---|---|
| `RevResourceSystem`（ABTool） | **LiteAB 打包工具**：分包浏览 / 依赖 / 体积 / 漏标检查；生成 `ResMap.txt` 与路径常量 | 15 / 3,645 |
| `RevGMCommand` | GM 指令的编辑器部分（指令面板） | 2 / 820 |

### 独立工具与示例

| 目录 | 说明 |
|---|---|
| `Revolution.ExcelTool/` | **导表工具**（WPF）：Excel → C# 数据类 + txt 数据文件 |
| `Revolution.Demo/ExcelTool/` | 导表 Demo：配置 Excel + 生成的代码与数据 |
| `Revolution.Demo/Unity/RevStateMachine/` | 状态机示例源码（Boss AI / 游戏流程 / UI 栈） |

---

## 三、目录结构

```text
Revolution/
├── RevolutionFrameWork_Unity/          Unity 工程
│   ├── Assets/Revolution/Runtime/        框架内核（上表的模块）
│   ├── Assets/Revolution/Editor/         编辑器工具（LiteAB 打包窗口、GM 指令）
│   ├── Assets/Revolution/Generation/     生成的路径常量（RevResPath.cs）
│   ├── Assets/Revolution/Resources/      框架自带的运行时资源（ResMap、UI 预制体）
│   ├── Assets/Revolution.Demo/           示例（动作序列、GM 指令）
│   ├── Assets/Scenes/                    示例场景
│   ├── Assets/GameRes/                   资源根目录（放你的资源，AB 工具从这里扫）
│   └── Packages/  ProjectSettings/       Unity 工程配置
├── Revolution.Document/                设计文档（每个模块：使用说明 / 使用指南）
├── Revolution.Demo/                    示例数据与示例源码
└── Revolution.ExcelTool/               导表工具（源码）
```

> 仓库只收录**必要的代码与文档**：Unity 的 `Library/` `Temp/` `Logs/`、构建产物（`AssetBundles/` `StreamingAssets/`）、
> IDE 工程文件（`*.csproj` `*.sln`）、本机打包配置都不入库 —— 规则见 [`.gitignore`](.gitignore)。

---

## 四、设计取向

1. **内核尽量不依赖引擎**。路径拼接、句柄、槽位表、对象池引擎等是纯 C#，可以链接进普通 .NET 工程直接跑断言（见第六节）；
   与引擎相关的部分收敛在"适配层"（如 `Unity/` 目录、`*Player`/`*Driver` 组件）。
2. **不打日志、不做查询**。框架自身一条日志都不打；需要观测就订阅事件（失败一律带**原因枚举**）。
   这不是省事，而是为了"等框架自己的日志系统接上之前，不制造第二套日志"。
3. **只走异步加载**。不为"看起来同步"的 API 付出代价：异步加载有明确完成回调，不会出现"同步命中正在加载的句柄 → 拿到空内容"。
4. **能用生成代码就不手写常量**。目录前缀由编辑器工具生成成 `const`（写错编译不过），新增资源不用重新生成。
5. **默认值取安全侧**。不静默卸载、不停播、不静音；需要破坏性行为必须显式传参。
6. **门面只留一个**。每个模块一个入口文件（`RevSound.Play(...)` / `RevSequence.Create(...)` / `RevPool.Get(...)`），
   其余按"要不要读"分层（`Engine/` 内核、`Extras/` 可选能力），小白只需要读第一个文件。

---

## 五、文档

| 模块 | 文档 |
|---|---|
| 资源加载系统 | [`Revolution.Document/资源加载系统/`](Revolution.Document) |
| 对象池 | [`Revolution.Document/对象池/`](Revolution.Document) |
| UI 系统 | `Revolution.Document/UI系统/` |
| 动作序列 | `Revolution.Document/动作序列/` |
| 状态机 | `Revolution.Document/状态机/` |
| 音效系统 | `Revolution.Document/音效系统/` |
| 事件系统 | `Revolution.Document/事件系统/` |
| 服务定位器 | `Revolution.Document/服务定位器/` |
| GM 指令 | `Revolution.Document/GM指令/` |
| 导表工具 | `Revolution.Document/导表工具/` |

另外，代码目录里也有"3 分钟上手"级别的说明：
`Assets/Revolution/Runtime/RevSoundSystem/README.md`、`RevActionSequence/README.md`、`RevGMCommand/README.md` 等。

---

## 六、怎么在工程外验证内核（纯 C#）

框架里有一批**不引用 `UnityEngine`** 的文件，例如：

```text
RevResourceSystem/Core/ResPathUtil.cs          路径拼接 + 缓存键（含"两段键 == 完整路径键"不变量）
RevSoundSystem/Engine/RevSoundVoiceTable.cs    声音槽位表（代际号 / 同帧去重 / 上限淘汰）
RevSoundSystem/Engine/RevSoundCatalog.cs       音效表（逻辑名 → 路径 + 默认参数）
RevObjectPool/Core/RevPoolCore.cs              池引擎
```

把它们链接进一个普通 .NET 控制台工程（`<Compile Include="...路径..." />`）就能跑断言，
不需要打开 Unity —— 这也是这些文件坚持"不引用 UnityEngine"的原因。

---

## 七、许可

[MIT](LICENSE) © 2026 Yokino337088
