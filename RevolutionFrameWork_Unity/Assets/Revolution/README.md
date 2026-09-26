# Revolution —— 框架本体

Unity 游戏框架本体（框架的全部代码都在这个目录里，`git clone` / 子模块 / UPM 三种装法都指向它）。

- **Unity**：2022.3+（开发版本 2022.3.15f1c1）
- **依赖**：无（只用 Unity 引擎模块）
- **许可**：MIT

## 三种安装方式

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

## 装完后的三步

1. 若工程里没有 `Assets/GameRes`，新建一个（框架的**资源根目录**约定）；
2. 菜单 `Revolution.Tools / 资源 / LiteAB 打包工具` → ① 打包配置 → 把「资源根目录」设为 `Assets/GameRes`；
3. 直接 `RevSound.Play("ui_click")` / `RevSequence.Create(...)` 开始写业务。

## 更新到最新版

| 装法 | 更新命令 |
|---|---|
| ① clone 进 `Assets/` | `git -C Assets/Revolution pull` |
| ② 子模块 | `git submodule update --remote Assets/Revolution` |
| ③ UPM | Package Manager 里选中该包点 **Update**（或重新 Add package from git URL） |

> `package` 分支由仓库的 GitHub Actions 自动同步（只同步 `Assets/Revolution/`）；也可手动同步：
> `git subtree split --prefix=RevolutionFrameWork_Unity/Assets/Revolution -b package && git push -f origin package`

## 文档

- 各模块《使用说明》与《使用指南》：仓库 `Revolution.Document/`
- 3 分钟上手（模块级 README）：`Runtime/RevSoundSystem/README.md`、`Runtime/RevActionSequence/README.md`、`Runtime/RevGMCommand/README.md` 等
