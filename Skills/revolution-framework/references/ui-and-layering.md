# UI 与分层（RevUISystem 专题 + "要不要 System / BusinessLogic"）

> 权威文档：`Revolution.Document/UI系统/UI系统使用说明.md`、`UI系统架构解析.md`（**第十章**专门回答分层问题）。

## 1. UI 主链路（声明 → 绑定 → 打开 → 传数据 → 关闭）

```csharp
// ① 声明：特性写在面板类上（root 必填；name 省略 = 用类名）
[RevUIPanel(RevResPath.Data, RevUILayer.Normal, ExclusiveGroup = "BagTab")]
public sealed class BagPanel : RevUIPanel<BagData>
{
    [RevBind] private Button _btnClose;                 // 字段名 ↔ 节点名
    [RevBind("Top/Title")] private Text _title;         // 显式路径

    protected override void OnBindView() { }            // 只装配：找节点、挂事件
    protected override void OnRefreshView() { }         // 只落屏：把 Data 画上去（带数据面板必实现）
    protected override void OnDataChanged(BagData o, BagData n) { }   // 增量刷新（可选）
    protected override void OnClick(string path) { }    // 只转发：按钮点了该干什么
}

// ② 打开 / 传数据（业务侧）
RevUI.Open<BagPanel, BagData>(new BagData { UseCellLayout = true });     // 带数据（推荐）
RevUI.Open<BagPanel>(p => p.SetData(default));                            // 或者回调式
await RevUI.OpenAsync<BagPanel, BagData>(data);                           // await 版

// ③ 关闭
RevUI.Close<BagPanel>();      RevUI.Back();      RevUI.CloseAll(RevUILayer.Popup);
```

- 数据流是**单向**的：业务 `SetData` → 基类 `OnDataSet(object)` → 强类型 `Data` → `OnDataChanged(old, new)` → 可见则 `RefreshView()`。
- 带数据的 `RevUIPanel<TData>` 把 `OnRefreshView` 定成 **abstract**：有数据就必须写清"数据怎么落到界面上"，不允许糊过去。
- 复用时要连数据一起清（框架有 `InternalClearData`），否则会出现"上次的数据还在"。

### 1.1 刷新时机与 `SetData` 调用权（新手最易混，务必讲清）

**`SetData` 内部顺序**（源码 `RevUIPanel.SetData`）：① `Data` / `DataObject` 同步成新值 → ② `OnDataChanged(旧, 新)`（此时 `Data` 已是新值）→ ③ 面板为 `Opened` / `Opening` 才自动 `RefreshView()`（没开着不画，等打开那次统一画）。所以 `OnRefreshView` / `OnDataChanged` 里读 `Data` **一定是最新值**。

**`OnRefreshView` 什么时候被调**（框架**不监听**数据对象内容，不是双向绑定）：

| 场景 | 自动刷新？ | 你该做什么 |
|---|---|---|
| 打开面板（首次 / 池里复用） | ✅ | `OnRefreshView` 写好"怎么画 Data" |
| 面板开着时 `SetData(新对象)` | ✅ | 什么都不用做 |
| 对已开面板再 `RevUI.Open<T,TData>(新数据)` | ✅（内部走 `SetData` + 置顶） | 同上 |
| 面板没开就 `SetData`，之后才 `Open` | ✅（打开时统一画一次） | 同上 |
| **原地改内容**：`Data.Count++` / `Data.Items.Add(..)` | ❌ **不会** | **改完手动 `RefreshView()`** |

```csharp
Data.Count++; RefreshView();                                  // 写法 A：原地改 → 手动刷（class 数据才走得通）
SetData(new DetailData { Id = Data.Id, Count = Data.Count + 1 });   // 写法 B：换新对象 → 自动刷 + OnDataChanged 增量
```

- **`struct` 数据**：`Data` 每次读是**拷贝**，"原地改 + RefreshView()"走不通，只能换新对象走 `SetData`（好处：数据不会被外部偷偷改）；**`class` 数据**可原地改，但改完必须手动刷，且 `SetData` 传同一引用时 `old == new`。
- **`OnDataChanged` 里不要手动 `RefreshView()`**：面板开着时框架随后会自动刷一次，手动再刷 = 每次换数据画两遍。起步只实现 `OnRefreshView`（全量重画）就完全正确，`OnDataChanged` 增量是后期优化。
- **`SetData(null)` 与回池复用都会把 `Data` 清成 null**：`OnRefreshView` 里用 `Data` 前先判空。
- **`OnRefreshView` 纪律**：只画界面——不发请求、不改数据（那些放 `OnOpen` / 交互回调），否则"刷新→改数据→再刷新"死循环。

**`SetData` 在哪里调（调用权归属）**：永远写在**业务侧"数据产生 / 变化的地方"**，面板只是消费方：

| 谁 | 调什么 | 典型位置 |
|---|---|---|
| 业务：打开面板那次交互 | `RevUI.Open<T,TData>(data)`（已开/没开不用区分） | 点列表项开详情 |
| 业务：面板已开想换内容 | `RevUI.Get<T>()?.SetData(新数据)` | 详情页点"下一个" |
| 业务：数据异步到达 | 回调里 `panel.SetData(..)`；面板没开就 `RevUI.Open<T,TData>(data)` | 网络回包 / 协程加载完成 |
| 面板内部 | 一般不调 `SetData`；原地改后 `RefreshView()` | — |

## 1.2 控件事件三种接法（可混用）

① **方法特性**（九个，标在面板/Part 任意方法上）：`[RevButtonClick("btnStart")] void OnStart()`；另有 `RevButtonLongPress` / `RevButtonLoosen` / `RevToggleChanged` / `RevSliderChanged` / `RevInputChanged` / `RevInputEndEdit` / `RevDropdownChanged` / `RevScrollChanged`。控件名 = 节点名（区分大小写；多级路径只取最后一段）；**写错第一次装配就报"找不到节点"**，不静默。参数形状只支持文档列出的几种（点击：`()` / `(string nodeName)`；Toggle：`()` / `(bool)` / `(string, bool)`…），其它装配时报错。
② **重写节点名回调**：`OnClick(string nodeName)` 等（集中处理，`switch (nodeName)`）。**高频事件（ScrollRect 滚动 / 输入框每次敲键）首选它**：方法特性走反射调用，每次触发一次小分配。
③ **`[RevBind]` 字段 + 自己 `AddListener`**：要用 UGUI 其它回调（拖拽 / 悬停）或第三方控件时。自研控件用 `RevUI.RegisterAutoEvent<T>((dispatch, c) => …)` 接进来。
- **长按判定**：按住 ≥ `RevUISetting.ButtonLongPressSeconds`（默认 0.5s），**松开时**触发；只对 `Button` 生效；同一次操作也触发"松开"。
- 同一次事件里"重写回调"与"方法特性"**两条路都会走**——同一件事只放一处做。

## 1.3 动画（内置库，不依赖 DOTween）

- 面板 / Part：重写 `ShowAnimation` / `HideAnimation` 一行预设（`RevUIAnimPreset.PopIn` / `PopOut` / `FadeIn` / `SlideInFromTop` … 共 14 个）；显示动画**播完才算"打开完成"**，隐藏动画**播完才回收**。想自己掌控节奏重写 `PlayOpenTransition(Action onDone)`，★ **结束时必须调 `onDone`**，否则面板永远停在 `Opening`。
- 任意控件：`RevUIAnim.FadeIn / SlideIn / ScaleTo / FadeTo / Breathe / AddHoverFeedback / Play(...)`；**一定传 `owner: this`**（关闭/销毁时框架一行 `StopAllOf(owner)` 清干净）。
- 同一控件上只留一个动画（再起一个顶掉上一个）；动画走 `unscaledDeltaTime`（`timeScale = 0` 也播）；`RevUISetting.UIAnimationsEnabled = false` 一键关全部（预设直接写终态，业务零改动）；时长不传或 ≤ 0 = 预设默认时长。
- 透明度写 `CanvasGroup`（没有自动补），缩放/位移写 `RectTransform`；恢复基准态 `RevUIAnim.RestoreBase(控件)`。

## 1.4 Canvas 架构与根 Canvas

- 默认 **单 Canvas**（`RevUISetting.CanvasArchitecture = Single`），渲染模式默认 `ScreenSpaceOverlay`；框架默认加载 `Resources/RevUIPrefab/RevUICanvas.prefab`，**载不到会代码兜底建 Canvas 并打 Warning**（不会整屏起不来）。
- 只有"Profiler 证明 Canvas 合批是瓶颈 + 常规优化仍不达标"才切 `Split`（`Static` / `Dynamic` / `Common` 三画布，面板上写 `CanvasType`），且要在**第一次开面板之前**切；只配一个属性不会自动把预制体里的控件分离，要拆成两个 Scene 面板；`Normal` / `Popup` / `Toast` / `Guide` / `Top` 层声明成 `Static` / `Dynamic` 会被放回 `Common` 并告警。

## 2. 同步 Open vs 异步 Open（最常踩）

- `RevUI.Open<T>()`（无回调那个重载）**不加载任何资源**：只在"已打开过"或"实例池里有"时拿得到实例；需要加载时返回 `null` 并报错。
- 真机预制体在 AB 里 → **首次打开必然异步**：用 `Open<T>(onOpened)` / `Open<T, TData>(data, onOpened)` / `OpenAsync<T>()`。
- 想"点开秒现"就先 `RevUI.Preload<T>()`（预热），之后再走同步 `Open<T>()`。

## 3. 面板池 / Back 语义

- **面板池是 UI 专用池**（不套通用对象池）：关闭优先回池（`KeepAlive`），池满或 `DestroyOnClose` 才真销毁；上限 `RevUISetting.MaxCachedPanels`。取出时必须走 `OnReuse` 钩子（重置界面状态）。
- `RevUICacheMode`：`Unspecified / DestroyOnClose / KeepAlive`…
- **`RevUI.Back()` 只关"最晚打开、且 `InBackStack = true`"的面板**；飘字 / 常驻 HUD 一律 `InBackStack = false`。
- `ExclusiveGroup`（特性参数）= 同组互斥（背包页签这类）。
- 关闭优先级：面板自己的 `RequestClose()`（可被拦截，例如"有未保存内容"）→ `CloseSelf()`。

## 4. Part / 宿主

| 类型 | 怎么用 | 什么时候用 |
|---|---|---|
| 节点级 Part | 不加特性，用 `[RevBind]` / `GetComponent` 拿 | 只是把节点上的组件包一层内聚逻辑，零加载 |
| 预制体 Part | 类上加 `[RevUIPart(root, name)]`，`RevUIPart.Create<T>(host, parent, onCreated)` | 可跨面板复用的界面块（弹窗、列表项） |

- 面板即宿主：`PartRoot => transform`、`IsHostOpened`、`RequestClose`、`NotifyPartChanged`。
- Part 状态变化 → 通过宿主通知（`IRevUIPartHost.NotifyPartChanged`），**不要**让 Part 直接找别的面板。

## 5. 分层：要不要做 System / BusinessLogic？（结论：不做）

| 判定项 | 结论 |
|---|---|
| 能力上缺不缺 | **不缺**：跨界面共享 → 服务定位器；单屏流程 → 面板 + `TData`；界面间通信 → `RevEvent` + Part 宿主中转；数据更新 → `SetData` + `OnDataChanged` |
| 框架要不要提供基类 | **不要**：框架给"机制"（容器 + 面板基类），分层是"约定"；把约定做成基类 = 让所有人付代价 |
| 做了的成本 | 每屏 3~4 个文件、4 跳数据流、两套生命周期、排查路径变长 |

**分工规则（一句可执行）**：跨界面复用的 = 服务；只属于这一屏的 = 面板自己的。
判断法：这段逻辑"**关掉这个界面之后还该活着吗**"？该 → 服务；不该 → 面板里。

分档落地：

| 档 | 典型界面 | 逻辑放哪 | 文件数 |
|---|---|---|---|
| 简单 | 设置页、说明页、加载页 | 全在面板类 | 1 |
| 中等 | 背包、商店、邮件列表 | 面板 + `TData` 数据对象；数据从服务取 | 2 |
| 复杂 | 战斗结算、活动、跨界面流程 | 面板只管装配 / 落屏 / 转发；规则与流程抽成**业务服务**（纯 C#）；必要时再加 `XxxFlow` | 3~4 |

**什么时候必须抽走（信号）**：

1. 同一套流程被 ≥2 个界面复用（如"购买流程"）→ 抽领域服务（`IPurchaseService`），**不是**新增 UI 层；
2. 逻辑要"无 UI 也能跑"（单测 / 离线算 / 与服务端同规则）→ 必须是**纯 C#、不引用 UnityEngine** 的类，放服务层；
3. 单屏逻辑 > 500 行或状态机复杂（多分支、可中断、可回退）→ 抽 `XxxFlow`；
4. 将来真要上**代码热更** → 那时才需要"可热更逻辑 vs 不可热更表现"的物理分层。

**面板里出现这些就该抽走**：协议 / HTTP、配置表解析、跨面板共享状态、计时器与长任务、纯计算（公式 / 排序 / 筛选）、存档读写、跨模块事件路由。
抽的时候**不要去改面板基类**（改基类 = 把特例变成所有人的负担）。

## 6. 写 UI 的自检清单

- [ ] 面板类上有 `[RevUIPanel(root, layer)]`；路径用 `RevResPath.*` 常量，不手拼字符串。
- [ ] `[RevBind]` 字段名与节点名一致（或写全路径）；`OnBindView` 里不做业务。
- [ ] 有数据的面板：`OnRefreshView` 只用 `Data` 渲染，不在里面发请求 / 读表；用 `Data` 前判空（`SetData(null)` / 回池复用会清成 null）。
- [ ] 原地改了 class 数据内容 → 有手动 `RefreshView()`；换数据走 `SetData`；`OnDataChanged` 里**没有**多余的 `RefreshView()`。
- [ ] `SetData` / `Open<T,TData>` 写在业务侧（点击回调 / 网络回包），不是写在面板里。
- [ ] 重写 `PlayOpenTransition` / `PlayCloseTransition` 的，结束时**调了 `onDone`**；动画都传了 `owner: this`。
- [ ] 按钮点击统一走 `OnClick(path)` 或绑定的方法，不在多个地方散落逻辑。
- [ ] `ExclusiveGroup` / `InBackStack` / `RevUICacheMode` 按语义设置（弹窗不该进返回栈；常驻 HUD 不该来回销毁）。
- [ ] 打开一律 `Open<T>(onOpened)` 或 `OpenAsync`；需要秒开就先 `Preload`。
- [ ] 面板里没有"只给这一屏用"以外的重逻辑（跨屏复用的一律搬去服务）。
