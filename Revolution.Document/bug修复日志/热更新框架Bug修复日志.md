# Revolution.HotUpdate 热更新框架 Bug 修复日志

> **一句话总结**：对 `Assets/Revolution.HotUpdate` 全部 20 个文件（18 Runtime + 2 Editor）做了九轮审查，共修复 **21 处问题**（6 处致命、4 处严重、5 处中等、6 处加固），修复后框架可投入生产。
> 第九轮教训：第八轮曾宣告收敛，但换用"数据流追踪"方法（追一个包的依赖闭包从打包机到玩家屏幕的完整链路）后，又发现一个此前所有轮次都漏掉的架构级缺陷 —— **审查方法的多样性比审查轮数更重要**。
>
> - 审查日期：2026-10-08
> - 审查范围：`RevolutionFrameWork_Unity/Assets/Revolution.HotUpdate`（含与框架资源系统 `RevABLoader` / `RevResBootstrap`、异步设施 `RevTask` 的接缝）
> - 验证方式：每轮修改后全量编译诊断，零错误零警告

---

## 目录

- [一、致命问题（6 处）](#一致命问题6-处)
- [二、严重问题（4 处）](#二严重问题4-处)
- [三、中等问题（5 处）](#三中等问题5-处)
- [四、加固与防御（6 处）](#四加固与防御6-处)
- [五、修复统计](#五修复统计)
- [六、遗留事项（非 Bug，不阻塞投产）](#六遗留事项非-bug不阻塞投产)

---

## 一、致命问题（6 处）

这四个问题单独任何一个都会让"热更成功"直接变成"资源全挂"，且都属于**只在真机 / 第二次热更后才爆发**的类型 —— 编辑器里跑一遍 Demo 根本测不出来。

### 1. Android 首包永不落地（调用链断裂）

| 项目 | 内容 |
|------|------|
| 现象 | Android 真机上，所有**未热更过**的内置包加载失败 |
| 根因 | `RevHotStore.CopyBuiltinPackagesAsync`（把内置包从 APK 拷到 persistentDataPath）**全项目只有定义、没有任何调用点**。`_builtin` 目录只被 `PrepareLocalRoot` 建出来，永远是空的 → 加载路径逐级查空 → 返回 null → 框架默认走 StreamingAssets → Android 的 StreamingAssets 是 `jar:` 路径，`LoadFromFile` 读不了 |
| 修复 | 接入 `RunCheckAsync`：读到内置基线清单后立即执行落地。幂等（已落地且大小一致直接跳过）；`ReadFromStreamingAssets` 模式不拷；WebGL 在方法内部直接跳过 |
| 文件 | `Runtime/Facade/RevHotUpdate.cs` |

### 2. 差量更新后，未变化的包全部失效

| 项目 | 内容 |
|------|------|
| 现象 | 第二轮热更起，除差量包外的所有 AB 资源加载失败（404 / 降级到 Resources 兜底） |
| 根因 | 差量方案只把 **Added + Changed** 的包写进新版本目录，未变化的包还留在旧版本目录里；而 `ResolveBundlePath` **只查当前版本目录**。场景：v37 热更新增包 A → v38 只改了 hero → 38 目录只有 hero → 加载 A 直接失败 |
| 修复 | `RevHotStore` 增加**历史版本回退链**：`SetActiveVersion` 时扫描当前大版本目录，收集"带 `.stamp` 的完整版本"（新→旧排序，半截目录绝不进链）。加载顺序变为：**当前版本 → 历史完整版本（新→旧）→ 首包落地目录 → 交还框架默认（StreamingAssets）**。配套：`DeleteOldVersions` 删完重建链、`ResetForNewSession` 清链 |
| 说明 | WebGL 不需要回退链：那边引擎缓存以 `Hash128` 为 key，hash 相同即命中，URL 换版本段无所谓 |
| 文件 | `Runtime/Pipeline/RevHotStore.cs` |

### 3. `UpdateAsync` 直调路径不装加载钩子

| 项目 | 内容 |
|------|------|
| 现象 | 走"检查更新 → 弹窗确认 → 执行更新"流程后，更新、切版本全部成功，但随后加载资源读到的还是旧内容 —— 差量下载全部白做 |
| 根因 | 装载 `BundlePathResolver` / `BundleCacheKeyResolver` / `ResMapOverride` 三个加载钩子的 `Install()` **只在 `InitializeAsync` 路径里调用**。而 `CheckAsync → UpdateAsync` 是公开 API 的业务主路径（用途：先弹窗问玩家 WIFI/大包确认再下载），这条路径上钩子永远不装 |
| 修复 | `RunUpdateAsync` 校验通过后补 `RevHotResBridge.Install()`（内部 `_installed` 防重，幂等无害），并注释原因防止后人"顺手删掉" |
| 文件 | `Runtime/Facade/RevHotUpdate.cs` |

### 4. WebGL 同会话二次热更拿旧映射表（修复过程中引入的回归，已闭环）

| 项目 | 内容 |
|------|------|
| 现象 | 同一会话内第二次热更且映射表变化时，新版本的新增资源全部加载不到 |
| 根因 | 抽象 `EnsureWebGLResMapAsync` 时带入的"已就位就跳过"（`HasMemoryResMap == true` 直接 return）套在了**所有**调用点上。同会话 v38 更新时已把 v38 的表拉进内存 → v39 更新时 `ResMapChanged == true`，但"已就位"直接跳过 → 切版本后资源系统读到的还是 **v38 的旧表**。文件平台没有此问题（表按版本目录落盘、天然隔离），是 WebGL 特有的坑 |
| 修复 | `EnsureWebGLResMapAsync` 增加 `force` 参数：更新路径传 `check.Plan.ResMapChanged`（表变了就**强制重拉**）；无更新收尾路径传 `false`（只补空） |
| 文件 | `Runtime/Facade/RevHotUpdate.cs` |

### 5. 玩家取消下载 → 半截版本被打上完成标记上线，且返回"更新成功"

| 项目 | 内容 |
|------|------|
| 现象 | 玩家在差量下载中途取消（或切后台被中断）后，流程**不报取消**，而是继续打 `.stamp`、写 `current.txt` 切版本，结果对象返回"已更新到 vX"—— 半新半旧的资源集被当成完整版本上线，玩家进游戏即错版 |
| 根因 | 自研 `RevTask.WhenAll` 的完成回调**只递减计数、从不检查异常**（`OnCompleted(() => { if (--remain == 0) source.SetResult(); })`）。`DownloadPlanAsync` 里工人抛出的 `RevOperationCanceledException` 进入该工人 RevTask 的异常状态后被 WhenAll 彻底吞掉；聚合处只检查 `session.FirstError`（取消不走 `Fail`）→ 取消被吞 → 流程继续走完切版本。注释里"已下载内容已保留，下次继续"的取消语义从未真正生效 |
| 修复 | ① `DownloadSession` 增加 `Cancelled` 专用标志（与复用于"失败也停"的 `Stop` 分开）；② 工人取消时先记账再上抛；③ `DownloadPlanAsync` 在 `WhenAll` 之后 `if (session.Cancelled) throw new RevOperationCanceledException();` —— 半截版本绝不能被打上 stamp / 切为当前版本 |
| 说明 | 已确认 `WhenAll` 全项目仅 `DownloadPlanAsync` 一处使用，无其他同类漏洞；失败路径（`FirstError`）原本就正常 |
| 文件 | `Runtime/Download/RevHotDownloader.cs` |

### 6. 热更清单的依赖图从未被消费 —— 新增/变更包的依赖闭包不会被加载

| 项目 | 内容 |
|------|------|
| 现象 | 热更新增的包 C（依赖出包时就存在的 A）加载时，A 不被自动加载 → C 的资产加载失败 / missing；**变更包同样中招**：v38 改了 A 且 A 新增了对 B 的依赖 → 主包 Manifest 里 A 的依赖记录是旧的 → B 不被加载。表现为"时而好时而坏"（依赖包恰好被别的资源加载过就侥幸通过）—— 最难排查的那类错版 |
| 根因 | 两个事实叠加：① 热更清单的 `Dependencies` 字段（Builder 精心生成、SelfCheck 校验、注释声称"用于下载前算依赖闭包"）**运行时全项目零消费** —— 该功能从未实现；② `RevABLoader` 的依赖解析（同步 2 处 + 异步 1 处 `GetAllDependencies`）**完全依赖 Unity 主包 Manifest**，而主包永远是出包时的静态快照 —— 它不认识热更新增的包、记不住变更包的新依赖 |
| 修复 | ① 框架 `RevABLoader` 新增第四个钩子 `DependenciesOverride`（与 `BundlePathResolver` 同款纪律：返回 null 回退主包 Manifest，不装热更包零影响）；加载与释放统一走新方法 `ResolveDependencies`（同一来源，+1/-1 才能配对）；② 热更包 `RevHotStore` 钉住"当前生效清单"（`_activeManifest`，所有平台），实现 `ResolveDependencies`：清单认识该包 → 返回清单的新鲜依赖图；不认识（主包本身）→ 返回 null 回退；③ `RevHotResBridge.Install` 装载钩子；④ `ResetForNewSession` 清 `_activeManifest` |
| 文件 | `Revolution/Runtime/RevResourceSystem/Implementation/Loaders/RevABLoader.cs`（框架侧）、`Runtime/Pipeline/RevHotStore.cs`、`Runtime/Integration/RevHotResBridge.cs` |

---

## 二、严重问题（4 处）

### 5. 下载速度永远显示 0

| 项目 | 内容 |
|------|------|
| 现象 | 更新进度条上的速度数字恒为 0 |
| 根因 | `RevHotProgressAggregator.Report` 先把 `_lastReportMs` 覆盖成 `now`，再算 `dt = now - _lastReportMs` —— **dt 恒为 0**，`if (dt > 0)` 永不成立，速度从未被计算 |
| 修复 | 先保存上次上报时刻（`lastMs`）再更新字段，`dt = now - lastMs` |
| 文件 | `Runtime/Core/RevHotProgress.cs` |

### 6. 二次 Play 残留旧版本引擎缓存表

| 项目 | 内容 |
|------|------|
| 现象 | 关闭 Domain Reload 的快速进入 Play 后，WebGL/小游戏上引擎拿旧版本的 hash 校验新包，直接加载失败 |
| 根因 | `RevHotStore.ResetForNewSession` 漏清 `_bundleKeys` —— 上一轮 Play 的 `Hash128` 缓存表原封不动活到下一轮 |
| 修复 | Reset 时 `_bundleKeys = null`（与 `_memoryResMap`、`_fallbackBundleDirs` 一起清） |
| 文件 | `Runtime/Pipeline/RevHotStore.cs` |

### 7. 二次生成清单会把 ResMap.txt 副本当 AB 包扫进清单

| 项目 | 内容 |
|------|------|
| 现象 | 第二次点"生成清单"后，清单里多出一个名为 `ResMap.txt` 的"包"，客户端会白下载一个文件 |
| 根因 | 上次 `WriteOutputs` 把 `ResMap.txt` 副本拷进了产物目录，而 `RevHotManifestBuilder.Build` 的排除规则（.manifest / .json / RevHot 前缀 / 主包名 / 隐藏文件）**没有任何一条能排除它** |
| 修复 | 排除列表增加 `ResMapFileName` |
| 文件 | `Editor/RevHotManifestBuilder.cs` |

### 8. WebGL/小游戏的内存映射表丢失

| 项目 | 内容 |
|------|------|
| 现象 | 重启后（或"有更新但表没变"时），热更新增的资源加载不到 —— WebGL 没有文件系统，热更映射表只能放静态内存，每次重启内存都是空的 |
| 根因 | 拉表逻辑只在 `ResMapChanged` 时执行，且"没有更新"分支从不拉表 → `_memoryResMap` 为 null → `LoadHotResMap` 返回 null → 退回内置表，热更表里新增的逻辑名全部失效 |
| 修复 | ① `RevHotStore` 暴露 `HasMemoryResMap` 状态；② 新增 `EnsureWebGLResMapAsync`（已就位零成本跳过）：更新路径 `strict=true`（表拉不到算更新失败，可整体重试）；无更新/重启补拉路径 `strict=false`（拉不到只警告，内置表兜底、游戏至少能跑） |
| 文件 | `Runtime/Pipeline/RevHotStore.cs`、`Runtime/Facade/RevHotUpdate.cs` |

---

## 三、中等问题（5 处）

### 9. WebGL 二次启动重复走全量更新流程

| 项目 | 内容 |
|------|------|
| 现象 | WebGL/小游戏每次启动都白拉一遍映射表、重打一遍完成标记 |
| 根因 | 那边没有文件系统、存不下清单副本，本地基线永远退化为内置清单 → 二次启动仍拿"内置基线 vs 远端清单"判出"有更新" |
| 修复 | `RunCheckAsync` 加**版本相等短路**：上次生效版本 == 远端版本 → 直接返回无更新（资源 URL 带版本段、内容不可变，语义严格正确）；`ResVersionOverride` 调试模式不受影响 |
| 文件 | `Runtime/Facade/RevHotUpdate.cs` |

### 10. 强更分支"旧版本照常能玩"是纸面承诺

| 项目 | 内容 |
|------|------|
| 现象 | 首次启动 + 大版本不匹配的组合下，返回消息承诺"出包基线可用"，但资源系统从未被 `Init` 过 —— 基线并不可用 |
| 根因 | `ForceUpdateRequired` 分支只设置状态就返回，没有调用 `ReinitResourceSystem` |
| 修复 | 强更分支补：有 current 时 `SetActiveVersion` + `ReinitResourceSystem` 后再返回 Ready |
| 文件 | `Runtime/Facade/RevHotUpdate.cs` |

### 11. 并发下载的进度互相覆盖

| 项目 | 内容 |
|------|------|
| 现象 | 并发下载（默认 3 工人）时，进度条只有约 **1/N 的真实进度**：几乎不动 → 每完成一个文件跳一截；速度数字剧烈抖动 |
| 根因 | 一个聚合器服务 N 个工人，但"进行中字节"是单个共享字段 `_currentDone` —— 工人 A 喂 5MB、工人 B 喂 3MB，A 的被覆盖。总量与最终结果不受影响（`CompleteFile` 累计正确），纯过程显示缺陷 |
| 修复 | 聚合器改为**按文件名分账**（`Dictionary<string, long>`，容量按最大并发 8 预分配）：`done = _doneBytes + Σ进行中字节`。RevTask 主线程模型无需加锁；完成后的迟到上报直接忽略；6 处调用点全部同步新签名 |
| 文件 | `Runtime/Core/RevHotProgress.cs`、`Runtime/Download/RevHotDownloader.cs`、`Runtime/Pipeline/RevHotStore.cs` |

### 12. 退避等待不可取消

| 项目 | 内容 |
|------|------|
| 现象 | 玩家取消更新后，界面要白等满一次退避时长（最长可达数十秒）才真正响应 |
| 根因 | `RevTask.Delay` 不带取消令牌；清单与文件两处重试的退避都直接 `await RevTask.Delay(...)` |
| 修复 | 新增逐帧检查取消的 `BackoffDelayAsync`；同时 `BackoffMs` 先升 `long` 再移位（杜绝 int 移位溢出变负数）、单次封顶 30 秒 |
| 文件 | `Runtime/Download/RevHotDownloader.cs` |

### 13. `UpdateAsync` 配置来源隐式依赖时序

| 项目 | 内容 |
|------|------|
| 现象 | Check 与 Update 之间会话被复位（或被另一次 Check 覆盖）时，`config = _activeConfig` 为 null → 后续 NRE，错误信息是"预期外的异常" |
| 根因 | 配置来源是门面静态字段，与 `check`（差量计划的载体）没有配对关系 |
| 修复 | `RevHotCheckResult` 内部携带 `Config`（与生成计划时的配置严格配对）；`RunUpdateAsync` 优先取 `check.Config`，缺失时回退 `_activeConfig`，再缺失返回明确的 `ConfigInvalid` 人话错误；`CheckAsync(null)` 同样返回人话而非 NRE |
| 文件 | `Runtime/Core/RevHotResults.cs`、`Runtime/Facade/RevHotUpdate.cs` |

---

## 四、加固与防御（6 处）

### 14. 清单截断漏防（`@bundleCount` 校验）

上传一半 / 被 CDN 截断的非空清单会被静默接受，缺失的包被差量计算误判成"远端已删除"，**残缺版本照样打上完成标记**。修复：`Parse` 记录 `@bundleCount` 声明数，与实际解析数不符时整体失败（保持旧版本）；老清单无此行时跳过，向前兼容。
→ `Runtime/Core/RevHotManifest.cs`

### 15. `Serialize` 的 UnityCrc 列错位

UnityHash 为空时 UnityCrc 单独输出会顶到第 7 段，解析侧错认成 Hash。修复：两列必须成对输出（CRC 依赖 Hash 才有意义）。
→ `Runtime/Core/RevHotManifest.cs`

### 16. 配置校验补全

新增三条校验：`TimeoutSeconds ≥ 1`（0/负数 = 请求没有超时兜底，弱网下热更永久卡死）；`RetryBackoffMs` 非负且 ≤ 60000（负数使退避失效，过大致玩家久等）。
→ `Runtime/Core/RevHotConfig.cs`

### 17. 静态 `Progress` 事件跨 Play 残留

业务只订阅不退订时，关闭 Domain Reload 的二次 Play 会经静态事件持有已销毁的 MonoBehaviour（空引用/泄漏经典来源）。修复：`ResetForNewSession` 时 `Progress = null`，与热更包"SubsystemRegistration 阶段清所有静态状态"的既有纪律对齐。
→ `Runtime/Facade/RevHotUpdate.cs`

### 18. `WriteCurrentVersion` 目录名空值防御

`Path.GetDirectoryName` 在路径无目录段时返回空串，`CreateDirectory(空串)` 抛异常。
→ `Runtime/Pipeline/RevHotStore.cs`

### 19. WebGL 本地基线白抛异常 + 注释对齐

WebGL 上 `ReadLocalBaselineAsync` 仍会对不存在的清单副本路径 `File.ReadAllText`（必然抛异常再靠 catch 兜底），每次启动白抛一次并打误导性警告。修复：WebGL 直通返回（基线恒走内置清单）。同时把 `CheckAsync` 的"不下任何东西"注释修正为如实说明首包落地这一纯磁盘例外。
→ `Runtime/Facade/RevHotUpdate.cs`

---

## 五、修复统计

| 轮次 | 数量 | 代表问题 |
|------|------|----------|
| 第 1 轮 | 9 处 | 速度恒 0、Reset 漏清、清单截断检测、配置校验补全 |
| 第 2 轮 | 2 处 | **Android 首包零调用、差量更新后未变化包全挂（回退链）** |
| 第 3 轮 | 3 处 | WebGL 内存表丢失、强更分支未 Init、WebGL 基线异常 |
| 第 4 轮 | 3 处 | **UpdateAsync 不装钩子**、静态事件残留、注释对齐 |
| 第 5 轮 | 1 处 | WebGL 同会话二次热更拿旧表（回归闭环） |
| 第 6 轮 | 1 处 | 并发进度互相覆盖（按文件名分账） |
| 第 7 轮 | 1 处 | **取消被 WhenAll 吞掉 → 半截版本上线** |
| 第 8 轮 | 0 处 | 收敛复核：补审 RevCancellation / Demo 完整逻辑 / RevABLoader 消费端，七轮修复逐项反向自审，无新发现 |
| 第 9 轮 | 1 处 | **热更清单依赖图零消费 → 新增/变更包依赖闭包漏加载**（数据流追踪法发现，推翻第八轮收敛判定） |
| **合计** | **21 处** | 致命 6 · 严重 4 · 中等 5 · 加固 6 |

### 修复后已闭合的核心链路

```
Android 首包落地   ：Check 阶段自动落地（幂等）→ _builtin 目录 → 加载第二级回退
差量版本回退       ：当前版本 → 历史完整版本(.stamp) → 首包 → StreamingAssets
WebGL 表供给       ：首装 strict 拉 / 表变 force 重拉 / 重启补拉 / 已就位跳过
强更兜底           ：钩子 + SetActiveVersion + Reinit 后返回 Ready
钩子装载           ：InitializeAsync / CheckAsync→UpdateAsync 两条主路径都闭合
进度显示           ：并发按文件名分账，速度按两次上报真实间隔计算
```

### 真机验证建议

1. Android 真机首启 → 确认 `persistentDataPath/RevHotUpdate/_builtin/Android/` 有包落地
2. 连续热更两轮（每轮构建序号 +1）→ 第二轮更新后，确认**第一轮新增的包**仍能加载
3. WebGL/小游戏 → 重启后直接进游戏，确认热更新增资源能加载（映射表补拉生效）
4. 确认旧版本清理后最旧的回退版本被删、加载仍正常

---

## 六、遗留事项（非 Bug，不阻塞投产）

| 项 | 说明 | 建议 |
|----|------|------|
| 校验阶段无进度上报 | `RevHotState.Verifying` 有定义但校验大包时 loading 条不动 | 体验优化项，需扩展 `RevHotVerifier` 接口回调字节进度 |
| 纯 `CheckAsync` 无并发防重 | `InitializeAsync` 有 `_pending` 防双跑，`CheckAsync` 之间并发会互相踩 `_activeConfig`/`_state` | 启动流程单点调用的现实场景下无风险；业务侧若要并发调 Check 再加闸 |
| `SelfCheck` 不重算 hash | 编辑器自检只比大小与依赖闭环，不重算 sha256（产物几 GB 时全量重算太慢） | 生成与自检紧邻执行，窗口极小，保持现状 |

---

## 审查中确认无问题的部分（记录以免重复排查）

- 原子落盘纪律：`.stamp` 先于 `current.txt`、临时文件替换、`.part` 校验后 rename —— 顺序全部正确
- 换源重试时删 `.part`（不同源内容不能拼接）、服务器不支持 Range 返回 200 时的追加写损坏检测 —— 均有防护
- 框架接缝：`RevABLoader.BundlePathResolver` / `BundleCacheKeyResolver` / `DependenciesOverride`、`RevResBootstrap.ResMapOverride` 全部存在且语义匹配；热更侧 `ParseResMapText` 与框架侧 `LoadResMap` 解析规则逐字符一致（依赖解析接缝是第九轮补上的 —— 之前"清单依赖零消费"的洞见第九节第 6 条）
- `RevABLoader` 消费端：包名无扩展名加工（与热更目录文件名直接对应）；PC 分支钩子返回 null 正确回退 StreamingAssets；Android 直接 `LoadFromFile(钩子路径)`
- 取消设施 `RevCancellation`：令牌/源/回调实现干净；`RevTask` 异常传播机制完整（Promise.SetException → Awaiter rethrow），仅 `WhenAll` 聚合处需要显式传递取消（已修）
- 版本清理顺序：先写新 `current.txt` 再删旧目录，进程被杀无"指针指向已删版本"窗口
- Demo 完整性：`UseABInEditor` 自动开启（防编辑器直读假阳性）、`AllowHttp = true`、配置值全部在 `Validate` 合法范围内、`Progress` 事件订阅/退订配对、每次操作新建 CTS（无"取消后秒失败"问题）

## 收敛判定（第九轮更新）

> **诚实记录**：第八轮曾宣告收敛，第九轮换用"数据流追踪"方法（追踪一个包的依赖闭包从打包机 → CDN → 差量下载 → 运行时加载的完整链路）后，立即发现了依赖图零消费的架构级缺陷。这说明"逐文件读 + 故障模式推演"的方法对**跨文件的功能性缺失**（字段生成了但没人用）不敏感 —— 收敛判定永远与方法覆盖绑定，不存在绝对的"没有 Bug"。

第九轮后的判定依据：

1. **代码覆盖**：热更包 20 个文件全部逐行读过；四个接缝（`RevABLoader`、`RevResBootstrap`、`RevTask`/`RevCancellation`、Demo）读到消费端实现层
2. **数据流追踪**：第九轮新增的方法 —— 对"包内容""依赖闭包""映射表""版本指针"四条数据流做了端到端追踪，修复后每条流的所有环节都有明确的所有者
3. **故障模式推演**：平台（4）× 路径（首装/热更/无更新/重启/强更/取消/失败/重试/换源/清理）× 会话（首次/同会话二次/跨 Play）组合逐条推演
4. **发现密度收敛**：9 轮的问题数为 9 / 2 / 3 / 3 / 1 / 1 / 1 / 0 / 1 —— 第 8、9 轮的发现均来自**新审查方法**而非旧方法的遗漏，旧方法已充分收敛
5. **回归自审**：每轮修复都做了反向审查（第 5 轮曾抓到一次自查回归并修复）
6. **机械验证**：热更包、Demo、RevTask、RevResourceSystem 全量编译诊断零错误零警告

剩余事项仅为第六节的三个非 Bug 体验项。若要进一步保真，建议补一轮"真实数据流验证"：打一个包含"新增包 + 变更包依赖变化"的 AB 产物（现有 Demo 只覆盖"变更单包"），在编辑器 AB 模式下走完整热更 + 加载。
