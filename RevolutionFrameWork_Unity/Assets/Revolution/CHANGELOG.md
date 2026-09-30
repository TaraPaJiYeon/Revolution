# 更新日志

## [未发布]

### 新增

- **导表工具（Unity 编辑器版）**：菜单 `Revolution.Tools/配置表/导表工具`（另有「快速导出」一步导完、CI 入口 `Revolution.Editor.ExcelTool.RevExcelCI.Export`）。Excel 规则与 WPF 版完全相同，生成的代码与数据逐字节一致（除生成时间）。相比 WPF 版：拖入 Excel 即读、Excel 开着也能读、保存后自动重读并标出改动的表；全量数据预览（运行时解析不了的单元格标色、错误一键定位到行）；输出目录默认进 Generation 程序集 / `<资源根目录>/Data`，并在导出前检查代码目录所属程序集与数据目录是否可被运行时读到；只写内容有变化的文件（仅数据变化时不触发脚本编译）；新增 / 删除表后自动生成资源映射，数据没有 AB 标记时提示并可一键标记；导出会移除上次导出过的表时先确认，旧数据文件可一键清理。输出设置存 `ProjectSettings/`（团队共享），Excel 来源存 `UserSettings/`（个人）。
- **`ABMapGenerator`**：「仅生成映射」的唯一实现，打包窗口与导表工具共用。
- **动作序列：易用性**
  - 一行播放：`清单.Play(gameObject)` / `await 清单.PlayAsync(gameObject)`。
  - 新增步骤：`.Tween`（随时间变化，可带起始值与缓动 `RevEase`）、`.If`（运行时分支）、`.Finally`（跑完 / 取消 / 出错都执行的收尾）、`.Log`、`.Publish(ctx => 事件)`、`.WaitTask<T>`（拿到任务结果）。
  - Unity 现成步骤：`.MoveTo` / `.ScaleTo` / `.FadeTo` / `.SetActive`。
  - 省略名字：`.Do(ctx => …)` / `.WaitUntil(ctx => …)`，日志里用"文件名:行号"定位。
  - 上下文：`ctx.Require<T>()`（必需服务，取不到直接报错并说明怎么注册）、`ctx.SourceAs<T>()`、`ctx.SourceGameObject()` / `SourceTransform()`；服务按接口兜底查找（`Add(new Impl())` 后 `Get<IFoo>()` 也能取到）。
  - `WaitUntil` 新增 `onTimeout`；`WaitTask` 新增 `cancelOnFailure`；`RevSequencePlayer.UseUnscaledTime`（暂停时也推进）；`runner.ActiveCount`。

### 修复

- **动作序列**：
  - 在步骤 / 回调 / await 续体里调用 `StopAll` / `Clear` / `Dispose` 会让 Tick 下标越界、已归还池的运行实例被继续推进 → Tick 改为按快照遍历，期间的取消统一在本次 Tick 末尾收尾。
  - 在步骤里 `Stop` 自己后，同一帧剩下的步骤仍会执行、最终按"完成"结束 → 每进下一步前检查取消（并行 / 重复 / 分支内部同样）。
  - `RunFinished` 订阅者抛异常时 `PlayAsync` 永不完成 → 各环节独立 try/catch，等待者在最后必然被唤醒。
  - DEBUG 下步骤抛异常会一直留在活跃表里、每帧重复抛出并阻塞其它序列 → 统一为"记日志 + 按取消收尾"。
  - 嵌套序列的 `ctx.Source` 被错误设成了父序列的上下文对象 → 新增 `Play(definition, context)` 重载，子序列沿用父序列的触发者 / 服务 / 事件总线。
  - 场景里的全局驱动者随场景销毁后，全局引擎再也没人 Tick → 访问 `Default` 时自动补建；关闭域重载时静态状态每次进入播放模式复位。
  - 同一个步骤实例被放进两条序列会互相覆盖状态槽 → `Build()` 时报错。
  - 收尾步骤里的 `Parallel` 不会执行子步骤、某一步抛异常会跳过其余收尾 → 收尾每步执行后再推进一次，逐步隔离异常；收尾里放等待步骤在 `Build()` 时报错。
  - 等待超时、异步任务失败、收尾 / 回调异常以前被静默吞掉 → 经 RevLog 输出（tag = `ActionSequence`），写明序列名与步骤。

### 变更

- **动作序列**：`OnCancel` / `OnCompleted` / `OnCancelled` 多次调用改为叠加（以前后一次覆盖前一次）；`RevSequenceDefinition.FinallySteps` 现在表示 `.Finally` 的步骤，取消收尾改名为 `CancelSteps`；触发者（Unity 对象）被销毁时序列自动取消（`runner.StopWhenSourceDestroyed = false` 可关闭）。

- **打包工具更名：LiteAB → RevAB**。菜单改为 `Revolution.Tools/资源/RevAB 打包工具`（与 `RevAB 分包浏览`）；日志 tag 改为 `RevAB`（静音过 `LiteAB` 的请改成 `RevLog.MuteTag("RevAB")`）；本机偏好键与快照目录（`Library/Revolution/RevAB/`）在首次使用时从旧名字自动迁移。
- **ABTool 按职责拆成子目录**：`Core` / `Pipeline` / `CodeGen` / `Snapshot` / `Integration` / `Window`（`.meta` 随文件移动，GUID 不变；`ABCIBuild.Build` 的调用方式不变）。
- **「分包」页签重做（对标 AssetBundle Browser）**：包树（`/` 分层、多选、搜索、F2 改名、Delete 删除、右键菜单、拖包调层级）+ 多列资源表（排序、标记来源、双击定位、拖到别的包 = 换包）+ 详情面板；从 Project 拖资源到空白处 = 以资源名新建包；包名统一小写，改名 / 删除后自动清理未使用的包名。
- **「打包」页签重做**：目标平台可选（不必先切平台，本机偏好）、输出目录可一键打开、主按钮置顶；配置按分组折叠、选项中文化；切到「按目录自动分包」前先确认；打包结果显示产物体积与用时。
- **检查类页签**：「检查」标题带问题数；共享资源 / 漏标资源可一键移进一个包，空包名一键清理；包名可点击跳转到「分包」页签；依赖分析改为下一帧带可取消进度条执行，不再卡住窗口；体积等数据按扫描结果缓存。

## [0.1.0] - 2026-09-26

### 新增

- **运行期（13 个模块 / 121 个 `.cs`）**：资源加载、对象池、UI 系统、动作序列、状态机、音效系统、事件系统、GM 指令、配置表、服务定位器、异步任务、单例
- **编辑器工具（17 个 `.cs`）**：LiteAB 打包工具（分包浏览 / 依赖 / 体积 / 漏标检查 + `ResMap` 与路径常量生成）、GM 指令面板
- **文档**：每个模块的《使用说明》（手把手）与《架构解析》（设计论证），见仓库 `Revolution.Document/`
- **安装方式**：`package` 分支（框架本体在根，可直接 clone / submodule 进工程 `Assets/`）、UPM git URL
