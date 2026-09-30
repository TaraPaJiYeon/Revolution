# UI 系统 · 架构解析

> 覆盖：`Assets\Revolution\Runtime\RevUISystem\`（`Core\` + `Facade\` + `Implementation\` + `Interfaces\` + `Support\`）
>
> 目标：看完能独立完成"写一个面板 → 声明预制体路径 → 打开/关闭 → 数据刷新 → 复用与释放"，并且知道它和早期版本的每一处差别、每处差别为什么这么改，以及从参考实现 UI 框架里取了哪些设计、砍掉了哪些复杂度。

---

## 〇、先看最短路径（三句话能跑起来）

```text
① 写面板      [RevUIPanel(RevResPath.UI_Panel)] 面板类继承 RevUIPanel（资源名默认 = 类名）
② 摆预制体    预制体名 = 面板类名，放在资源根目录下的 UI/Panel，并设好 AB 名
③ 打开        RevUI.Open<BagPanel>(panel => { ... });        // 关闭：panel.CloseSelf()
```

就这三步。剩下的都是这三步的细节。

---

## 设计哲学 · 一条主线

> **让"复用"安全，让"异常"可见，让"分层"靠结构而不是靠自觉。**
> 后面每一章的设计决策都是这三条的展开 —— 它们同时也是"为什么这么设计"和"好处是什么"的答案。

### ① 复用安全：复用是性能，也是 bug 温床

界面关闭不销毁、下次直接复用是最省的做法（不加载、不实例化、不重新绑定），但**复用最大的风险是"上次的东西还在"**：
上一次的滚动位置、选中的页签、输入框里的字、面板还挂着上次的数据 —— 这类 bug 最难查，因为它"偶尔才出现"。

所以框架把"复用"做成一条**有契约的路径**，而不是让每个面板各凭自觉：

- 实例池 + 必写钩子 `OnReuse()`（框架保证它一定被调用，业务只要在里面清**界面残留**）；
- 框架在 `OnReuse` 之前**先替你清空上一次的数据**（泛型面板连 `Data` 一起清）—— 不需要你记得清，也不会忘；
- 复用时再兜一次 `RevEvent.RemoveAllByOwner(this)`，避免"复用的实例还在收上一次的事件"。

### ② 异常可见：UI 的问题几乎都是"静默失败"

UI 出问题的典型形态是"点了没反应""图不见了""数据不对"，而根源往往是：节点改名了、预制体没进包、控件类型写错了。
这些如果只是一句 `null`，就只能靠断点猜。

所以框架里凡是"本该成功却没成功"的地方，都报**能照着改**的错误：

- 绑定找不到节点 → 打印"要找的名字 + **根节点下真实存在的节点名** + 两种改法"；
- 预制体加载不到 → 列出三条排查路径（资源根目录 / AB 标记 / 路径写法），并提示打包工具「检查」页签能看漏标；
- 预制体与脚本对不上 → 直接说"根节点上没有 XxxPanel 组件"，并提醒默认约定是"预制体名 = 类名"；
- 同步打开拿不到实例 → 明确告诉你改用哪个入口，而不是返回 null 让你查半天。

### ③ 分层靠结构：钩子职责分离，但仍然只有一个脚本

参考实现的 UI 是 `View / Logic / System / BusinessLogic` 四层 + TS 桥接，分层很干净，代价是**一个界面要写好几个文件**。
你没有热更需求、也不想为一面板写一堆脚本，所以这里取的是参考文档《06-架构抽离与复用》自己给出的结论：
**用结构约束行为，而不是靠注释约定**。

```csharp
protected override void OnBindView()    { /* 只做表现装配：拿控件、挂交互 */ }
protected override void OnRefreshView() { /* 只做数据落屏：把 Data 画到界面 */ }
protected override void OnClick(string nodeName) { /* 业务逻辑：改数据、请求数据、刷新 */ }
```

带数据的界面继承 `RevUIPanel<TData>` 时，`OnRefreshView` 是**抽象方法** ——
有数据就必须写清楚"数据怎么落到界面上"，不允许糊过去；而纯静态界面（说明页、加载页）继承 `RevUIPanel` 就少写一个方法。

> **好处（一句话版）**：一个界面写一个文件、改一处只影响一处；真机出问题能顺着报错文案直接定位，不用开断点猜；
> 面板之间"谁开谁关、谁挡谁、谁进池"由管理器统一保证，业务不用各自维护。

---

## 一、UI 系统解决什么问题

UI 是"最容易越写越乱"的一块：谁都能开界面、谁都能改界面，最后变成"改一个按钮崩三个界面"。这套系统盯的是四件事：

| 要解决的问题 | 具体做法 |
|---|---|
| **界面怎么找到** | 路径**写在面板类自己身上**（一行特性），打开时框架去读 —— 调用处一行路径都不写，也就没有"调用处拼错路径" |
| **控件怎么拿到** | `[RevBind]` 字段按"**字段名 ↔ 节点名**"自动绑定；不想写字段就重写 `OnClick(节点名)` 按名字分发 |
| **表现 / 数据 / 逻辑怎么分** | 用**两个必写钩子**在结构上强制分开（`OnBindView` 只装配、`OnRefreshView` 只画数据），但**一个面板仍然只有一个脚本** |
| **开/关/复用的资源与状态怎么不出错** | 管理器统一管层级、遮罩、实例池、返回栈、互斥组、延迟销毁；关闭时**自动摘掉本面板注册的全部事件** |

---

## 二、当前实现 vs 早期版本

### 2.1 总览

| 维度 | 早期版本（`BasePanel` 219 行 + `UIMgr` 335 行） | 新版（`RevUISystem\`，26 个文件） |
|---|---|---|
| 面板基类 | `BasePanel`：`ShowMe` / `HideMe` 两个钩子 | `RevUIPanel`：**生命周期 8 个钩子 + 2 个必写钩子**（表现/数据分离） |
| 管理器 | `UIMgr.ShowPanel<T>()`：加载 + 层级 + 隐藏复用 | `RevUIManager` + `RevUI` 门面：打开/关闭/层级/池/互斥/返回栈/遮罩/诊断 |
| 控件获取 | `Awake` 里**把 Button/Toggle/Slider/InputField/Dropdown/ScrollRect/Text/Image… 全量扫进字典**，再用 `GetControl<T>("名字")` 弱类型取 | `[RevBind]` 强类型字段，**每类型只反射一次**（绑定计划缓存）；只扫"你重写了回调的那几类控件" |
| 事件绑定 | 每个按钮 `AddListener(() => ClickBtn(名字))`（**每实例每次 Awake 都分配闭包**） | 每个交互节点一个"继电器"对象，**实例创建时挂一次**，池化复用不再重挂 |
| 输入框事件 | `onValueChanged` 里同时回调"输入中"和"结束编辑"（每敲一个字都触发一次结束编辑） | `onValueChanged` / `onEndEdit` **分开挂**，语义正确 |
| 资源路径 | 靠"预制体名 = 面板类名"约定 + AB 名写死 `ui_panel` | 同样默认"预制体名 = 类名"，但**根目录段写在特性里**，支持多级嵌套目录，可用 `RevResPath` 常量 |
| 层级 | `Bottom / Middle / Top / System` 四层（靠加载 `UI/Canvas` 预制体摆好） | `Scene / Normal / Popup / Toast / Guide / Top` 六层，**代码建 Canvas**（零资源依赖） |
| Canvas 架构 | 只有单 Canvas | **单 Canvas（默认且主推）**；仅单 Canvas 的实测合批瓶颈经过常规优化仍不达标时，可选三 Canvas 动静分离（常用 / 静态 / 动态；见 4.12） |
| 遮罩 / 点穿 | 无（要每个弹窗预制体自己记得摆遮罩） | 按层级**自动**给弹窗铺透明挡板，自动插到正确位置，点遮罩关最上面的弹窗 |
| 复用 | "隐藏不销毁"（`SetActive(false)`），**没有清理契约** | 实例池 + **`OnReuse` 清理钩子**，框架还会替你清掉上一次的数据 |
| 数据 | 没有数据这一层（数据在面板字段里，谁都能改） | `RevUIPanel<TData>` + 纯 C# 数据对象 + `OnDataChanged(增量)` + `OnRefreshView` |
| 返回栈 / 互斥 | 无 | `RevUI.Back()` / `[RevUIPanel(..., ExclusiveGroup = "bag")]` |
| 加载合并 | 有（用 `PanelInfo` 占位 + 回调累加，思路很好，保留） | 有，且更彻底：在途请求**真合并**，多份回调都收到同一个实例 |
| 诊断 | 无 | `RevUI.DumpStats()`（打开中/加载中/池中 + 每层顺序 + 被盖住状态） |
| 第三方依赖 | DOTween 扩展（`DOTweenPanelAnimationExtension` 等 1000+ 行，硬依赖） | **零依赖**：转场是 `PlayOpenTransition(Action onDone)` 可插拔钩子 |

> **说明**：代码量比早期版本大（早期版本 554 行 ≈ 只做"开/关/隐藏"），因为补上了早期版本**完全没有**的能力：六层与自动遮罩、实例池 + 清理契约、互斥组、返回栈、数据驱动、Part 复用、绑定计划缓存、延迟销毁与延迟排序、异常隔离、诊断统计。早期版本那两个"方便"的点（**预制体名 = 类名**、**一个面板一个脚本**）不但保留，还是新版的主线。

### 2.2 为什么"表现 / 数据 / 逻辑"不拆成三个类

参考实现的 UI 是 `View / Logic / System / BusinessLogic` 四层 + TS 热更桥接，分层干净，但**一个界面要写好几个文件、还要跨语言通信**。你没有热更需求、也不想为一面板写一堆脚本，所以这里取的是参考文档《06-架构抽离与复用》自己给的结论：

> **用结构约束行为，而不是靠注释约定。**

具体落地就是：**一个面板 = 一个脚本**，但每个钩子只许干一件事：

```csharp
protected override void OnBindView()   { /* 只做表现装配：拿控件、挂交互 */ }
protected override void OnRefreshView() { /* 只做数据落屏：把 Data 画到界面 */ }
protected override void OnClick(string nodeName) { /* 业务逻辑：改数据、请求数据、刷新 */ }
```

带数据的界面继承 `RevUIPanel<TData>` 时，`OnRefreshView` 是**抽象方法**——有数据就必须写清楚"数据怎么落到界面上"，不允许糊过去；而纯静态界面（说明页、加载页）继承 `RevUIPanel` 就少写一个方法。

---

## 三、运用了参考实现的哪些设计哲学

对照《00~07 UI框架》《05-UI中介者模式》《局外系统架构》《messagebox》里的结论，逐条说落地情况：

| 参考实现的做法 / 结论 | 这里的落地 |
|---|---|
| ① **路径寻址**（`OpenForm(path)`），且路径不该手写在调用处 | 路径写在**面板类的特性**上（`[RevUIPanel(root, layer, name)]`），打开时框架读；`name` 省略 = 类名 |
| ② **Form 专用对象池 + `ReUse()` 重置契约**（明确写过"form 不要用通用 GameObjectPool"） | 专门的 `RevUIPanelPool`（**没有**套用框架的 `RevPool`）+ 必写钩子 `OnReuse()`，框架还会先替你清空上次的数据 |
| ③ **三层查找**：单例复用 → 池 → 新建 | 在途合并 → 已打开 → 实例池 → **资源缓存里同步实例化** → 异步加载新建（五级，见 4.4） |
| ④ **绑定代替硬编码 `Find("Panel/Panel/Button")`**（文档里把硬编码 Find 列为反面教材） | `[RevBind]` 字段 + 绑定计划缓存；找不到时报错会**列出根节点下真实存在的节点名** |
| ⑤ **按所有者批量注销事件**（防泄漏纪律，`RemoveEventHandlersByObserver(this)`） | 关闭面板/Part 时框架自动 `RevEvent.RemoveAllByOwner(this)` —— 机制保证，不靠纪律 |
| ⑥ **遍历中删除要延迟**（`m_formsWaitingRecycle`）、**排序用脏标记延迟批量做** | 关闭的面板下帧才销毁；层内排序由根节点 `LateUpdate` 按脏标记一次做完（本帧开关的面板在同一帧渲染前排好） |
| ⑦ **互斥组 + 打开序号决定层级**（`m_group` / `m_formOpenOrder`） | `ExclusiveGroup`（开新的自动关同组旧的）；层内顺序 = 打开顺序 |
| ⑧ **消息框结论**：队列 + 优先级 + 去重；按钮只转发事件不处理业务；内容/容器分离 | 结构上已备齐（`InBackStack`、`CloseGroup`、遮罩点击关顶层）；MessageBox 本身列为"本期没做"，见第九章 |
| ⑨ **单向数据流**（UI 不改数据、系统不直接刷 UI、界面之间不直接通信） | `SetData → OnDataChanged → OnRefreshView` 是唯一数据入口；Part 与 Part 之间必须经宿主中转（`IRevUIPartHost.NotifyPartChanged`） |
| ⑩ **From / Part**（独立面板 / 依附的可复用单元） | `RevUIPanel` / `RevUIPart`；Part 分**节点级**（摆宿主预制体里，零加载）与**预制体级**（可跨面板复用） |
| ⑪ 异常隔离（一个 handler 抛异常不影响整条派发） | 所有业务钩子都过 `RevUILog.Guard`；打开回调逐个 try/catch |
| ⑫ 巨型方法（`OpenMessageBoxBase` 19 个参数）是反面教材 | 打开接口是 `Open<T>(data, onOpened)`；数据用**数据对象**传，而不是参数列表 |

**砍掉的（都是"为 TS 热更 / 跨语言"而存在的那部分）**：Puerts 桥接、`LuaCallCs_*`、`CallTsViewFun`、`CUITsComponent / CPuertsProxyView`、`.mjs` + `InGamePath:` + CDN 寻址、跨语言事件转发。

**简化的**：`View + Logic + System + BusinessLogic` 四层 → **一个面板类 + 职责钩子**；参考实现的 `UI_BINDING` **代码生成流程** → 特性 + 反射（每类型一次，结果缓存）；三套事件系统 → **统一用本框架的 `RevEvent`**。

---

## 四、新版 UI 系统怎么用

### 4.1 一个面板长什么样

**最小面板（没有数据）**：

```csharp
[RevUIPanel(RevResPath.UI_Panel, RevUILayer.Normal)]      // ← 资源路径写在类上，资源名默认 = 类名
public sealed class SettingsPanel : RevUIPanel
{
    [RevBind] private Button _btnClose;                   // 节点名 btnClose
    [RevBind] private Slider _volume;                     // 节点名 volume

    protected override void OnBindView()                  // ★ 只做表现装配
    {
        _btnClose.onClick.AddListener(CloseSelf);
    }

    protected override void OnClick(string nodeName)      // 也可以不写字段，按节点名分发
    {
        if (nodeName == "btnReset") ResetToDefault();
    }

    private void ResetToDefault() { /* 业务逻辑 */ }
}
```

**带数据的面板**：

```csharp
// 数据：纯 C# 对象（放哪都行，同一个文件也可以）—— 它不碰 UnityEngine，所以能被普通单测构造与断言
public sealed class BagData
{
    public bool UseCellLayout;
    public IReadOnlyList<int> ItemIds;
}

[RevUIPanel(RevResPath.UI_Panel, RevUILayer.Normal, name: "BagPanel")]
public sealed class BagPanel : RevUIPanel<BagData>
{
    [RevBind] private ScrollRect _itemList;

    protected override void OnBindView()                  // ★ 只装配
    {
        _itemList.gameObject.SetActive(false);
        RevEvent.AddEventListener(GameEventId.BagChanged, OnBagChanged, owner: this);   // 关闭时自动摘
    }

    protected override void OnRefreshView()               // ★ 只画 Data（有数据的面板必须实现）
    {
        _itemList.gameObject.SetActive(Data.UseCellLayout);
        // Data.ItemIds → 列表…
    }

    protected override void OnDataChanged(BagData oldData, BagData newData)   // 可选：只更新变化的部分
    {
        if (oldData != null && oldData.UseCellLayout == newData.UseCellLayout) return;   // 布局没变就不动
        // 只处理会变的那部分…
    }

    private void OnBagChanged() => RefreshView();         // 业务逻辑：数据变了就重画
}
```

### 4.2 资源路径声明（写在类上，加载时自动读）

```csharp
[RevUIPanel(RevResPath.UI_Panel, RevUILayer.Normal)]                          // 资源名 = 类名（BagPanel）
[RevUIPanel("UI/UIPanel/Lobby", RevUILayer.Normal)]                           // 多级嵌套目录，随便多深
[RevUIPanel("UI/Popup", RevUILayer.Popup, "Form_Confirm")]                    // 资源名与类名不一致时写第三个参数
[RevUIPanel(RevResPath.UI_Popup, RevUILayer.Popup,
    CacheMode = RevUICacheMode.DestroyOnClose,                                // 关闭即销毁（不占内存）
    Mask = RevUIMaskMode.None,                                               // 不挡下面的点击
    ExclusiveGroup = "confirm",                                              // 同组只留一个
    InBackStack = false)]                                                    // 不参与返回栈
public sealed class ConfirmPanel : RevUIPanel<ConfirmData> { ... }
```

| 特性字段 | 默认 | 说明 |
|---|---|---|
| `Root`（第一个参数，必填） | — | 预制体所在的**资源根目录段**，和 `RevResManager` 的 `rootPath` 同义；结尾带不带 `/`、用 `\` 还是 `/` 都会被规范化 |
| `Layer`（第二个参数） | `Normal` | 挂哪一层 |
| `Name`（第三个参数） | `null` → **类名** | 资源名（不带扩展名） |
| `CacheMode` | `Unspecified` → 用 `RevUISetting.DefaultCacheMode` | `KeepAlive`（关闭进池复用）/ `DestroyOnClose` |
| `Mask` | `Auto` | 按层推断：Popup / Guide / Top 挡点击，其余不挡 |
| `CanvasType` | `Common` | 进哪个画布：`Common` / `Static` / `Dynamic`；只在三 Canvas 架构下生效，静态 / 动态只收 Scene 层（见 4.12） |
| `ExclusiveGroup` | 空 | 互斥组名 |
| `InBackStack` | `true` | 是否参与 `RevUI.Back()` |

**预制体本身的要求**（两条）：

1. 根节点是 `RectTransform`，并且**挂上面板脚本**（框架对面板加了 `[RequireComponent(typeof(RectTransform))]`，漏了会自动补）；
2. 预制体**名字**默认与面板类名一致（不一致就用特性第三个参数写清楚）。

> 面板预制体放哪：放在「资源根目录」下的任意目录（建议 `UI/Panel`），并给它（或它的父文件夹）**设 AB 名** —— 和普通资源完全一样。打包工具的「检查」页签会替你确认没有漏标。

### 4.3 控件绑定与"控件监听"怎么处理（两种写法，可以混用）

**写法一：强类型字段 + `[RevBind]`**

```csharp
[RevBind] private Button _btnClose;          // 找名字叫 "btnClose" 的节点
[RevBind] private Text   m_title;            // 同样找 "title"（m_ 前缀会被忽略）
[RevBind("Top/Title")] private Text _title;  // 层次深、或同名节点多时，写显式路径（相对面板根节点）
```

节点名规则（**越少越好预测**）：

| 字段名 | 找的节点名 | 规则 |
|---|---|---|
| `_btnClose` | `btnClose` | 去掉前导下划线 |
| `__btnClose` | `btnClose` | 多个下划线都去掉 |
| `m_title` | `title` | 去掉 `m_` 前缀 |
| `btnClose` | `btnClose` | 原样 |

查找顺序：**直接子节点 → 整个后代**（UI 嵌套三四层很常见）。同名节点有多个时用第一个并**告警**提示你改用显式路径；找不到时报错会把"要找的名字"和"**根节点下真实存在的节点名**"一起打出来：

```text
BagPanel._btnClose（想要 Button）找不到节点：
  找的是：名字 = "btnClose"（按字段名推出来的）
  根节点 BagPanel 下现有节点："Bg"  "Top"  "btnClose2"  "List"  …
  → 要么把节点改成这个名字，要么写清楚路径：[RevBind("父节点/子节点")]
```

字段类型可以是**任意 Component**（`Button` / `Image` / `Text` / TMP 的控件 / 你自己的控件 / `RevUIPart`）或 `GameObject`；节点上直接没有、子节点里**唯一**有一个时会自动用它（多个就报错让你指清楚）。

**写法二：按节点名分发（早期实现最方便的地方，保留）**

```csharp
protected override void OnClick(string nodeName)
{
    switch (nodeName)
    {
        case "btnClose": CloseSelf(); break;
        case "btnSort":  SortByQuality(); break;
    }
}

protected override void OnToggleChanged(string nodeName, bool value) { }
protected override void OnSliderChanged(string nodeName, float value) { }
protected override void OnInputChanged(string nodeName, string value) { }
protected override void OnInputEndEdit(string nodeName, string value) { }
protected override void OnDropdownChanged(string nodeName, int index) { }
protected override void OnScrollChanged(string nodeName, float x, float y) { }
```

两个关键点：

- **只挂你重写了的那些**：没重写 `OnToggleChanged`，框架连 `Toggle` 都不扫（早期版本是无脑全扫 + 每个按钮一个闭包）；
- **同一个按钮只会走一条路**：写了字段就用字段，重写了 `OnClick` 就按名字分发，两者不会重复触发。

**控件监听：从声明到回调的完整链路**（"控件监听"到底是怎么处理的）

早期实现的做法是"`Awake` 时把所有控件全扫一遍，每个按钮挂一个闭包，回调里再按名字 if/switch"。这里把这件事拆成五步，每一步都解决旧做法的一个具体问题：

```text
① 类加载期（每个类型只做一次，结果缓存）
   反射一次 → 绑定计划 RevUIBindPlan：哪些字段要绑、字段名对应哪个节点、这个类是"重写了哪些交互回调"

② 实例创建期（面板/Part 首次装配，只做一次）
   按计划绑字段：显式路径 or 字段名 → 找节点 → 取组件 → 赋值
   只扫"你重写了回调"的控件类型：没重写 OnToggleChanged 就连 Toggle 都不扫

③ 挂监听（每个交互节点一个"继电器"对象，它自己记住自己叫什么）
   btn.onClick.AddListener(relay.OnClick)
   ↑ 不是 () => OnClick("btnClose") 那种闭包；继电器是实例方法引用，也不会随"开/关界面"重复分配

④ 事件触发（框架统一转发，业务钩子被异常隔离）
   控件事件 → relay → IRevUIUserEvents.DispatchXxx(节点名) → RevUILog.Guard → 你的 OnClick/OnToggleChanged/…

⑤ 生命周期收尾（自动，不用你记）
   关闭面板/Part 时 → RevEvent.RemoveAllByOwner(this)      // 你自己注册的事件全摘掉
   实例进池 → 控件引用保留（不重新绑定）；复用时 → OnReuse 清界面残留
```

| 要挂的监听 | 怎么声明 | 什么时候用哪个 |
|---|---|---|
| 按钮点击 | `[RevBind] Button _btnClose;` 或 `OnClick(节点名)` | 单个按钮要做特殊处理 → 用字段；一整屏按钮统一分发 → 用 `OnClick` |
| 开关 / 滑条 / 输入框 / 下拉 / 滚动 | 重写对应 `OnXxxChanged(节点名, 值)` | 这类控件天然是"按名字区分"，用名字分发最省代码 |
| 输入框"结束编辑" | 重写 `OnInputEndEdit` | 提交 / 校验放在这里；`OnInputChanged` 只做实时反馈 |
| 自定义控件（TMP / 长按按钮 / 自研） | `RevUI.RegisterAutoEvent<T>(...)` | 注册一次，之后所有面板里出现该类型控件都自动接上 |
| 框架之外的事件（业务事件 / 网络回调） | `RevEvent.AddEventListener(..., owner: this)` | 写在 `OnBindView` 里，关闭时框架自动按 owner 摘掉 |

**自定义控件怎么办**（TMP、长按按钮、自研控件）：

```csharp
RevUI.RegisterAutoEvent<LongPressButton>((dispatch, btn) =>
{
    btn.onShortClick.AddListener(dispatch.Click);     // 短按 → OnClick(节点名)
    btn.onLongPress.AddListener(dispatch.Click);      // 长按 → 也走 OnClick(节点名)
});
```

### 4.4 打开、关闭、层级、遮罩

**打开的五种姿势**：

```csharp
// ① 回调式（推荐；真机首次必然异步，这种写法最稳）
RevUI.Open<BagPanel>(panel => { /* 打开完成后回调；失败时收到 null */ });

// ② await 式
BagPanel p = await RevUI.OpenAsync<BagPanel>();

// ③ 带数据（强类型，不装箱）
RevUI.Open<BagPanel, BagData>(data, panel => panel.RefreshView());
BagPanel p2 = await RevUI.OpenAsync<BagPanel, BagData>(data);

// ④ 同步（只在"已经打开过 / 池里有 / 预制体已在资源缓存里"时能成功）
BagPanel p3 = RevUI.Open<BagPanel>();
//   想让第一次打开也同步成功：先预热（编辑器直读模式下不预热也能成功）
RevUI.Preload<BagPanel>(() => { BagPanel p4 = RevUI.Open<BagPanel>(); });
```

> `RevUI.Open<T>()` 走不通时会**报一条明确错误**并告诉你改用哪个入口，而不是静默返回 null 让你查半天。

**找实例的顺序（从快到慢）**：

```text
① 正在加载中  → 把本次请求并进在途那次（★ 真合并：并发 Open 同一面板只创建一个实例，两份回调都收到它）
② 已经打开    → 置顶 + 按最新数据重画
③ 实例池里有  → 取出复用（不加载、不实例化、不重新绑定 —— 最快的一条路）
④ 预制体已在资源缓存里（Preload 过 / 编辑器直读）→ 同步实例化
⑤ 都没有      → 异步加载预制体 → 实例化 → 装配 → 打开
```

**关闭与层级**：

```csharp
panel.CloseSelf();                       // 面板内部关自己
RevUI.Close<BagPanel>();
RevUI.Close(panel);
RevUI.CloseAll(RevUILayer.Popup);         // 只关某一层
RevUI.CloseGroup("confirm");             // 关掉互斥组当前占用的那个
RevUI.Back();                            // 返回上一层（关掉最晚打开、且参与返回栈的面板）
RevUI.ShutdownAll();                     // 回登录界面 / 切大版本：所有面板 + 实例池 + 根节点一起清
```

| 层级 | 典型内容 | 默认挡点击 |
|---|---|---|
| `Scene` | 主界面、大厅、全屏场景界面（对应早期版本 `Bottom`） | 否 |
| `Normal` | 二级界面、背包、商店（对应早期版本 `Middle`） | 否 |
| `Popup` | 确认框、奖励结算（对应早期版本 `Top`） | **是** |
| `Toast` | 飘字、跑马灯（不参与返回栈） | 否 |
| `Guide` | 新手引导遮罩、手指提示 | **是** |
| `Top` | 断线重连、Loading、公告（对应早期版本 `System`） | **是** |

**遮罩是自动的**：某层出现"要挡点击"的面板时，框架在该层铺一块**全屏透明挡板**（只挡点击、不遮画面；要半透明黑底就在面板预制体里自己画），并自动插到"最上面那个弹窗"的正下方（挡板是**不生成顶点**的 Graphic：只挡射线、不进合批，没有全屏 overdraw）。点它会关掉该层最上面的弹窗（`RevUISetting.ClickMaskClosesTop = false` 可关掉这个行为）。

**被盖住会通知你**：面板被上层遮罩盖住 / 恢复时会收到 `OnCovered(bool)` —— 用它可以暂停界面上的动画、音效、每帧逻辑。

### 4.5 Part：可复用的 UI 单元

| | 节点级 Part | 预制体级 Part |
|---|---|---|
| 怎么声明 | **不加特性**，就摆在宿主面板的预制体里 | `[RevUIPart("UI/Part")]`（可选第二个参数写资源名） |
| 怎么拿到 | 面板上 `[RevBind] private RevShopTab _tab;`（拿到即自动初始化） | `RevUIPart.Create<ShopTabCell>(this, slot, part => { ... })` |
| 加载成本 | 零（本来就在宿主预制体里） | 一次资源加载（同实例复用，不会重复创建） |
| 复用范围 | 只服务它所在的宿主类型 | **可跨面板复用**（背包、商城、活动共用一个商品格） |

```csharp
[RevUIPart("UI/Part")]
public sealed class ShopTabCell : RevUIPart
{
    [RevBind] private Button _btnBuy;

    protected override void OnBindView()                    // ★ 只装配
    {
        _btnBuy.onClick.AddListener(() => NotifyHost());    // 通知宿主"我这儿点了"
    }

    protected override void OnPartRefresh() { /* 宿主让我刷就刷 */ }
    protected override void OnPartClose()   { /* 停协程、停特效 */ }
}
```

**通信纪律**（Part 才能真的跨面板复用）：

- Part → 宿主：只认 `IRevUIPartHost`（`PartRoot` / `IsHostOpened` / `RequestClose()` / `NotifyPartChanged(this)`）；
- Part ↔ Part：**不直接耦合**，经宿主中转（宿主在 `OnPartChanged(part)` 里协调）；
- 宿主 → Part：宿主直接调 Part 的公开方法，或 `RefreshParts()` 全部刷一遍。

> 加 Part 与面板一样**自动摘事件**：Part 关闭时框架执行 `RevEvent.RemoveAllByOwner(part)`。

### 4.6 生命周期与回调顺序

```text
创建（实例化）
  └─ 绑定控件（[RevBind] 字段此时已有值）→ OnBindView() → OnInit()          ← 只发生一次
打开
  └─ 状态 Opening → PlayOpenTransition() →（转场结束）State=Opened
     → OnOpen() → OnRefreshView() → 打开身上所有 Part → 触发打开回调
复用（从池里取出）
  └─ 框架清空上次的数据 → OnReuse()（清界面残留）→ 摘事件 → 再走"打开"
关闭
  └─ 状态 Closing → 关闭所有 Part → PlayCloseTransition() →（转场结束）
     → OnClose() → ★ 自动 RevEvent.RemoveAllByOwner(this) → State=Closed
     → 回实例池（KeepAlive）或下一帧销毁（DestroyOnClose / 池满）
销毁
  └─ OnRelease() → 归还这一次的 prefab 资源引用 → Destroy(GameObject) → OnDestroy（兜底摘事件）
```

> 状态枚举里有 `Loading`，但那是**管理器的"在途加载"**在表示（这时面板实例还不存在）；
> 面板自己的状态只会走 `None → Opening → Opened → Closing → Closed`。

| 你想做的事 | 该写在哪 |
|---|---|
| 拿控件、挂交互、注册事件（`owner: this`） | `OnBindView` |
| 每次打开都要做的准备（刷新数据、开始计时） | `OnOpen` |
| 清掉上一次的残留（滚动位置、页签选中、输入框） | `OnReuse` |
| 把数据画到界面 | `OnRefreshView` |
| 增量刷新（只更新变化的控件） | `OnDataChanged(old, new)` |
| 业务规则、发请求、改数据 | `OnClick` 等交互回调 |
| 停协程 / 计时器 / 特效 | `OnClose` |
| 解绑外部引用、归还自申请的东西 | `OnRelease` |

### 4.7 转场与显示动画（**框架内置动画库**，不依赖任何缓动库）

面板 / Part 的显示隐藏动画**一行预设**就能加（用法见《使用说明》的动画一节）：

```csharp
protected override RevUIAnimPreset ShowAnimation => RevUIAnimPreset.PopIn;    // 打开时自动播，播完才算"打开完成"
protected override RevUIAnimPreset HideAnimation => RevUIAnimPreset.PopOut;   // 关闭时自动播，播完才真正关闭 / 回池
```

**想自己掌控**：转场钩子仍然可插拔（与预设并存，不重写预设时就在这里写）：

```csharp
protected override void PlayOpenTransition(Action onDone)
{
    RevUIAnim.SlideIn(this, RevUISlideDirection.Top, 0.25f, onDone, owner: this);   // ★ 结束时必须调 onDone
}

protected override void PlayCloseTransition(Action onDone) { onDone(); }
```

- 动画库在 `Runtime\RevUISystem\Animation\`：`RevUIEase` / `RevUIAnimSpec` / `RevUIAnimEngine`（**纯 C#**：采样模型 + 帧余量结转 + 循环往返 + 运行时池 + 版本号句柄）＋ `RevUIAnimTarget` / `RevUIAnimDriver` / `RevUIWidgetFeedback` ＋ 门面 `RevUIAnim`。
- 每帧推进**复用框架已有的 `RevMono`**（不新起隐藏宿主）；时间口径 `unscaledDeltaTime`（暂停时 UI 动画照常播），全局倍速 `RevUIAnim.GlobalSpeed`。
- 默认实现仍是"无动画、立刻完成"（不重写预设、不写转场 = 行为与没有动画库时完全一致）。**注意**：转场钩子里 `onDone` 不调 = 面板会一直停在 `Opening`/`Closing` 状态。
- 要关动效：`RevUISetting.UIAnimationsEnabled = false` —— 预设直接写终态，业务代码一行不用改。

### 4.8 配置与诊断

```csharp
RevUISetting.ReferenceResolution = new Vector2(1920f, 1080f);   // 设计分辨率（框架自建 Canvas 时用）
RevUISetting.MatchWidthOrHeight  = 0.5f;
RevUISetting.SortOrderBase       = 100;                          // Canvas 排序基准
RevUISetting.CanvasArchitecture  = RevUICanvasArchitecture.Single; // Single（默认）/ Split（三 Canvas 动静分离，见 4.12）
RevUISetting.DefaultCacheMode    = RevUICacheMode.KeepAlive;     // 面板没声明时用哪个
RevUISetting.MaxCachedPanels     = 1;                            // 同一面板最多缓存几个实例
RevUISetting.ClickMaskClosesTop  = true;                         // 点遮罩关最上面的弹窗
RevUISetting.BindFailureIsError  = true;                         // 绑定失败按错误报（不建议关）
RevUISetting.VerboseLog          = false;                        // 打开/关闭/命中池的诊断日志
```

```csharp
Debug.Log(RevUI.DumpStats());
// RevUI（单 Canvas）：打开中 3 个，加载中 1 个，池中 2 个      ← 三 Canvas 下每行还会带画布，如 [Scene/Dynamic]
// 打开顺序（从下到上）：
//   [Scene] UI/Panel/MainPanel  Opened
//   [Normal] UI/Panel/BagPanel  Opened（被上层遮罩盖住）
//   [Popup] UI/Popup/ConfirmBox  Opening（互斥组 confirm）
```

框架会**自己建 UI 根节点**（第一次打开面板时）：`[RevUIRoot]`（Canvas + CanvasScaler + GraphicRaycaster，`DontDestroyOnLoad`）→ 六个层级空节点；场景里没有 `EventSystem` 时会兜底建一个（已有自己的就完全不插手）。

### 4.9 关于摄像机：需要吗？

**不需要。** 框架建的 Canvas 是 `Screen Space - Overlay`：

```csharp
// RevUIRoot.Build()
_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
_canvas.sortingOrder = RevUISetting.SortOrderBase;      // 只和"别的 Overlay Canvas"排先后，与场景 3D 无关
```

Overlay 的语义是"**直接画到屏幕上、不参考场景或相机**"（Unity 手册原话：即使场景中根本没有相机也会渲染 UI）。具体到三个组件：

| 组件 | Overlay 下和相机的关系 |
|---|---|
| `Canvas` | 没有 Render Camera 字段可填，也不需要（不存在"忘了指定相机"这种错） |
| `CanvasScaler` | 按**屏幕分辨率**算缩放，不经过相机（所以相机 FOV / 正交透视都不影响 UI 尺寸） |
| `GraphicRaycaster` | 用**屏幕坐标**合成射线，不需要 `EventCamera` —— 这就是"UI 不用相机也能点"的原因 |

**那早期实现为什么要加载 `UI/UICamera` 预制体？** 那台相机是给"UI 层里的 3D 模型 / 粒子特效"和"Camera 模式 Canvas"准备的，顺带沿用了模板；
代价是多三份资源、必须记得打 AB 包、配错（比如 culling mask 漏了 UI 层）要到运行时才发现整个 UI 不显示。这里零资源依赖、零配置。

**什么时候你会真的需要一台 UI 相机**（出现下面四种需求之一时）：

| 需求 | 为什么 Overlay 不行 | 怎么办 |
|---|---|---|
| 面板里摆 **3D 模型**（立绘、旋转展示） | Overlay **永远**画在最上层，3D 模型会被 UI 盖住 | 切 `ScreenSpaceCamera` + 一台 UI 相机（或再加一台相机做相机堆叠） |
| 面板里播 **粒子 / 特效** | 同上 —— 粒子和场景物体都压不过 Overlay | 同上；或把粒子渲到 `RenderTexture` 再贴 `RawImage` |
| 要把 UI **渲进 RenderTexture**（UI 模糊背景、UI 截图、小地图外框） | Overlay **不由相机渲染**，不会出现在任何 RT 里 | 必须用 Camera / World Space 模式 |
| 要让 UI **被场景物体遮挡**（角色走到界面前面挡住它） | Overlay 不参与深度排序 | World Space / Camera 模式 |

> **优先考虑不改模式的替代做法**（改动最小）：把 3D 模型 / 粒子单独用一台相机渲染到 `RenderTexture`，再在面板里用 `RawImage` 显示 ——
> UI 仍然是 Overlay，"零配置 + 永远最上层"的好处全部保留。

**Camera 模式真正要接的线只有三条**（框架已经全替你接好，自己接的时候别漏）：

1. 指定相机：`canvas.worldCamera = uiCamera;`
2. 相机的 **Culling Mask 必须包含 UI 所在 Layer**（漏了 → 整个 UI 不渲染）；
3. **Plane Distance** 要落在相机近裁剪面与远裁剪面之间（否则 Canvas 被裁掉）。

> **不用管 `GraphicRaycaster.eventCamera`** —— 它是**只读**属性，ugui 内部是 `m_EventCamera ?? canvas.worldCamera`，
> 会自动取 Canvas 的 worldCamera。所以"worldCamera 设对" = **显示与点击同时对**。
> （只有工程里另外加了 `PhysicsRaycaster` / 自定义 Raycaster 时，才需要单独关心它们各自的 eventCamera。）

> 现在的默认选择（Overlay）换来的是：UI 永远在最上层；相机被销毁 / 禁用 / 改 culling mask / 改 FOV / 加后处理，**都不影响 UI**；
> 层内顺序靠 Canvas 子节点顺序天然保证，不需要谁去调 `sortingOrder`。代价就是上表那四种需求不满足。

### 4.10 Canvas 和 EventSystem 是谁建的？（都不用你手动建）

| | 谁建 | 什么时候建 | 会不会重复 |
|---|---|---|---|
| `[RevUIRoot]`（Canvas + CanvasScaler + GraphicRaycaster + 六层挂点） | 框架，**代码建** | **第一次** `RevUI.Open<T>()` / `Preload<T>()`（懒创建，不是启动时） | 不会：`EnsureReady` 用 Unity 的"假 null"判断，`ShutdownAll` 销毁后下次打开**自动重建** |
| `[RevUIEventSystem]`（EventSystem + **输入模块**：优先新输入系统，退回早期版本） | 框架，**仅当场景里一个 EventSystem 都没有、且 `RevUISetting.AutoCreateEventSystem` 打开**时 | 紧跟在根节点创建之后（同一时刻，只判断一次） | 只在"你自己一个都没摆"时才建；已有自己的**完全不插手** |

```csharp
// RevUIRoot.Build() 的最后一步
EnsureEventSystem();   // 开关关了 → 直接返回；已有 EventSystem → 直接返回；否则建一个
```

**输入模块怎么选**（"UI 点不动"最常见的坑，框架已经替你处理）：

| 工程情况 | 框架挂的模块 | 结果 |
|---|---|---|
| 装了新输入系统（Input System Package） | `InputSystemUIInputModule`（反射挂载，**自动分配默认操作**） | 正常 |
| 没装新输入系统（就是早期版本 Input） | `StandaloneInputModule` | 正常 |
| Active Input Handling = **New only** 且找不到新模块 | 退回 `StandaloneInputModule` | **报错告警**：UI 会点不动，去改 Active Input Handling 或自己摆一个 |

> 为什么用**反射**而不是直接写类型：新输入系统是可选包，直接 `using` 会让"没装这个包的工程"编译不过 ——
> 现在**装了就自动用上**（`AddComponent` 即可：官方文档写明，代码挂载的模块会在自己的 `OnEnable` 里自动分配
> `DefaultInputActions`，不需要你手动 assign），**没装也照常工作**。

**两条纪律**（踩了会出问题）：

1. **你自己的 EventSystem 必须早于"第一次开面板"存在**。这个判断**只在建根节点那一刻做一次** —— 如果你的 EventSystem 是后来才实例化 / 后来场景才加载的，
   框架已经先建了一个 → 场景里两个 EventSystem，Unity 会警告 `There are 2 event systems in the scene`，输入行为变得不确定。
2. **想自己全权管 EventSystem（例如自带 `InputActionAsset` 的预制体）**：把 `RevUISetting.AutoCreateEventSystem = false` 关掉，框架一行都不碰
   —— 但同样要遵守上面第 1 条（自己的那个必须早于第一次开面板）。

> 另外 `FindObjectOfType` **只找启用中的对象**：场景里的 EventSystem 若是禁用状态，框架会当成"没有"而再建一个
> （禁用状态的 EventSystem 本来也不收输入，所以通常没问题 —— 但别靠"运行时再把它启用"这种做法）。

**结论**：给 UI 用的 Canvas **不用摆、也不要摆**（框架的根节点是 `DontDestroyOnLoad` 常驻的，你再摆一个只会变成两套 UI 根，各排各的 `sortingOrder`，互相盖）；
EventSystem 是"**你有就听你的、没有就兜一个**"。

### 4.11 怎么开相机模式（框架已内置：两个标准预制体 + 三个配置字段）

相机模式不需要你改框架代码。标准模板放在 `Assets/Revolution/Resources/RevUIPrefab/`：

| 文件 | 内容 | 干什么用 |
|---|---|---|
| `RevUICanvas.prefab` | Canvas（`ScreenSpaceCamera`、sortingOrder `100`）+ CanvasScaler（1920×1080、match 0.5）+ GraphicRaycaster | 当"UI 根节点模板"：你可以在 Inspector 里调缩放/排序、加自己的背景层；★ **不要**往里放六个层级节点（框架自己建） |
| `RevUICamera.prefab` | Camera：**正交**、`Clear Flags = Depth only`、`Depth = 100`、`Culling Mask = UI` 层、近/远裁剪 `0.3 / 1000` | 当"UI 相机模板"：叠在主相机之上、只画 UI、不重复画 3D |

```csharp
// ① 最省事：两个路径都指上 —— 渲染模式自动跟随预制体里选的 ScreenSpaceCamera
RevUISetting.CanvasPrefabPath   = "RevUIPrefab/RevUICanvas";
RevUISetting.UICameraPrefabPath = "RevUIPrefab/RevUICamera";

// ② 或者不用预制体，代码里显式指定（Canvas 由框架代码建，但模式按你指定的来）
RevUISetting.CanvasMode          = RevUICanvasMode.ScreenSpaceCamera;
RevUISetting.UICamera            = myUiCamera;     // 你场景里的相机
RevUISetting.CanvasPlaneDistance = 100f;           // 必须落在相机近/远裁剪面之间

// ③ 什么都不配 = ScreenSpaceOverlay（默认值；不需要相机、永远最上层）
//    （想跟随 Canvas 预制体里选的模式，要显式设 Auto）
```

**相机的查找顺序**（找到即用）：
`RevUISetting.UICamera` → `UICameraPrefabPath` 预制体 → 场景里名为 `UICamera` / `[RevUICamera]` 的相机 → 按标准参数代码兜底建一台。

**图层是隐形前提**：相机 Culling Mask 只认 `RevUISetting.UILayerName`（默认 `"UI"`，即 Unity 第 5 号图层），
框架会把**根节点 / 六层挂点 / 每个面板实例 / 每个 Part / 遮罩**都设成这个图层 —— 你只要保证相机认它（示例相机预制体已经填好 `UI` 层）。

| 现象 | 多半是这个原因 |
|---|---|
| 面板开了，但屏幕上一个都没有 | 相机的 **Culling Mask 没包含 UI 图层** |
| UI 有透视变形、边缘被拉伸 | 相机不是**正交**（`orthographic = true`） |
| UI 显示正常，但整个界面**点不动** | 缺 `EventSystem`（见 4.10），或 `canvas.worldCamera` 为空 |
| Canvas 整个被裁掉 | `planeDistance` 不在相机近/远裁剪面之间 |
| 屏幕完全没有 UI | 相机被禁用 / 销毁 → 框架会**退回 Overlay 并报错** |

**兜底逻辑（保证不会"静默看不到 UI"）**：Camera 模式下如果一台相机都找不到，框架**报错 + 退回 `ScreenSpaceOverlay`**；
如果只是没配相机路径，它会按标准参数建一台并**告警**提醒你换成自己的那台。

> 这两份预制体放在 `Resources` 下，会被无条件打进包（都很小）且**不参与 AB 分包** —— 它们是框架自用的模板，
> 打包工具里不需要做任何配置（也**不用**给它们设 AB 名，它们不走资源系统）。

### 4.12 单 Canvas 优先，必要时才启用三 Canvas

**一句话总结：优先使用单 Canvas；只有实际项目在目标设备上测出 UI 合批开销已成为性能瓶颈、常规优化仍无法满足帧预算时，才考虑切换到三 Canvas。**

`RevUISetting.CanvasArchitecture` 默认是 `RevUICanvasArchitecture.Single`。这不只是兼容旧项目的默认值，也是**框架推荐的新项目起点**。框架提供 `Split` 作为有条件的性能优化选项，不要求每个项目都配置三 Canvas，更不是"有动态 UI 就必须拆"。

**① 为什么可能需要拆：两笔账**

| 开销 | 什么时候发生 | 波及范围 |
|---|---|---|
| 网格重建（Rebuild） | 控件自己变了：换文字 / 换图 / 改颜色 / 改尺寸 | 只重建变了的那个控件 —— 与 Canvas 怎么分**无关** |
| **重新合批（Rebatch）** | Canvas 下**任意一个**控件的网格或位置 / 缩放变了 | 该 Canvas 的**全部**控件重新排序、合并网格 |

单 Canvas 下，主界面 / HUD 上常驻的倒计时、血条、飘字、摇杆可能持续触发重新合批；**但可能不等于性能不达标**。如果目标设备上的耗时仍在预算内，拆 Canvas 只会增加管理和渲染成本，保持单 Canvas 即可。

**② 先优化单 Canvas，达不到目标再考虑切换**

1. **从单 Canvas 开始**：在目标设备、目标帧率和典型重负载场景（例如主界面常驻 HUD、弹窗出现、滚动列表）实测，记录 UI 相关 CPU 耗时、帧时间和 Draw Call。不要拿编辑器中的卡顿或"面板数量多"代替证据。
2. **先定位并优化具体界面**：检查是否有不必要的每帧文本 / 布局刷新、循环动画、未虚拟化的长列表和过多的可射线检测控件；让不显示的界面停止更新，减少不必要的 UI 写入。优化后按同样场景复测。
3. **只有确认 Canvas 重新合批仍是主要瓶颈，且实际帧时间超出项目预算时，才试用 `Split`**：把长期不变的 Scene 内容与常驻高频变化的 Scene 内容拆成不同面板，分别放到静态 / 动态画布，再测一次 CPU 与 Draw Call。如果收益不足或渲染成本反而上升，就继续用单 Canvas。

> **没有统一的"几个面板 / 多少控件就必须切换"阈值**：目标机型、目标帧率、UI 复杂度不同，结论由该项目的性能预算和实际测量决定。不是只要有倒计时、血条，就应该切换。

| | 单 Canvas（`Single`，推荐默认） | 三 Canvas（`Split`，性能瓶颈时按需开启） |
|---|---|---|
| 框架新建的 Canvas 数 | 1 | **3 个**（根/常用、静态、动态；不按面板增设） |
| 合批与 Draw Call | 跨区域有机会合批；单个动态控件可能让整张画布重新合批 | 常驻动静内容互不拖累；画布边界会减少跨区域合批机会，Draw Call 变化须实测 |
| 适合 | **绝大多数项目的起点；只要满足预算就继续使用** | 已定位到单 Canvas 合批开销超预算且常规优化无法解决的项目 |
| 业务改动 | 无 | 一行配置 + 给需要分离的 Scene 层面板写 `CanvasType` |

> 为什么三 Canvas 不按面板数量扩张：固定少量合批边界，使复杂度可控；但三 Canvas **不是性能保证**，同一画布内的动态内容仍会使该画布重新合批，是否值得拆必须用 Profiler 验证。预制体或业务自行添加 Canvas 时，场景 Canvas 总数可能超过框架新建的 3 个。

**③ 结构长什么样**（常用画布就是根 Canvas 本身，单 Canvas 只是"少建了两个画布"）

```text
[RevUIRoot]            常用画布 = 根 Canvas（CanvasScaler 只在这里）         sortingOrder = 根（默认 100）
  ├ [StaticCanvas]     静态画布：override 排序 + GraphicRaycaster          sortingOrder = 根 − 2
  ├ [DynamicCanvas]    动态画布：override 排序 + GraphicRaycaster          sortingOrder = 根 − 1
  └ Scene / Normal / Popup / Toast / Guide / Top     常用画布的六层挂点（与单 Canvas 完全一样）
```

- 两个子画布各自带 `GraphicRaycaster`：UGUI 的 Graphic 只登记到**离自己最近的 Canvas**，没有它里面的按钮就点不到；
- 子画布的着色器通道、排序层都跟根 Canvas 对齐（TMP 等要用的通道不丢）；
- 遮罩跟着"托着它的面板"所在的父节点走（必要时换父节点）。

**④ 谁盖谁：常用画布在最上面（这是整个设计里最关键的一条）**

```text
静态画布（根 − 2）  <  动态画布（根 − 1）  <  常用画布（根）
```

- **为什么常用画布必须在最上面**：弹窗遮罩、新手引导、Loading、断线重连都在常用画布里，它们必须盖住并**挡住**一切。
  如果动态画布排在最上面，弹窗打开时 HUD 的倒计时会压在弹窗上、摇杆还能被点到 —— 遮罩就形同虚设。
- **代价**：静态 / 动态画布整体排在常用画布之下，所以它们**只收 Scene 层**（主界面、HUD 这类"常驻底层"的界面）。
  其它层声明了会**自动放回常用画布，并按面板类型告警一次**。
  例子：一个 Top 层的 Loading 进度条声明成动态，如果真放进动态画布，就会被常用画布里的主界面盖住 —— 所以框架不照做。
- 同在 Scene 层时，视觉顺序是"静态 < 动态 < 常用"，同一个画布里才按打开顺序；"被盖住"通知、点遮罩关顶层都按这个顺序算。

**⑤ 怎么用：先保持默认，确需切换时再设置**

新项目**不用写任何 Canvas 架构配置**，`Single` 就是默认值；所有面板照常声明层级、打开和关闭。下面是**在第二步优化后仍不达标**的项目才需要的可选配置和画布划分：

```csharp
// 仅确认单 Canvas 的合批开销是瓶颈后，在第一次打开面板前设置；不设 = Single
RevUISetting.CanvasArchitecture = RevUICanvasArchitecture.Split;

[RevUIPanel(RevResPath.UI_Panel, RevUILayer.Scene, CanvasType = RevUICanvasType.Static)]
public sealed class MainBgPanel : RevUIPanel { ... }        // 主界面背景、固定框体

[RevUIPanel(RevResPath.UI_Panel, RevUILayer.Scene, CanvasType = RevUICanvasType.Dynamic)]
public sealed class BattleHudPanel : RevUIPanel { ... }     // 倒计时、血条、飘字、摇杆

[RevUIPanel(RevResPath.UI_Panel, RevUILayer.Normal)]        // 不写 CanvasType = 常用画布
public sealed class BagPanel : RevUIPanel { ... }
```

| 画布 | 放什么 | 别放什么 |
|---|---|---|
| 静态（`Static`） | 主界面背景、固定框体、装饰、打开后不变的按钮区 | 带倒计时、跳动红点、循环动画的东西 |
| 动态（`Dynamic`） | HUD 倒计时、血条、飘字、摇杆、跑马灯、循环动画 | 大块静态内容（会被动态内容拖着一起重算） |
| 常用（`Common`，默认） | 其余全部：二级界面、弹窗、Toast、引导、Loading、系统层 | — |

> 同一套面板代码两种架构都能跑：单 Canvas 下 `CanvasType` 直接忽略（不告警）。
> 一个主界面里既有静态背景又有动态 HUD 时，把它拆成两个 Scene 层面板（一个 Static、一个 Dynamic）一起打开即可。

**⑥ 代价与边界**

| 项 | 说明 |
|---|---|
| 切换时机 | 根节点创建时读一次；运行中要换架构，先 `RevUI.ShutdownAll()` 再打开面板 |
| 常用画布里的高频内容 | 弹窗里的倒计时、Toast、Loading 条仍会让常用画布重算 —— 这是"最多 3 个 Canvas"的取舍；它们多是短时出现，影响有限 |
| 静态面板的开关动画 | 打开 / 关闭时的 `PopIn` 等动画同样会让静态画布重算，但只在那一瞬；静态画布里**别放常驻循环动画** |
| 其它 Overlay Canvas | 框架占用 `[根 − 2, 根]`（默认 98 ~ 100）：项目里别的 Overlay Canvas（调试面板、SDK 弹窗）请避开这几个数 |
| 验收方法 | Profiler 的 UI 模块看 `Canvas.BuildBatch`（合批）与 `Canvas.SendWillRenderCanvases`（重建 / 布局）切换前后的耗时，Frame Debugger 看 Draw Call 数 —— 别凭感觉 |

**本节结论**

| 问题 | 答案 |
|---|---|
| 默认推荐哪种架构？ | **单 Canvas**。动态 UI 或面板较多都不是切换的充分理由，满足目标设备的帧预算就保持单 Canvas |
| 何时才切换？ | 先在目标设备实测并优化单 Canvas；若 `Canvas.BuildBatch` 仍是主要瓶颈且项目帧时间仍超预算，再设置 `RevUISetting.CanvasArchitecture = RevUICanvasArchitecture.Split` 做对照测试 |
| 三个 Canvas 分别放什么？ | 常用（默认，所有层级）/ 静态（Scene 层不变的内容）/ 动态（Scene 层高频变化的内容） |
| 三个画布谁在上面？ | 静态 < 动态 < 常用 —— 常用画布在最上面，弹窗遮罩和系统层才能盖住并挡住一切 |
| 为什么静态 / 动态只收 Scene 层？ | 它们整体排在常用画布之下；放别的层会被常用画布里的界面盖住，框架自动放回常用画布并告警 |
| 业务代码要改多少？ | 一行配置 + 面板特性里一个 `CanvasType`；不写就是常用画布，两种架构同一份代码 |

---

## 五、必须知道的八个坑

1. **预制体名和类名不一致**：默认约定是"预制体名 = 面板类名"。不一致时用特性第三参数写清楚，否则会报"预制体根节点上没有 XxxPanel 组件"。
2. **复用时忘了清残留**：控件引用不会重置、滚动位置不会自己回到顶部。写 `OnReuse()` —— 框架只替你清**数据**，界面上的残留只有你知道怎么清。
3. **事件没用 `owner: this` 注册**：框架关闭时会 `RemoveAllByOwner(this)`，但它只能摘"登记在你名下的"。用别的 owner 注册的（如某个服务）框架摘不掉，那类监听要自己按生命周期管。
4. **在 `OnRefreshView` 里发请求 / 改数据**：那就不是"落屏"而是逻辑了，会出现"刷新一次发一次请求"的死循环。请求放 `OnOpen` / 交互回调里。
5. **面板里直接 `Destroy(gameObject)`**：绕过管理器会让索引、池、资源引用对不上。关自己请用 `CloseSelf()`（= `RevUI.Close(this)`）。
6. **把飘字放在参与返回栈的层**：`Toast` 层不参与 `Back()`；如果自定义了面板又希望它不被返回键关掉，设 `InBackStack = false`。
7. **遮罩把不该挡的挡住了**：想"看一眼但不打断操作"的浮层，把 `Mask = RevUIMaskMode.None` 写清楚（`Auto` 在 Popup/Guide/Top 层默认是挡的）。
8. **池里实例占内存**：`KeepAlive` 的界面会一直留着一份实例（连同它端的 prefab 引用）。大界面（战斗内的全屏界面）用 `CacheMode = DestroyOnClose`，或把 `MaxCachedPanels` 调小。

---

## 六、API 速查

**门面 `RevUI`（业务唯一入口）**

| 分类 | API |
|---|---|
| 打开 | `Open<T>()`、`Open<T>(onOpened)`、`Open<T, TData>(data, onOpened)`、`OpenAsync<T>()`、`OpenAsync<T, TData>(data)` |
| 关闭 | `Close(panel)`、`Close<T>()`、`CloseAll(layer?)`、`CloseGroup(group)`、`Back()`、`ShutdownAll()` |
| 查询 | `Get<T>()`、`IsOpen<T>()`、`TopOf(layer)` |
| 预热 | `Preload<T>(onLoaded)` |
| 扩展 / 诊断 | `RegisterAutoEvent<T>(bind)`、`DumpStats()` |

**面板 `RevUIPanel` / `RevUIPanel<TData>`**

| 分类 | 成员 |
|---|---|
| 元数据 | `Meta`、`PanelKey`、`PrefabRoot`、`PrefabName`、`Layer`、`CanvasType`、`State`、`IsOpened`、`IsCovered` |
| 数据 | `DataObject`、`SetData(object)`；泛型版：`Data`、`SetData(TData)` |
| 必写钩子 | `OnBindView()`；泛型版另加 `OnRefreshView()` |
| 可选钩子 | `OnInit` / `OnOpen` / `OnReuse` / `OnDataChanged` / `OnCovered` / `OnClose` / `OnRelease` |
| 交互回调 | `OnClick` / `OnToggleChanged` / `OnSliderChanged` / `OnInputChanged` / `OnInputEndEdit` / `OnDropdownChanged` / `OnScrollChanged` |
| 转场 | `PlayOpenTransition(onDone)` / `PlayCloseTransition(onDone)` |
| 便捷 | `RefreshView()`、`CloseSelf()`、`FindPart<T>()`、`RefreshParts()` |

**Part `RevUIPart`**

| 分类 | 成员 |
|---|---|
| 元数据 | `Meta`、`PartKey`、`Host`、`HostPanel`、`IsOpened`、`IsPrefabPart` |
| 钩子 | `OnPartInit` / `OnBindView`（必写）/ `OnPartOpen` / `OnPartRefresh` / `OnPartClose` |
| 交互 | 与面板同名的七个回调；`NotifyHost()` 通知宿主 |
| 创建 / 关闭 | `RevUIPart.Create<T>(host, parent, onCreated)`、`ClosePart(destroy = true)` |

**声明**

| 特性 | 用在哪 |
|---|---|
| `[RevUIPanel(root, layer, name?)]` | 面板类（必写；`root` 是资源根目录段） |
| `[RevUIPart(root, name?)]` | Part 类（只在"独立预制体、要跨面板复用"时写） |
| `[RevBind(path?)]` | 面板/Part 的字段（不写路径就按字段名找节点） |

---

## 七、验证结果

分两层验：**纯逻辑内核在工程外跑真断言**，**整程序集用 Unity 真实引用编译**。

| 验证项 | 结果 |
|---|---|
| 工程外行为断言（路径解析 / 资源名默认 / 遮罩按层推断 / 字段名→节点名 / 显式路径 / 继承字段收集 / "只挂重写的回调" / 元数据与绑定计划缓存 / 忘了写特性的报错文案 …） | **65 项全部通过** |
| 整个 `Revolution.Runtime` + `Revolution.Editor` 程序集（Unity 真实引用 + `UNITY_EDITOR`） | **0 错误 0 新增警告** |
| `LangVersion 9.0`（对齐 Unity 2022.3） | 编译通过 |
| 三 Canvas 改动：`Revolution.Runtime` 用 Unity 2022.3 引擎引用重编（玩家配置 + `UNITY_EDITOR` 两套） | **0 错误**，UI 模块 0 警告 |
| 画布归属与排序规则工程外断言（单 Canvas 一律常用 / Scene 层静态动态生效 / 其它层放回常用并标记告警 / 未知枚举兜底 / 排序号 根−2 < 根−1 < 根 / 常用画布在最上） | **12 项全部通过** |

> 三 Canvas 的**运行期收益**取决于具体界面，需要在 Unity 里按 4.12 第 ⑥ 条的方法用 Profiler 对比验收。

**断言跑在"要提交的那份源码"上**（通过 csproj 直接链接真实文件，不是复制品），它验的都是"写的时候看不错、跑起来才知道"的规则：

- 特性里写 `"UI/Panel/"`、`"\\UI\\Panel\\"`、`"  UI/Panel  "` → **都得到 `UI/Panel`**（不会拼出双斜杠）；
- 省略资源名 → 取**类名**；显式写了 → 以显式为准；`Key` = `根目录 + 资源名`；
- `Popup` 层默认 `ClickBlock`、`Toast` 层默认不挡；显式声明永远优先；
- `_btnClose` / `m_title` / `__x` → 节点名 `btnClose` / `title` / `x`；全是下划线时原样返回（不会给出空名字）；
- 只重写了 `OnScrollChanged` → **只**挂滚动监听（`WantsClick == false`），一个都没重写 → 连扫子节点都省了；
- 基类与派生类里的 `[RevBind]` 字段**都**会被收集，且不重复；
- 元数据与绑定计划**被缓存**（每类型只解析/反射一次）。

**这一轮验证各抓到 1 个真错误**（都已修）：

1. 断言工程编译时就报了 `CS0103`：绑定条目里调 `RevUIBindPlan` 的静态方法漏写类名 —— 纯内核抽出来后，**Unity 还没打开就先红了一次**；
2. Unity 引用编译时报 `RevTaskSource<>` 类型不存在：`RevTask<T>.CreateSource()` 返回的是 `RevTaskCompletionSource<T>` —— 门面里我按印象写错了类型名，编译把它拦下了。

---

## 八、附：关键文件索引

| 文件 | 职责 |
|---|---|
| `Runtime\RevUISystem\Core\RevUIDefine.cs` | 公共词汇：层级 / 状态 / 缓存模式 / 遮罩模式 / Canvas 架构与画布类型（**纯 C#**，遮罩按层推断、面板进哪个画布、画布排序号的规则也在这） |
| `Runtime\RevUISystem\Core\RevUIAttributes.cs` | 声明式特性：`[RevUIPanel]` / `[RevUIPart]` / `[RevBind]`（**纯 C#**） |
| `Runtime\RevUISystem\Core\RevUIPanel.cs` | **面板基类**：生命周期、状态、绑定触发、数据入口、自动摘事件、转场钩子 |
| `Runtime\RevUISystem\Core\RevUIPanel.Generic.cs` | `RevUIPanel<TData>`：强类型数据 + 强制实现 `OnRefreshView` |
| `Runtime\RevUISystem\Core\RevUIPart.cs` | **Part 基类**：依附单元、宿主契约、预制体 Part 的创建/销毁与资源引用配对 |
| `Runtime\RevUISystem\Interfaces\IRevUIPartHost.cs` | Part 的宿主契约（Part 因此能跨面板复用） |
| `Runtime\RevUISystem\Interfaces\IRevUIUserEvents.cs` | 控件事件 → 宿主回调 的内部转发口 |
| `Runtime\RevUISystem\Facade\RevUI.cs` | **业务唯一入口**：打开 / 关闭 / 查询 / 预热 / 扩展 / 诊断 |
| `Runtime\RevUISystem\Implementation\RevUIManager.cs` | 管理器：五级查找、在途合并、面板进哪个画布、层级排序（先按画布再按打开顺序）、互斥组、返回栈、遮罩与"被盖住"、诊断 |
| `Runtime\RevUISystem\Implementation\RevUIPanelPool.cs` | **UI 专用实例池**（不套用通用对象池）+ 池快照 |
| `Runtime\RevUISystem\Implementation\RevUIRoot.cs` | UI 根节点：代码建 Canvas/六层挂点/遮罩宿主 + 延迟销毁 + 每帧维护 |
| `Runtime\RevUISystem\Implementation\RevUIPopupMask.cs` | 弹窗遮罩：按层自动铺透明挡板、自动摆位、点遮罩关顶层 |
| `Runtime\RevUISystem\Support\RevUIPanelMeta.cs` | 面板/Part 元数据解析（**纯 C#**，带缓存；报错文案也在这） |
| `Runtime\RevUISystem\Support\RevUIBindPlan.cs` | 绑定计划（**纯 C#**）：字段↔节点↔类型、"这个类重写了哪些回调" |
| `Runtime\RevUISystem\Support\RevUIBinder.cs` | 绑定器：按计划赋值控件、安装按节点名分发的监听、自定义控件扩展口 |
| `Runtime\RevUISystem\Support\RevUISetting.cs` | 全局配置 + 统一日志出口（含异常隔离 `Guard`） |
| `Runtime\RevUISystem\Support\RevUIUnityHooks.cs` | 进 Play 前清索引（关掉"域重载"时也能正常反复运行） |
| `Runtime\RevUISystem\Animation\RevUIEase.cs` | 缓动曲线（**纯 C#**）：17 种，边界恒等 / 单调（可脱机断言） |
| `Runtime\RevUISystem\Animation\RevUIAnimSpec.cs` | 动画规格与预设（**纯 C#**）：三通道掩码（透明度 / 缩放 / 位移）+ 14 个预设（Fade · Pop · Scale · Slide×四方向；另有 `None` = 不做动画） |
| `Runtime\RevUISystem\Animation\RevUIAnimEngine.cs` | **动画内核**（**纯 C#**）：采样模型 + 帧余量结转 + 循环往返 + 运行时对象池 + 版本号句柄 |
| `Runtime\RevUISystem\Animation\RevUIAnimTarget.cs` | 采样落点：CanvasGroup 自动补、基准值只取一次、透明度 / 缩放 / 位移写入 |
| `Runtime\RevUISystem\Animation\RevUIAnimDriver.cs` | 每帧推进（复用 `RevMono`；`unscaledDeltaTime` = 暂停也能播；全局倍速 `GlobalSpeed`） |
| `Runtime\RevUISystem\Animation\RevUIWidgetFeedback.cs` | 控件反馈：悬停放大 + 按下缩小（旧框架 `AddButtonAnimation` 的同款能力） |
| `Runtime\RevUISystem\Animation\RevUIAnim.cs` | **动画门面**：FadeIn / PopIn / SlideIn / ScaleTo / Breathe / AddHoverFeedback / StopAllOf |
| `Resources\RevUIPrefab\RevUICanvas.prefab` | **相机模式**的根节点模板（Canvas + Scaler + Raycaster；`RevUISetting.CanvasPrefabPath` 用） |
| `Resources\RevUIPrefab\RevUICamera.prefab` | **相机模式**的 UI 相机模板（正交 / Depth clear / depth 100 / 只渲染 UI 层；`RevUISetting.UICameraPrefabPath` 用） |

---

## 九、本期没做的（可后续扩展）

| 项 | 说明 |
|---|---|
| 面板清单窗口（编辑器工具） | 列出所有面板的"类名 / 资源路径 / 层级 / 缓存方式"，一键校验"类名 ↔ 预制体"是否对得上、路径是否真的存在（可复用打包工具那套扫描） |
| 按模板一键创建面板脚本 | 从选定目录生成"面板类 + 数据类 + 基础骨架"，省掉手写特性与钩子 |
| MessageBox（按参考实现那份文档的结论做） | 队列 + 优先级 + 去重 + "按钮只转发事件"；它是标准 `Panel`（独立遮罩与层级），自定义内容用"空壳 + Content 插槽"的装饰器做法 |
| 常用 Part | 无限滚动列表、页签组、通用奖励格（都是"写一次多处复用"的典型） |
| 面板资源分组可配 | 现在面板预制体统一走 `RevResGroup.UI`；将来可以按面板声明自己的资源分组 |
| 打开优先级 | 现在打开是即时的；可以加 `RevUI.OpenAsync` 的优先级参数，让"读条期预加载"与"临时弹窗"排队更合理 |

---

*对应代码版本：`Assets\Revolution\Runtime\RevUISystem\`（26 个 `.cs` / 5,992 行；其中动画库 7 个在 `Animation\` 下）。*
