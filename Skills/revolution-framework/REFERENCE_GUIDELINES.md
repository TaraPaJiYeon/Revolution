# Revolution 代码审查指南（Referece Guidelines）

> 用途：**审查入口**。按本清单逐条过一遍，输出"严重问题 → 规范问题 → 潜在风险 → 优化建议"。
> 每条都对应真实的坑（详见 `references/pitfalls.md`）与代码规范（`references/code-style.md`）。

## 0. 输出格式（固定四段）

| 段 | 内容 | 例子 |
|---|---|---|
| **严重问题** | 会导致崩溃 / 泄漏 / 线上事故 / 数据错 | 事件订阅没 owner 且对象会销毁；热更包实现成"只从持久化读"；引用计数不归零 |
| **规范问题** | 违反本框架硬规范 | 方法括号换行；`if` 不带大括号；类名缺 `Rev` 前缀；绕过门面读文件 |
| **潜在风险** | 现在能跑、边界会出问题 | 同步 `Load` 用在真机首次；`RevTimer.Paused` 当局部暂停；`force` 卸载分组 |
| **优化建议** | 更好写法，不强求 | 提局部变量让调用单行；把纯计算抽到服务以便工程外断言 |

**结论要可执行**：每条给出"改哪一行 / 改成什么"，别只说"建议优化"。

## 1. 清理与生命周期

- [ ] 每个 `RevTimer.After/Every`、`RevMono.Add*` 都有"能停"的路（`owner:` 或 `OpenScope()`），宿主销毁时有 `CancelAllOf` / `RemoveAllOf`。
- [ ] 每个 `RevEvent.AddEventListener` 都有对应移除：`RemoveAllByOwner(owner)` 或字段保存的委托（**匿名 lambda 按委托移除无效**）。
- [ ] 每个 `RevResManager.Load/LoadAsync` 都有对应 `Release`。
- [ ] 池对象：`OnPoolReturn` 清空字段；`Return` 后不再访问该对象；WebGL 用 `GetAsync`。
- [ ] 池按类型取：预制体主脚本写了 `[RevPoolPrefab]`（子类自己写、同一预制体只一个类声明）；清理区分对：`Clear*` 只清空闲实例，退出业务域用 `DestroyGroup` / `DestroyPool`（再 `Shutdown(group)`）。
- [ ] 生命周期类监听（`RevAppLifecycle.Resumed` 等）用 `owner: this` 注册，销毁时 `RemoveAllByOwner(this)`。
- [ ] `MonoBehaviour` 的 `OnEnable/OnDisable` 订阅与退订**成对且对称**（别在 `OnEnable` 订阅、在 `OnDestroy` 退订）。
- [ ] 静态字段/静态容器在关闭 Domain Reload 的场景下有复位钩子（照 `Support/*UnityHooks.cs`）。
- [ ] 服务（`RevServices`）：跟着模块 / 场景走的服务**有注销**——注册带 `owner: this`，`OnDestroy` 里 `UnregisterAllOf(this)`（没有 = 服务一直占内存）；全局服务集中在启动入口注册；没有"先试着 `Unregister` 一下再 `Register`"这种掩盖重复注册的写法。
- [ ] 服务取用：**没有把 `RevServices.GetRequired<T>()` 的结果长期存成字段**（注销后它是已 `Dispose` 的对象）；`Register<I, T>()` 的 `T` 有公开无参构造（否则用工厂重载）；`owner` 传的是引用类型（通常 `this`）。
- [ ] 服务（Builder 写法）：只在需要"每局 / 每场景各一份"或单测隔离时才用；`AddScoped` 的服务放进 `CreateScope()` 的 `using`；`AddSingleton(instance)` 的实例有人释放；`Build()` 的结果缓存进 `static readonly`；**没有为了"一行注册"这种需求去手写 `static readonly RevServiceLocator` 再链式 `Build()`**（那是 `RevServices` 的活）。

## 2. 资源（RevResourceSystem）

- [ ] 路径用 `RevResPath.*` 常量或两段式参数，**没有**自己拼 `StreamingAssets` / `dataPath` / `Resources.Load`。
- [ ] 真机首次加载用 `LoadAsync`（不是同步 `Load`）。
- [ ] 资源按域分了组（`Battle`/`UI`/`Config`/`Sound`），没有"什么都传 `Unknown`"（那样永远不会被分组卸载）。
- [ ] 没有用 `UnloadGroup(group, force: true)` 去"省事清理"（会误伤别的域共享资源）。
- [ ] 新增资源打了 AB 标记，并重新生成了 `ResMap.txt` / `RevResPath.cs`。
- [ ] 失败分支看的是 `handle.ErrorReason`（不是 `if (content == null)` 猜）。

## 3. UI（RevUISystem）

- [ ] 面板有 `[RevUIPanel(root, layer)]`，root 用常量；`[RevBind]` 字段名与节点名对得上。
- [ ] `OnBindView` 只装配、`OnRefreshView` 只落屏、`OnClick` 只转发（没有在视图钩子里做业务/发请求）。
- [ ] 有数据的面板走 `SetData` 单向流；`OnRefreshView` 只读 `Data`，且用 `Data` 前**判空**（`SetData(null)` / 回池复用会清成 null）。
- [ ] **刷新时机**：原地改了数据内容（`Data.X++` / `Items.Add`）→ 有手动 `RefreshView()`；换数据走 `SetData`；`OnDataChanged` 里**没有**多余的手动 `RefreshView()`（会画两遍）；`struct` 数据没有"原地改 Data"的写法（读到的是拷贝）。
- [ ] `SetData` / `Open<T,TData>` 写在**业务侧数据产生处**（点击回调 / 网络回包），不是写在面板里。
- [ ] 控件事件：控件名与节点名一致（写错装配时就报错）；高频事件（滚动 / 每次敲键）用重写回调而不是方法特性；同一件事不在"重写回调"和"方法特性"两处都做。
- [ ] 动画：传了 `owner: this`；重写 `PlayOpenTransition` / `PlayCloseTransition` 的在结束时调了 `onDone`。
- [ ] 打开用 `Open<T>(onOpened)` / `OpenAsync<T>`；没有在真机路径上用"同步 `Open<T>()` 并假设它会加载"。
- [ ] 弹窗/常驻 HUD 正确设置 `InBackStack`；`ExclusiveGroup` 按语义设置；`RevUICacheMode` 与 `KeepAlive` 一致。
- [ ] 面板里没有"跨界面复用"的重逻辑（该抽成服务）；面板没有缓存未在 `OnReuse` 里重置的状态。

## 4. 异步（RevTask）

- [ ] 没有引入 `System.Threading.Tasks.Task` / 协程写业务（协程仅用于桥接旧代码）。
- [ ] `async void` 只出现在事件/按钮入口，且**整体 try/catch**（否则异常静默）。
- [ ] 关键节点有 `token.ThrowIfCancelled()`；宿主销毁 / 切场景能取消。
- [ ] 不 await 的任务显式 `Forget()`；没有在异步里 `new` 大对象 / 做重活（该分帧）。
- [ ] 没有依赖"续体立即全部执行"（每帧限量 128）。

## 5. 事件 / 时间 / 输入

- [ ] 事件名来自常量类；同一事件名只有一种签名；派发返回值 `0` 时有提示（`LogNoListener` 或显式 Warn）。
- [ ] 派发/回调里没有重活、没有跨线程调用。
- [ ] 计时器时间域正确：UI 倒计时/超时用 `Unscaled`；`At(...)` 前有 `SyncServerTime`。
- [ ] 输入：用到的 action 都 `Bind` 过（未绑定会静默返回 false）；换场景 / 打开弹窗时 `ResetAll` 或 `OpenScope().Block()`。

## 6. 音频 / 状态机 / 动作序列 / GM / 数据表

- [ ] 音效：3D 播放传了 target；接了 `Failed` / `VoiceFinished`（框架自身不打日志）；逻辑名与路径不一致时先 `Register`。
- [ ] 状态机：状态实例注册复用（不是每次 new）；没有在 `OnEnter/OnExit` 里再切状态（防重入）；异步切换后检查了 `CurrentState`。
- [ ] 动作序列：蓝图静态缓存只 `Build` 一次；没有把状态存在构建期字段；占用资源在 `Finally` / `OnCancel` 归还。
- [ ] GM：注册集中在一处；命令名不含空白字符、不是只有分隔符；参数用 `args.Int/Float/Bool/Str/Enum` 取；业务拒绝抛 `RevGMUsageException`（不静默 return）。
- [ ] 配置表：走 `RevDataTableManager`（不直接读文件）；字符串取表用 `TableName`（不是类名）；失败分支处理了 `RevResLoadErrorReason`。

## 7. 热更（如果项目装了扩展包）

- [ ] `InitializeAsync` 放在"加载任何业务资源"之前；`UpdateAsync` 用 `CheckAsync` 的原结果。
- [ ] 资源包语义是**覆盖式**（持久化优先 → 回退 StreamingAssets），不是"只从持久化读"。
- [ ] 上传顺序：内容先、清单最后；清单路径 no-cache、内容路径 immutable。
- [ ] 远端目录带大版本段；`@appVersion` 与 `Application.version` 一致。
- [ ] 替换包时先 `ShutdownAll()` → 换文件 → `Init()`（不在包被使用时覆盖文件）。

## 8. 编辑器 / 真机差异

- [ ] 只用编辑器直读验证过的逻辑，是否在 AB 模式下也成立（必要时开 `UseABInEditor` 复验）。
- [ ] 平台相关代码没有硬编码 `PC`（编辑器平台的默认值）；热更/AB 目录段与 `ABBuildSetting.GetPlatformName` 对齐。
- [ ] 生成物（`RevResPath.cs` / `ResMap.txt` / `RevSoundPath.cs`）没有被手改。

## 9. 代码规范（本项目硬规范）

- [ ] 没有"以 `(` 结尾"的行（括号后换行）；没有 `( 参数` 的多余空格。
- [ ] 所有 `if/for/foreach/while` 都带大括号。
- [ ] 类名 `Rev` 前缀；文件名 = 类名；命名空间正确。
- [ ] 新文件有文件头"为什么"段；关键/易错处有 `★` 注释（解释为什么，不复述代码）。
- [ ] 新增资产带 `.meta`；文件夹 meta 是**同级**命名。
- [ ] 没有新增分层 / 第二套 API（`System` / `BusinessLogic` / 自己的资源加载入口）。
- [ ] 文档（md + html）与 README 统计数字已同步。

## 10. 启动 / 生命周期 / 真机适配（RevInitialize · RevAppLifecycle · 代码裁剪）

- [ ] 启动入口**只调一次** `RevInitialize.Initialize()`；没有直接调 `RevResBootstrap.Instance.Init()`，也没有"手动初始化一圈模块"。
- [ ] 异步入口（`async void`）整体 `try/catch` + `RevLog.Exception`；全局服务集中在启动入口 `RevServices.Register<…>()`（一行一个）。
- [ ] 切后台 / 回前台用 `RevAppLifecycle` 事件，**没有**自己写 `OnApplicationPause` / `OnApplicationFocus`；长连接 / 计时 / 体力类逻辑按 `LastBackgroundSeconds` 处理了回前台；数据**随存随写**（不依赖 `Quitting`）。
- [ ] 小游戏 / WebGL 目标：没有 `Thread` / `Task.Run` / `System.Threading.Tasks.Task`；没有同步 `Load` / 同步 `Open<T>()` 的假设；音频首次播放有用户手势；热更用 https。
- [ ] 代码裁剪：已确认 `Managed Stripping Level`（主推 `Disabled`）；开了裁剪的已生成 `Assets/link.xml` 并看过真机 `RevStripGuard` 输出；`RevStripGuard` 的 `WatchedTypes` 新增条目保持**字符串写法**（不直接引用被检查类型）。
- [ ] 平台名对齐：小游戏目标产物目录 / 主包名 / 热更平台段都归 `WebGL`；编辑器里验证热更前 `UseABInEditor = true`。

## 11. 对照示例（反例 → 正例）

### 例 1：事件订阅

```csharp
// ✗ 匿名 lambda：无法按委托移除；对象销毁后回调仍被调用
RevEvent.AddEventListener<int>("Hp.Change", v => hpBar.Set(v));

// ✓ 存字段 + owner，销毁时一行清掉
private Action<int> _onHpChange;
private void Awake() { _onHpChange = OnHpChange; RevEvent.AddEventListener("Hp.Change", _onHpChange, owner: this); }
private void OnDestroy() { RevEvent.RemoveAllByOwner(this); }
```

### 例 2：计时器

```csharp
// ✗ 没留"能停"的路 + 时间域选错（暂停游戏后倒计时冻结）
RevTimer.Every(1f, () => countdown.text = (--sec).ToString());

// ✓ owner 可整体取消 + UI 倒计时用 Unscaled
RevTimer.Every(1f, () => countdown.text = (--sec).ToString(), owner: this, domain: RevTimeDomain.Unscaled);
```

### 例 3：资源加载

```csharp
// ✗ 绕开资源系统：没缓存、没引用计数、真机路径不对、热更无处落脚
string path = Application.streamingAssetsPath + "/UI/icon.png";

// ✓ 逻辑路径 + 分组 + 异步
RevResManager.LoadAsync<Sprite>(RevResPath.Data, "UI_Icon", s => image.sprite = s, RevResGroup.UI);
```

### 例 4：失败可见

```csharp
// ✗ 静默返回：使用者永远不知道"为什么没反应"
if (cfg == null) { return; }

// ✓ 带原因：句柄字段 / 抛出人话异常 / GM 面板显示
if (cfg == null) { throw new RevGMUsageException("配置未加载：先执行 工具/重载配置"); }
```

### 例 5：热更语义

```csharp
// ✗ 只认持久化目录：新装机 / 清数据后全部 BundleLoadFail（首屏黑）
return Path.Combine(localRoot, bundleName);

// ✓ 覆盖式：持久化有就用、没有回退首包、都没有才交还框架默认
if (File.Exists(local)) { return local; }
return null;      // null = 让框架走 StreamingAssets/<平台>/<包名>
```

### 例 6：UI 数据刷新

```csharp
// ✗ 原地改了内容却指望界面自己动（框架不监听数据对象）；在 OnDataChanged 里又手动刷导致画两遍
Data.Count++;
protected override void OnDataChanged(Detail o, Detail n) { RefreshView(); }

// ✓ 原地改后手动刷；或换新对象走 SetData（面板开着自动刷，OnDataChanged 只做增量）
Data.Count++; RefreshView();
SetData(new Detail { Id = Data.Id, Count = Data.Count + 1 });
protected override void OnRefreshView() { if (Data == null) { return; } _count.text = Data.Count.ToString(); }
```

### 例 7：切后台

```csharp
// ✗ 自己写 OnApplicationPause / OnApplicationFocus：移动端两个回调前后脚到达，重连逻辑被触发两遍
private void OnApplicationPause(bool pause) { if (!pause) { Reconnect(); } }
private void OnApplicationFocus(bool focus) { if (focus) { Reconnect(); } }

// ✓ 订阅框架事件：只在"前后台状态真翻转"时派发一次，且能拿到后台时长
RevEvent.AddEventListener(RevAppLifecycle.Resumed, OnResumed, owner: this);
private void OnResumed() { if (RevAppLifecycle.LastBackgroundSeconds > 60f) { Reconnect(); } }
```

### 例 8：启动与小游戏异步

```csharp
// ✗ 到处手动初始化 + 用 Task 写异步（小游戏单线程会直接崩）+ async void 不兜异常
RevResBootstrap.Instance.Init();
private async void OnStart() { await System.Threading.Tasks.Task.Delay(100); await Load(); }

// ✓ 只调一次 RevInitialize；异步用 RevTask；async void 入口整体 try/catch
RevInitialize.Initialize();
private async void OnStart()
{
    try { await RevTask.Delay(100); await Load(); }
    catch (Exception e) { RevLog.Exception(e, "启动失败", "Boot"); }
}
```
