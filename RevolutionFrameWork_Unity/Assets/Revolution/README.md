# Revolution

**Unity 游戏框架本体** —— 框架的全部代码都在这个目录里（`git clone` / 子模块 / UPM 三种装法都指向它）

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-blue.svg?style=flat-square)](https://unity.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg?style=flat-square)](https://github.com/Yokino337088/Revolution/blob/main/LICENSE)
[![Modules](https://img.shields.io/badge/Runtime%20模块-16%20个-brightgreen.svg?style=flat-square)](https://github.com/Yokino337088/Revolution)

---

## 🚀 一行上手

```csharp
RevSound.Play("ui_click");                                   // 播放音效
RevUI.Open<LoginPanel>();                                    // 打开面板
RevTimer.After(2f, () => RevUI.Close<LoadingPanel>());        // 2 秒后关掉
RevLog.Info("登录成功", "Login");                             // 打日志（分级 + 模块标签）
RevMono.AddUpdate(OnTick, owner: this);                      // 让纯 C# 类每帧跑一次
```

不摆物体、不挂脚本、零配置 —— 框架自己解决"谁在每帧驱动"的问题。

## ✨ 特点

- 🧩 **模块化** - 16 个运行期模块，每个一个门面（`RevSound` / `RevTimer` / `RevInput` / `RevLog` …），可单独拿走、可整块删除
- 🧪 **可脱离 Unity 验证** - 内核是纯 C#，链接进普通 .NET 工程就能跑断言（145 条，不打开 Unity）
- 🛡 **防漏防崩** - 句柄代际校验、`owner` / 作用域一行清理、逐回调异常隔离、失败必带原因枚举
- 📦 **编辑器工具齐** - LiteAB 打包窗口（分包浏览自动同步 / Project 窗口包名角标 / 体积依赖漏标检查 / 布局快照对比）+ 导表工具
- 🔧 **无第三方依赖** - 只用 Unity 官方模块

## ⚠️ 不含热更新（用前必读）

本框架**不含**：
- ❌ **代码热更新**（HybridCLR / ILRuntime / xLua —— 全仓库 0 处相关代码）；
- ❌ **AB 远程下载 / 版本管理 / 差量更新** —— AB 只从本机 `StreamingAssets` 读（编辑器下直读），没有 `persistentDataPath` 覆盖路径、没有下载器与版本比对。

**要接热更**：资源层对外只有 `IRevResPolicy` + `IRevResLoader` **两个接口**（在 `Runtime/RevResourceSystem/Interfaces/`，`RevABLoader` 是同接口的现成范例），
可以自己实现（接你已有的下载/CDN 体系），也可以整体换成 **YooAsset** 等第三方资源框架 —— **两条路都不需要改上层业务代码**。

详细方案（含步骤、注意事项与"要不要上热更"的决策表）见仓库首页 README：
<https://github.com/Yokino337088/Revolution#-不做什么热更新与远程更新>

## 📦 三种安装方式

| 方式 | 命令 / 操作 | 框架落在哪 |
|---|---|---|
| ① **放进工程 `Assets/`**（推荐） | `git clone -b package --depth 1 https://github.com/Yokino337088/Revolution.git Assets/Revolution` | `Assets/Revolution` |
| ② 子模块（可 `git submodule update --remote` 更新） | `git submodule add -b package https://github.com/Yokino337088/Revolution.git Assets/Revolution` | `Assets/Revolution` |
| ③ Package Manager（UPM） | Package Manager → Add package from git URL → `https://github.com/Yokino337088/Revolution.git?path=/RevolutionFrameWork_Unity/Assets/Revolution` | `Packages/`（或 `Library/PackageCache`） |

> 方式①②在**工程根目录**执行（`Assets/` 同级）。克隆出来的 `Assets/Revolution` 里带 `.git` 目录，Unity 会忽略它（以 `.` 开头的目录不导入）。

### 为什么推荐方式①②（放 `Assets/`）

| 原因 | 说明 |
|---|---|
| `Resources` 生效 | 框架自带 `Resources/ResourceSystem/ResMap.txt` 与两个 UI 预制体；Unity 的 `Resources.Load` 只保证加载**工程 `Assets` 下**的 `Resources` 文件夹 —— 装在 `Packages/` 里不保证（框架对此有降级：Canvas 会用代码建、缺 ResMap 时编辑器直读照常工作） |
| 生成物可写 | `Generation/RevResPath.cs`、`RevSoundSystem/Generated/RevSoundPath.cs` 是打包工具生成的常量；包目录只读，UPM 装法下工具会**跳过生成并提示** |
| 可以直接改 | 想改框架代码、加日志、裁模块，在 `Assets/` 下随手就能改 |

### 方式③（UPM）的两个注意点

1. 包在 `Packages/` 下 → 上面两条限制生效（`Resources` 不保证、生成物跳过）；**要用完整能力请用方式①②**；
2. 包目录是只读缓存 → 想改代码请先 **Embed**（Package Manager → 右键包 → Embed），或改用方式①②。

## ✅ 装完后的三步

1. 若工程里没有 `Assets/GameRes`，新建一个（框架的**资源根目录**约定）；
2. 菜单 `Revolution.Tools / 资源 / LiteAB 打包工具` → ① 打包配置 → 把「资源根目录」设为 `Assets/GameRes`；
3. 直接 `RevSound.Play("ui_click")` / `RevTimer.After(2f, ...)` 开始写业务。

> 打包配置（`Assets/Editor/ABBuildConfig.asset`）属于**本机设置**，不在仓库里 —— 文件不存在时工具会自动生成一份默认的。

## 🔄 更新到最新版

| 装法 | 更新命令 |
|---|---|
| ① clone 进 `Assets/` | `git -C Assets/Revolution pull` |
| ② 子模块 | `git submodule update --remote Assets/Revolution` |
| ③ UPM | Package Manager 里选中该包点 **Update**（或重新 Add package from git URL） |

> `package` 分支由仓库的 GitHub Actions 自动同步（只同步 `Assets/Revolution/`）；也可手动同步：
> `git subtree split --prefix=RevolutionFrameWork_Unity/Assets/Revolution -b package && git push -f origin package`

## 📁 这个目录里有什么

```text
Revolution/
├── Runtime/          15 个运行期模块（RevResourceSystem / RevUISystem / RevSoundSystem / RevTimer / RevLog / …）
├── Editor/           编辑器工具（LiteAB 打包窗口、GM 指令面板）
├── Generation/       生成的路径常量（RevResPath.cs，勿手改）
├── Resources/        框架自带运行时资源（ResMap.txt、UI 预制体）
├── package.json      UPM 包描述（com.yokino.revolution，零依赖）
├── README.md         本文件
└── CHANGELOG.md      版本记录
```

模块内部统一按 `Core`（数据与契约）/ `Facade`（对外入口）/ `Implementation`（实现）/ `Interfaces`（接口）/ `Support`（宿主适配）分层，
上手只需要读 `Facade` 里的那一个入口文件。

## 📚 文档

- **在线文档站（推荐从这里进）**：<https://yokino337088.github.io/Revolution/>
- 各模块《使用说明》（手把手）与《架构解析》（设计论证）：仓库 `Revolution.Document/`
- 3 分钟上手（模块级 README）：`Runtime/RevSoundSystem/README.md`、`Runtime/RevTimer/README.md`、`Runtime/RevLog/README.md` 等

## 📄 许可

[MIT](https://github.com/Yokino337088/Revolution/blob/main/LICENSE) © 2026 Yokino337088
