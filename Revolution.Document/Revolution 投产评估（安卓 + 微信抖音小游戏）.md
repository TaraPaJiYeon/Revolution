# Revolution 投产评估 —— 安卓 / 微信小游戏 / 抖音小游戏

> 评估日期：2026-10-07
> 评估范围：`RevolutionFrameWork_Unity/Assets/Revolution`（框架本体）、`Assets/Revolution.HotUpdate`（热更扩展包）、`Editor/RevResourceSystem/ABTool`（打包工具链）
> 评估方式：全量静态扫描（多线程 / 文件 IO / 反射 / 平台宏 / 网络 / 引擎 API 六条线）+ 逐文件精读关键路径
> 行号来自本次扫描，改动前请以实际文件为准。

---

## 〇、修复状态（2026-10-07 已按本报告执行）

| 条目 | 状态 | 落地位置 |
|---|---|---|
| P0-1 RevFileSink 线程门禁 | ✅ 已修 | `RevFileSink.SupportsBackgroundWriter`（WebGL / 小游戏默认禁用，并把"平台不支持"明确报到 `RevLog.SinkErrors`） |
| P0-2 IL2CPP 裁剪保护 | ✅ 已修 | 框架侧新增 `Assets/Revolution/link.xml`；新增编辑器工具 `Revolution.Tools/平台/生成 IL2CPP 裁剪保护 (link.xml)`（扫业务派生类 → 生成 `Assets/link.xml`，不静默覆盖手写文件） |
| P0-3 平台名三处对齐 | ✅ 已修 | `RevABLoader.MainName` / `RevHotPlatform.Name` / `ABBuildSetting.GetPlatformName` 统一把 `XxxMiniGame` 归一到 `WebGL`（渠道段不硬编码，交由 `BundlePathResolver` 钩子按需处理） |
| P1-1 AB 重试 + 超时 + hash/crc | ✅ 已修 | `RevABLoader.MaxAttempts / RetryDelayMs / AttemptTimeoutSeconds` + 新钩子 `BundleCacheKeyResolver` + 新类型 `RevBundleCacheKey`；热更侧 `RevHotStore.ResolveBundleCacheKey` 从清单注入 `unityHash` / `unityCrc`，并在切版本时同步 |
| P1-2 首包 Resources 审计 | ✅ 已修（以"审计工具"落地） | 新增 `Revolution.Tools/平台/首包体积审计 (Resources)`；策略链未改（Resources 兜底是刻意设计，动它会波及既有工程） |
| P1-3 小游戏音频 | ✅ 已修（能力表 + 文档） | 新增 `RevSoundPlatform`（`AudioManagedByPlatform` / `NeedsUserGestureUnlock` / `Note`），三条约束写进代码注释 |
| P1-4 生命周期广播 | ✅ 已修 | 新模块 `RevAppLifecycle`（`Paused` / `Resumed` / `Quitting` + 状态查询）+ 隐藏宿主 + 《使用说明》md & html |
| P1-5 热更并发 / 明文 HTTP | ✅ 已修 | `RevHotConfig.Concurrency` 按平台给默认（小游戏 2）；`Validate()` 在小游戏上禁明文 http、并发 > 4 报错 |
| P2 Deterministic / StripUnityVersion | ✅ 已修 | `ABBuildConfig.deterministic`（默认开）/ `stripUnityVersion`（默认关）+ 打包窗口勾选框 |
| P2 Debug / Release 构建区分 | ⏭ 未做 | 会改变产物目录结构（波及既有 CI 与远端路径约定），需先定目录规范再动 |
| P2 出包链路（BuildPlayer / 切平台） | ⏭ 未做 | 依赖"小游戏出包路线"决策（见第五节），定了再做 |
| P2 首包体积门禁（超预算即失败） | ⏭ 未做 | 先以"审计工具 + 报告"落地（避免误阻断构建）；等预算数字明确后再加硬门禁 |
| P2 热更下载类内部守卫等 4 条加固 | ⏭ 未做 | 属"加断言"的加固项，不影响投产，后续按需补 |

> 本次改动总量：**Runtime +4 个文件**（`RevAppLifecycle` 2 个 / `RevSoundPlatform` / `RevBundleCacheKey`）、**Editor +2 个文件**（`RevPlatform` 工具）、**文档 +2 份**（应用生命周期使用说明 md & html）；
> README 的模块数与规模统计已同步为 **18 个模块 / 183 个 `.cs` / 30,130 行**。

---

## 一、结论

**不能直接投产，但离得不远 —— 架构不用推翻，问题集中在少数几处。**

先给四个"已经做对了"的判断，这是能不能上小游戏的**决定性设计**，它们全部正确：

| 关键设计 | 现状 | 为什么这是对的 |
|---|---|---|
| 异步内核 | `RevTask` 是自研**单线程** Promise，零 `Thread` / `Task.Run` | 小游戏无多线程，用 `Task` 会直接崩 |
| AB 加载分派 | WebGL 分支走 `UnityWebRequestAssetBundle`，同步 `LoadFromFile` 被 `#if UNITY_WEBGL` 挡住 | 小游戏无本地文件系统，读文件必失败 |
| 热更平台能力表 | `RevHotPlatform.SupportsLocalFiles / IsWebGL / NeedsBuiltinCopy` 按平台分流 | 文件型（安卓）与 URL 型（小游戏）本就是两套语义 |
| GM / 状态机 | GM 注册表在 Editor 侧，状态机显式 `new` 不用 `Activator` | 提前规避了 IL2CPP 反射构造，AOT 安全 |

**平台就绪度分级：**

| 目标平台 | 就绪度 | 一句话 |
|---|---|---|
| 安卓 | 🟡 接近可用 | 框架侧只差裁剪保护与网络健壮性；出包链路要另搭 |
| 微信小游戏 | 🔴 需改造 | P0 三项全中 + 音频/生命周期适配 |
| 抖音小游戏 | 🔴 需改造 | 同微信，另需补"渠道维度"（当前与微信共用 `WebGL/` 目录） |

---

## 二、P0 阻塞项（不修就上不了线）

### P0-1　`RevFileSink` 的后台线程在小游戏上会崩，且现有门禁拦不住

**证据**：`Runtime/RevLog/Implementation/RevFileSink.cs`
- `:49` `private readonly AutoResetEvent _signal = new AutoResetEvent(false);`
- `:68-69` `_thread = new Thread(Loop) { ... }; _thread.Start();`
- `:103` `while (...) Thread.Sleep(1);`　`:120` `_thread.Join(1000);`

**为什么现有保护无效**：`Enabled = ProbeDirectory()`（`:64`）只探测"目录能不能写"。WebGL/小游戏下 `persistentDataPath` 映射到 IDBFS 虚拟文件系统，**试写会成功**，于是 `Enabled = true`，紧接着就执行 `new Thread(...)` → 小游戏上抛 `PlatformNotSupportedException`，**启动即崩**。

**触发条件**：`RevLog.EnableFileLog(...)`（`Facade/RevLog.cs:98`）。框架默认不启用，所以是"随时可能踩响的地雷"而非必炸 —— 但一旦有人为了抓真机日志打开它，就是白屏。

**改法**（二选一，建议都做）：
1. `#if UNITY_WEBGL` 下让 `RevFileSink` 直接返回"不可用"（不建线程），并在 `RevLog.SinkErrors` 里明确报"WebGL/小游戏不支持文件日志"；
2. 保留线程版给安卓，安卓侧零改动。

> 参考量级：`RevFileSink.cs` 改动约 10 行。

---

### P0-2　UI 反射绑定链无裁剪保护 —— 一旦开 IL2CPP 裁剪，UI 会"静默全废"

**证据**（全链路无 `link.xml`、无 `[Preserve]`；全仓库搜 `Preserve|UnityEngine.Scripting` 仅 1 处误报）：
- `Runtime/RevUISystem/Support/RevUIBindPlan.cs:155-166` `t.GetFields(BindingFlags.Instance|Public|NonPublic|DeclaredOnly)` + `field.GetCustomAttributes(typeof(RevBindAttribute), false)`
- `Runtime/RevUISystem/Support/RevUIBinder.cs:127` 由 `FieldInfo.SetValue` 装配控件引用
- `Runtime/RevUISystem/Support/RevUIPanelMeta.cs:141,186` `type.GetCustomAttributes(...)` 解析面板/Part 元数据
- `Runtime/RevUISystem/Core/RevUIWidgetEvents.cs:307` `h.Method.Invoke(target, ...)`（**每次控件事件**一次反射调用）
- `Runtime/RevSingleton/RevSingleton.cs:49-63` `GetConstructor(...) + ctor.Invoke(null)`

**为什么危险**：Unity 的 Managed Stripping Level 默认较低时不裁用户程序集，所以现在没事。但**小游戏为了压代码包，官方转换工具会建议调到 Medium/High** —— 那一刻开始：
- `[RevBind]` 字段被裁 → 控件引用全 `null` → 面板空白；
- 面板特性被裁 → `RevUIPanelMeta.TryResolve` 失败 → 面板打不开；
- 单例构造被裁 → `InvalidOperationException("没有无参构造函数")`。

**症状层面更麻烦**：它是"真机才出现、编辑器全好的静默失效"，排查成本极高。

**改法**（推进前必做）：
1. 加 `link.xml`，保留 `RevUIPanel` / `RevUIPart` 的全部派生类型与 `RevSingleton<T>` 派生类型；
2. 或在 `RevUIBindPlan` 反射入口对 `targetType` 用 `[Preserve]`（`UnityEngine.Scripting.PreserveAttribute`，可加到类上）；
3. 顺带把 `RevUIWidgetEvents` 的反射事件改为**优先走接口重写**（框架本身已支持两条路，`Core/RevUIWidgetEvents.cs:44` 自己写了"低频用特性、高频用重写"）—— 高频控件（滑条、输入框）每次事件一次 `MethodInfo.Invoke` 有 GC 分配，小游戏 iOS 上会体现为掉帧。

> 备注：AOT 泛型风险本身很低 —— 全仓 `MakeGenericType` / `Activator.CreateInstance` / `Emit` / `unsafe` **0 命中**。真正的敌人只有"裁剪"。

---

### P0-3　平台/渠道维度缺失：微信与抖音会撞在同一个目录里

**证据**：
- 运行时平台名：`Runtime/RevResourceSystem/Implementation/Loaders/RevABLoader.cs:72-86`（`UNITY_IOS→iOS` / `UNITY_ANDROID→Android` / `UNITY_WEBGL→WebGL` / `else PC`）、`Assets/Revolution.HotUpdate/Runtime/Core/RevHotPlatform.cs:29-43`（同规则）
- 打包侧平台名：`Editor/RevResourceSystem/ABTool/Core/ABBuildSetting.cs:75-87`
  ```csharp
  default:
      return target.ToString();          // iOS / Android / WebGL ...
  ```
- 打包平台下拉只有 6 项：`ABBuildSetting.cs:132-140`（无任何小游戏目标）
- 全仓搜 `WEIXINMINIGAME|BYTEDANCE|MINIGAME|WECHAT` → **0 命中**

**两条后果，取决于你走哪条路**：

| 出包路线 | 平台名是否一致 | 后果 |
|---|---|---|
| **A. Unity WebGL 构建 + 官方小游戏转换工具**（当前代码假设的路线） | 一致（两边都是 `WebGL`） | 能跑，但**微信与抖音共用同一个 `WebGL/` 远端目录**：两个渠道的 AB 无法分开部署，一个渠道发版会污染另一个 |
| **B. 引擎原生小游戏构建目标**（团结引擎 `WeixinMiniGame` / `ByteDanceMiniGame`） | **不一致** | 打包侧产出目录 = `target.ToString()`（如 `WeixinMiniGame`），运行时 `MainName` 走 `#if UNITY_WEBGL` 返回 `WebGL` → **主包名与目录名对不上，首屏全 404（BundleLoadFail）** |

**改法**（先做路线决策，再改 3 处）：
1. 在 `RevHotPlatform` / `RevABLoader.MainName` / `ABBuildSetting.GetPlatformName` 三处**同时**加小游戏分支，并让 `CommonBuildTargets`（`ABBuildSetting.cs:132`）放出对应目标；
2. 平台名里引入**渠道段**，例如 `WebGL-Weixin` / `WebGL-Douyin`，远端目录随之分渠道；
3. 参考已有能力钩子的形状（`RevHotPlatform.cs:56-73` `SupportsLocalFiles` / `NeedsBuiltinCopy`）——**本文件的平台判断方式已经是正确形状，照它补分支即可，不要另起一套**。

---

## 三、P1 必补项（投产前应完成）

### P1-1　AB 加载无超时、无重试、不传 hash/crc

**证据**：`RevABLoader.cs:466-484`
```csharp
using (UnityWebRequest www = UnityWebRequestAssetBundle.GetAssetBundle(ResolveBundlePath(abName)))
{
    await www.SendWebRequest();
    if (www.result != UnityWebRequest.Result.Success) return null;   // 一次失败即 BundleLoadFail，无重试
    return DownloadHandlerAssetBundle.GetContent(www);
}
```

三个问题：
1. **无重试**：热更模块有"多源降级 + 指数退避"（`RevHotDownloader.cs:259-296`），但 AB 直连加载这一层**一次失败就放弃**。小游戏网络抖动是常态，首屏加载失败率会明显偏高。
2. **无超时兜底**：WebGL/小游戏底层是 XHR，`UnityWebRequest.timeout` 在部分版本不生效。建议用 `RevTimer` 计时 + `www.Abort()` 做兜底。
3. **不传 hash/crc**：清单里**已经生成了** `unityHash` / `unityCrc` 两列（`RevHotManifestBuilder.cs:94-99`、`RevHotManifest.cs:214-216`），`RevHotBundleInfo.cs:12` 也明确写了"小游戏/WebGL 上要交给 `GetAssetBundle(url, hash, crc)`"——**但运行时唯一的消费点没传**。后果：**引擎的 IndexedDB/小游戏文件缓存不生效，玩家每次进游戏重新下载全部 AB**（流量 + 加载时长双输）。

**改法**：`GetAssetBundle(url, hash, crc)` 三参重载（清单里现成的两列直接用），加超时与 2~3 次退避重试。这是本清单里**性价比最高的一条**。

---

### P1-2　首包体积：三处 `Resources.Load` 会强制进包

**证据**：
- `Runtime/RevUISystem/Implementation/RevUIRoot.cs:105` `Resources.Load<GameObject>(canvasPath)`（UI 根 Canvas）
- `Runtime/RevUISystem/Implementation/RevUIRoot.cs:398` `Resources.Load<GameObject>(path)`
- `Runtime/RevResourceSystem/Support/RevResBootstrap.cs:124` `Resources.Load<TextAsset>("ResourceSystem/ResMap")`
- `Runtime/RevResourceSystem/Implementation/Loaders/RevResourcesLoader.cs:17,24`（存在一条 Resources 兜底加载策略）

`Resources/` 下的内容**无法分包、强制进首包、且是同步加载**。微信小游戏首包预算是硬约束（4MB 代码包 / 20MB 总包，转换工具另有 CDN 分包机制）。

**改法**：
1. 审计这三个入口的内容物大小，把"UI 根 Canvas"改为 AB 加载（框架已支持 `RevResManager.LoadAsync`，改的是调用点）；
2. ResMap 文本（几十 KB）留在 `Resources` 可接受，但要在文档里写明"这是唯一允许进 Resources 的东西"；
3. 确认 `RevResourcesLoader` 在真机策略链里**未被启用**（编辑器默认只注册 `RevEditorResPolicy`，这条要写成断言或启动期自检）。

---

### P1-3　小游戏音频：`AudioClip` 需要专项适配

**证据**：`Runtime/RevSoundSystem/Implementation/RevSoundAssets.cs`
- `:136` `RevResManager.LoadAsync<AudioClip>(root, name, ...)`
- `:60-67` 自建 `RevPoolCore<AudioSource>`，`:67` `go.AddComponent<AudioSource>()`
- 全仓 `UnityWebRequestMultimedia` **0 命中**

**问题**：小游戏上 `AudioClip` 的可用性取决于**导入设置**（必须是 `Decompress On Load`、且避免平台不支持的压缩格式），否则从 AB 加载出来是"哑的"；且 iOS 上**首次播放必须由用户手势解锁**，小游戏还需处理"音频中断"（来电/切后台）事件。

**改法**：框架不必自己接 WX SDK，但应该**留一个后端抽象**（类似 `RevHotPlatform` 的能力表：`IRevSoundBackend` / `RevSoundPlatform.SupportsAudioSource`），把解锁与中断交给宿主适配层。同时补一篇《小游戏音频导入设置》文档（这条最容易在真机上"没声音但无报错"）。

---

### P1-4　应用生命周期：只有输入模块处理了切后台

**证据**：全 Runtime 搜 `OnApplicationPause|OnApplicationFocus` → 仅 `Runtime/RevInput/Support/RevInputDriver.cs:60-67`（失焦/切后台时 `RevInput.ResetAll`）

**缺什么**：小游戏切后台会带来——音频中断、网络连接失效、时间跳变（`Time.deltaTime` 受 `maximumDeltaTime` 限制，最多 0.333s，所以计时器风险可控，但**长连接/轮询类逻辑会静默失效**）。框架侧应有统一的"前后台事件广播"（用 `RevEvent` 发一条 `App.Pause/Resume` 即可），业务据此暂停音频、重连、校验资源。

**改法**：加一个 `RevAppLifecycleDriver`（MonoBehaviour，照 `RevInputDriver` 的形状写），把 `OnApplicationPause/Focus` 转成 `RevEvent` 广播 —— 约 40 行，是所有业务都要用的地基。

---

### P1-5　热更并发数与"合法域名/跨域"约束未落地

**证据**：
- `Assets/Revolution.HotUpdate/Runtime/Core/RevHotConfig.cs:119` `Concurrency = 3`（`:191` 只校验 1~8）
- 技术方案文档明确要求小游戏 **1~2 并发**（微信 `wx.request` 有并发上限），且需 CORS / 缓存头 / 合法域名配置（`Revolution.Document/热更新/RevHotUpdate技术方案.md:512-515, 1401-1407`）
- 代码里只有 `AllowHttp` / `ExtraHeaders` 两个笼统开关，**无任何小游戏约束校验**

**改法**：按平台给默认并发（小游戏 2、安卓 3），启动期对"明文 HTTP + 小游戏"报一条显式错误（不是静默通过）。

---

## 四、P2 优化清单（按性价比排序）

| # | 项 | 证据 | 收益 |
|---|---|---|---|
| 1 | AB 构建缺 `Deterministic` / `StripUnityVersion` | `ABBuildConfig.cs:263-283` | 跨机可复现 + 减小包体/利于缓存命中 |
| 2 | 无 Debug/Release 构建区分 | `ABBuildConfig.cs:98-116` | 出包期少一次人工失误 |
| 3 | 无首包体积门禁 | `ABManifestWriter.cs:54-74` 只记 size | 小游戏首包是硬约束，应做成"超预算即构建失败" |
| 4 | 完全没有出包链路（只有 AB） | 全仓 `BuildPlayer|BuildPlayerOptions` **0 命中**；无切平台菜单 | 安卓 APK / 小游戏代码包目前只能手动出 |
| 5 | 热更文档已设计、代码未落地 | `CoreBundles` / `IRevHotSink` 仅存在于 `技术方案.md:643, 17.5` | 要么实现，要么在文档里标注"未实现"，避免误用 |
| 6 | 热更下载类内部零守卫 | `RevHotDownloader.cs:304,305,327`、`RevHotVerifier.cs:47,72` 只靠 Facade 门禁 | 加类内断言，防越权直调 |
| 7 | `RevHotStore.cs:457-459` `File.ReadAllText` 弱守卫 | 靠 `StartsWith("http")` 判断 | 改用 `RevHotPlatform.SupportsLocalFiles` |
| 8 | 用字符串反射找 `InputSystemUIInputModule` | `RevUIRoot.cs:296-305` `Type.GetType` + 遍历 `AppDomain.GetAssemblies()` | IL2CPP 下慢且脆弱，建议改直引类型 |

---

## 五、平台适配路线建议（**需要你先拍板**）

**路线 A（推荐近期使用）：Unity WebGL 构建 + 微信/抖音官方小游戏转换工具**
- 优点：现有 `#if UNITY_WEBGL` 分支**全部有效**，改造成本最低；
- 必须补：渠道段（P0-3）、CDN 合法域名与缓存头（P1-5）、首包审计（P1-2）。

**路线 B（长期）：引擎原生小游戏构建目标**
- 必须先补 `UNITY_WEIXINMINIGAME` / `UNITY_BYTEDANCE_MINIGAME` 分支与打包侧映射（P0-3），否则 404；
- 好处：能直接用引擎提供的平台能力（分包、音频、登录）。

> 结论：两条路不冲突 —— **P0-3 的改法在两条路下都是必需的**，先改不亏。

---

## 六、建议的推进顺序

| 批次 | 内容 | 粗略工作量 |
|---|---|---|
| **第 1 批（P0）** | P0-1 线程门禁、P0-2 link.xml + `[Preserve]`、P0-3 平台/渠道名三处对齐 | 0.5~1 天 |
| **第 2 批（P1）** | P1-1 AB 重试 + hash/crc（收益最大）、P1-4 生命周期广播、P1-5 并发与域名 | 1~1.5 天 |
| **第 3 批（P1）** | P1-2 首包审计与改造、P1-3 音频后端抽象 + 文档 | 1~2 天 |
| **第 4 批（P2）** | 打包工具增强（Deterministic / Debug-Release / 首包门禁）、出包链路 | 按需 |

**真机验证清单**（改完必跑，编辑器全绿不代表能上线）：
1. 安卓 IL2CPP + **把 Stripping Level 调到 High** 出包 → UI 面板能否打开、按钮能否响应（验 P0-2）；
2. 微信小游戏真机 → 首屏 AB 下载、断网重连、二次进入是否走缓存（验 P1-1）；
3. 小游戏真机 → 音效能否播放、切后台恢复后音频/网络是否正常（验 P1-3、P1-4）；
4. 小游戏真机 → 整包体积与首包体积是否在预算内（验 P1-2）。

---

*本报告为评估结论，未改动任何框架代码。*
