# UI 系统 · 使用说明（手把手教程）

> 使用说明 · 从零到能写业务
>
> 这份文档只回答一个问题：**我该怎么用它？**（一个特性声明面板、一行打开、事件自动绑到方法）
> 读完你能做到：3 分钟写出第一个面板 · 分清 7 个生命周期回调谁先谁后 · 会给面板传数据 · 知道返回栈与层级怎么用
>
> ⚡ **想先看跑起来的效果**：`Assets/Revolution.Demo/RevUISystem.Demo/`（一个场景把本文每一章都演了一遍 ——
> 三种接法 / 生命周期 / 全部 API / 带数据 / 六层与遮罩 / 动画 / Part；左侧按章节点，右侧是共享日志）。

## 目录

- 〇、它是干什么的
- 一、3 分钟写出第一个面板
- 二、面板的生命周期（谁先谁后）
- 三、"我要做 X" 对照表（全部 API）
- 四、带数据的面板（一个面板多处复用）
- 五、层级 / 遮罩 / 返回栈
- 六、新手最容易踩的 6 个坑
- 七、相关文档

---


## 〇、它是干什么的

*先花 30 秒建立直觉，再动手写。*

> [!TIP]
> **只学 3 件事就能干活（真的）**
>
> // ① 声明一个面板：特性写"预制体在哪个目录、哪个名字"
> [RevUIPanel("UI/Bag", RevUILayer.Normal, "BagPanel")]
> public class BagPanel : RevUIPanel
> {
>     protected override void OnBindView() { }        // ② 唯一必须实现的方法：这里做绑定
>     protected override void OnClick(string nodeName) // ③ 点哪个按钮就走这里（不用手动挂监听）
>     {
>         if (nodeName == "BtnClose") RequestClose();
>     }
> }
> 
> // 打开 / 关闭（业务里就这两行）
> RevUI.Open<BagPanel>();
> RevUI.Close<BagPanel>();
> 带数据的、异步打开、返回栈、按组关闭……都在第三节的对照表里，**用到再查**。

> **人话** UI 系统 = 你只写"面板类 + 预制体名"，剩下的事（加载预制体、挂到哪个 Canvas、层级顺序、遮罩、关闭时回收还是销毁）它全包。
> 面板自己就是 MonoBehaviour，但**不要手动 Instantiate** —— 一律 `RevUI.Open`，否则层级、缓存、事件都接不上。

| 它替你解决的问题 | 怎么做的 |
|---|---|
| 面板预制体路径到处硬编码 | 写在特性里：`[RevUIPanel("UI/Bag", RevUILayer.Normal, "BagPanel")]`（目录, 层级, 预制体名），只有一处 |
| 按钮监听重复挂/忘记卸 | 节点名回调：`OnClick(nodeName)` / `OnToggleChanged` / `OnSliderChanged`… 由框架统一分发 |
| 打开顺序、层级盖住别人 | `RevUILayer` 四层（Scene / Normal / Popup / Toast）+ 同层按打开顺序 |
| 关了再开卡一下 | **面板池**：默认缓存复用，支持 `[RevUIPanel(..., CacheMode = …)]` 调策略 |
| 弹窗忘了加遮罩、点击穿透 | `Mask = RevUIMaskMode.Auto`：弹窗默认带遮罩挡住下面操作 |
| 返回键该关哪个面板 | `InBackStack` + `RevUI.Back()`：Toast 之类不进返回栈 |

---


## 一、3 分钟写出第一个面板

*准备：一个预制体放在资源根目录的 `UI/Bag/BagPanel.prefab`。*


```csharp
// ① 新建脚本 BagPanel.cs —— 就这么短，能跑
using UnityEngine;

[RevUIPanel("UI/Bag", RevUILayer.Normal, "BagPanel")]
public class BagPanel : RevUIPanel
{
    // RevUIPanel 是抽象类，这个方法必须实现 —— 做"拿组件、填初始内容"的事
    protected override void OnBindView()
    {
        // 例：拿到预制体里名为 Title 的文本并设置
        // (按你项目的绑定方式：RevBind 特性 / 自己 GetComponentInChildren)
    }

    protected override void OnOpen()
    {
        // 每次打开都会走这里：刷新数据、播开场动画
    }

    // 点了预制体里名为 BtnClose 的按钮（名字对得上就会进来）
    protected override void OnClick(string nodeName)
    {
        switch (nodeName)
        {
            case "BtnClose": RequestClose(); break;
            case "BtnSort":  SortBag();      break;
        }
    }
}

// ② 业务里打开它（哪都能调，不用挂物体）
RevUI.Open<BagPanel>();

// ③ 打开后拿到实例做点事
RevUI.Open<BagPanel>(panel => panel.RefreshView());

// ④ 关闭（面板内部也可以 RequestClose()）
RevUI.Close<BagPanel>();
```

> [!NOTE]
> **就这么点东西**
> 预制体放对目录 + 特性写对名字 = 能打开

---


### 控件事件的三种接法（挑一种就用，能混用）

同一个控件的事件，框架给三条路 —— **都可用、可混用**，按场景挑：

| 接法 | 长什么样 | 什么时候选它 |
|---|---|---|
| ① **方法特性**（最省事） | `[RevButtonClick("btnStart")] void OnStart() => StartGame();` | "点一下做一件事"：不写绑定字段、不重写回调，方法上标个特性就行 |
| ② **按节点名分发** | `protected override void OnClick(string nodeName) { … }` | 一个面板上按钮很多、想集中处理（`switch (nodeName)`） |
| ③ **绑字段 + 自己挂监听**（最灵活） | `[RevBind] Button _btn;` → `_btn.onClick.AddListener(...)` | 要用 UGUI 的其它回调（拖拽 / 悬停…）或接第三方控件 |

方法特性一共九个（标在面板 / Part 的**任意方法**上），覆盖全部控件事件：

```csharp
// 控件名 = 节点名（区分大小写）；写多级路径时只取最后一段（"Top/btnStart" 等价于 "btnStart"）
[RevButtonClick("btnStart")]       void OnStart()      => StartGame();      // 点击
[RevButtonLongPress("btnSkill")]   void OnSkillHold()  => ShowSkillTip();   // 长按（按住 ≥ 0.5s 后松开）
[RevButtonLoosen("btnMove")]       void OnMoveUp()     => StopMove();       // 松开（指针在控件上抬起）
[RevToggleChanged("tglSound")]     void OnSound(bool on)              => SetSound(on);
[RevSliderChanged("sldVolume")]    void OnVolume(float v)             => SetVolume(v);
[RevInputChanged("inpName")]       void OnName(string text)           => Preview(text);   // 单参数 = 文本
[RevInputEndEdit("inpName")]       void OnNameDone(string text)       => Submit(text);
[RevDropdownChanged("ddlQuality")] void OnQuality(int index)          => SetQuality(index);
[RevScrollChanged("scrollList")]   void OnScrolled(float x, float y)  => LoadMore(y);

// 同一个控件可以挂多个方法（都会被调用）；想同时要"节点名 + 值"就用两参数 / 三参数的形状
[RevButtonClick("btnBuy")]         void OnBuy(string nodeName)              => Buy(nodeName);
[RevToggleChanged("tglSound")]     void OnSound2(string nodeName, bool on)  => Log(nodeName, on);
```

| 事件 | 可用的参数形状（**只支持这些**，其它在装配时报错） |
|---|---|
| 点击 / 长按 / 松开 | `()` · `(string nodeName)` |
| Toggle 变化 | `()` · `(bool value)` · `(string nodeName, bool value)` |
| Slider 变化 | `()` · `(float value)` · `(string nodeName, float value)` |
| 输入框文本变化 / 结束编辑 | `()` · `(string text)` · `(string nodeName, string text)` |
| Dropdown 变化 | `()` · `(int index)` · `(string nodeName, int index)` |
| ScrollRect 滚动 | `()` · `(float x, float y)` · `(string nodeName, float x, float y)` |

> [!WARNING]
> **六条要记住的**
> · **长按的判定**：按住时长 ≥ `RevUISetting.ButtonLongPressSeconds`（默认 0.5 秒），**松开时**触发长按；同一次操作也会触发"松开"（不会和"点击"抢同一瞬间）。
> · **长按 / 松开只对 Button 生效**（UGUI 的 Button 本身不报这两个事件，框架给交互节点挂了一个小继电器）；自研控件用 `RevUI.RegisterAutoEvent<T>((d, c) => { … d.LongPress() … d.Loosen() })` 接进来。
> · **控件名写错会当场报错**：第一次装配就打"找不到名为 xxx 的节点"，不会静默成"点了没反应"。
> · 同一次事件里，"重写的 `OnToggleChanged(节点名, 值)`"这类回调和"方法特性"**两条路都会走到** —— 同一件事只放一处做。
> · **输入框那两个特性的单个 `string` 参数是"文本"**（不是节点名）—— 节点名靠特性声明，用不上；两个都要就写 `(string nodeName, string text)`。
> · **高频事件建议用重写回调**：特性走反射调用（每次触发一次小分配）。按钮 / Toggle / Dropdown 这类低频事件完全无所谓；**ScrollRect 滚动**与**输入框每次敲键**较频繁，若在意 GC 就重写 `OnScrollChanged` / `OnInputChanged`（直调、零分配）—— 两种写法可以并存。

## 二、面板的生命周期（谁先谁后）

*只要记住"创建一次、打开很多次"这一点，其余照着图用。*

OnInit → OnBindView → OnOpen → OnReuse（复用打开时） → OnRefreshView → OnCovered(true/false) → OnClose → OnRelease

| 回调 | 什么时候 | 该放什么 |
|---|---|---|
| `OnInit()` | 面板对象**第一次**创建时，一次 | 只做一次的初始化（缓存组件引用） |
| `OnBindView()` **（必须实现）** | 紧随 OnInit，一次 | 绑定 UI 元素、挂必要的内部结构 |
| `OnOpen()` | **每次**打开 | 刷新数据、播开场动画（最常用） |
| `OnReuse()` | 从池里**复用**打开时 | 想区分"全新"与"复用"时用（一般不需要） |
| `OnRefreshView()` | 你调 `RefreshView()` 时 | 只重刷显示内容（不动结构） |
| `OnCovered(bool)` | 上面盖了别人 / 别人关了 | 被盖住时停特效、暂停刷新（省性能） |
| `OnClose()` | 关闭时（对象还在池里） | 停协程/计时器、退订事件、**释放资源** |
| `OnRelease()` | 对象真正销毁时 | 清理非托管/静态引用（少见） |

**自定义开场 / 关闭动画**

*想让"打开动画播完再算打开完"，重写这两个方法即可*

protected override void
PlayOpenTransition(Action onDone)  {
/* 播动画，结束后 */
onDone?.Invoke(); }
protected override void
PlayCloseTransition(Action onDone) { onDone?.Invoke(); }
> 不重写就立即算完成（默认实现就是直接回调）。


---


## 三、"我要做 X" 对照表（全部 API）

*入口统一是 `RevUI`（动画入口是 `RevUIAnim`，见第五节"动画"一节）。所有泛型参数都要求 `T : RevUIPanel`。*

| 我想… | 这么写 |
|---|---|
| 打开（最常用） | `RevUI.Open<BagPanel>()` |
| 打开并拿到实例 | `RevUI.Open<BagPanel>(p => p.xxx)` |
| 打开并传数据 | `RevUI.Open<BagPanel, BagData>(data)` |
| 异步打开（可用 await） | `T p = await RevUI.OpenAsync<BagPanel>()` |
| 关闭 / 关闭某类型 | `RevUI.Close(panel)` · `RevUI.Close<BagPanel>()` |
| 关掉某一层 / 某一组 | `RevUI.CloseAll(RevUILayer.Popup)` · `RevUI.CloseGroup("Bag")` |
| 返回上一级（响应返回键） | `RevUI.Back()` |
| 全部关掉（切场景/回登录） | `RevUI.ShutdownAll()` |
| 判断是否开着 / 拿实例 | `RevUI.IsOpen<BagPanel>()` · `RevUI.Get<BagPanel>()` |
| 看某层最上面是谁 | `RevUI.TopOf(RevUILayer.Popup)` |
| 提前加载（避免首次打开卡） | `RevUI.Preload<BagPanel>()` |
| 调试：看当前所有面板 | `Debug.Log(RevUI.DumpStats())` |
| 给面板 / Part 加动效（一行） | `protected override RevUIAnimPreset ShowAnimation => RevUIAnimPreset.PopIn;` |
| 给任意控件加动效 | `RevUIAnim.PopIn(icon, owner: this)` · `RevUIAnim.SlideIn(this, RevUISlideDirection.Top)` |
| 跳过动画（直接到终态） | `RevUIAnim.ApplyEnd(panel, RevUIAnimPreset.PopIn)` |
| 停掉某个对象的动画 | `RevUIAnim.StopAllOf(this)` |
| 关掉全部动效（配置） | `RevUISetting.UIAnimationsEnabled = false` |
| 调试：看正在播的动画 | `Debug.Log(RevUIAnim.Dump())`（`RevUIAnim.ActiveCount` = 正在播几个） |

> [!WARNING]
> **关闭 ≠ 销毁**
> Close
> ShutdownAll()
> CacheMode
> 不要依赖 OnRelease 做常规清理
> OnClose

---


## 四、带数据的面板（一个面板多处复用）

*同一个界面既要显示"英雄详情"又要显示"道具详情"时，用泛型面板传数据。*


```csharp
// ① 数据类随便定义（class / struct 都行）
public class DetailData { public int Id; public string Title; }

// ② 面板继承 RevUIPanel<TData>：重写 OnDataChanged 收数据
[RevUIPanel("UI/Common", RevUILayer.Normal, "DetailPanel")]
public class DetailPanel : RevUIPanel<DetailData>
{
    protected override void OnBindView() { }                 // 仍需实现（做绑定）
    protected override void OnRefreshView() { }               // 用 _data 刷新显示
    protected override void OnDataChanged(DetailData oldData, DetailData newData)
    {
        // 数据换了：刷新界面（最常写的地方）
    }
}

// ③ 打开时传进去
RevUI.Open<DetailPanel, DetailData>(new DetailData { Id = 1001, Title = "亚瑟" });

// ④ 已经开着时再传一份新数据（不会重开面板）
RevUI.Get<DetailPanel>()?.SetData(new DetailData { Id = 1002, Title = "妲己" });
```

> [!TIP]
> **为什么要泛型面板**
> object
> 数据从特性/框架外进来，界面只管显示。

---


## 五、层级 / 遮罩 / 返回栈

*都在特性上声明，不用写代码。*

| 层级 | 放什么 | 特点 |
|---|---|---|
| `RevUILayer.Scene` | 主界面、大厅、全屏场景面板 | 最底层，会被普通界面盖住 |
| `RevUILayer.Normal` | 二级界面：背包、商店、设置 | 默认层级 |
| `RevUILayer.Popup` | 确认框、奖励结算 | **默认带遮罩**挡住下面的操作，进返回栈 |
| `RevUILayer.Toast` | 飘字、跑马灯 | 不挡操作、**不参与返回栈** |

**特性上还能配什么**

*一个面板的全部"外观策略"集中在这一行，不用散在代码里*

[RevUIPanel(
"UI/Common"
, RevUILayer.Popup,
"ConfirmDialog"
,
            CacheMode   = RevUICacheMode.Unspecified,
// 缓存策略（默认按框架规则）
Mask        = RevUIMaskMode.Auto,
// 遮罩：弹窗默认自动加
ExclusiveGroup =
"Dialog"
,
// 互斥组：同组只留一个
InBackStack =
true
)]
// 是否进返回栈（Back() 能关它）
> 这些字段都是可选的，不写就用默认值（默认值见 `RevUIPanelAttribute` 的字段声明）。


> [!NOTE]
> **返回键怎么接**
> RevUI.Back()
> false

---


### UI 根 Canvas 从哪来（默认：加载框架自带预制体）

> **架构建议：先使用单 Canvas。** 默认 `RevUISetting.CanvasArchitecture = RevUICanvasArchitecture.Single`，无需额外设置；即使面板里有动画、倒计时或滚动内容，只要在目标设备上满足帧预算，就继续使用单 Canvas。只有定位到 Canvas 合批确实成为瓶颈，且常规优化后仍不达标，才按下文步骤评估三 Canvas。

- **默认渲染模式是 `ScreenSpaceOverlay`** ✓（不需要相机、UI 永远最上层）—— 要改就设 `RevUISetting.CanvasMode`；
- 框架**默认加载** `Resources/RevUIPrefab/RevUICanvas.prefab` 来渲染 ✓（Overlay / 1920×1080 / match 0.5 / sortingOrder 100）；
- **载不到就代码兜底** ✓：预制体缺失或路径写错时，框架自己建 Canvas + CanvasScaler + GraphicRaycaster，并打一条 Warning 说明原因 —— 不会出现"整屏 UI 起不来"；
- 想改用**相机模式**（UI 可被 3D 遮挡 / 能进 RenderTexture）：

```csharp
RevUISetting.CanvasMode          = RevUICanvasMode.ScreenSpaceCamera;
RevUISetting.UICameraPrefabPath  = "RevUIPrefab/RevUICamera";   // 默认就是这个；也可直接 RevUISetting.UICamera = cam
RevUISetting.CanvasPlaneDistance = 100f;
```

| 你想… | 怎么设 |
|---|---|
| 用框架自带的 Canvas（默认） | 什么都不用做 |
| 用自己的 Canvas 预制体 | `RevUISetting.CanvasPrefabPath = "你的目录/你的Canvas"` |
| 完全用代码建（不依赖任何资源） | `RevUISetting.CanvasPrefabPath = string.Empty` |
| 在预制体里自己调渲染模式 | `RevUISetting.CanvasMode = RevUICanvasMode.Auto`（Auto = 跟随预制体） |
| UI 要被 3D 挡住 / 进 RenderTexture | 见上面那三行 |
| 六层挂点放哪 | 框架自己建（预制体里**不要**放六个层级节点） |

### 什么时候才切换到三 Canvas？

**优先坚持默认的单 Canvas**，不要仅因为有倒计时、动画或多个面板就切换。按以下顺序决策：

1. 在目标机型的典型场景中用 Profiler 测量 UI 合批（`Canvas.BuildBatch`）、重建 / 布局（`Canvas.SendWillRenderCanvases`）与总帧时间，记录帧率、Draw Call 和项目自己的帧预算。
2. 若超预算，先解决无意义的逐帧文本 / 布局更新、过多的射线检测、持续运行的动画以及长列表没有虚拟化等问题，按**同一测试条件**复测。若达到目标，继续使用单 Canvas。
3. 只有确定 **Canvas 合批仍是主要瓶颈**，且单 Canvas 优化后仍不达标，才在**第一次打开面板之前**启用三 Canvas：

```csharp
RevUISetting.CanvasArchitecture = RevUICanvasArchitecture.Split;

[RevUIPanel("UI/Main", RevUILayer.Scene, CanvasType = RevUICanvasType.Static)]
public sealed class MainBackgroundPanel : RevUIPanel
{
    protected override void OnBindView() { }
}

[RevUIPanel("UI/Main", RevUILayer.Scene, CanvasType = RevUICanvasType.Dynamic)]
public sealed class MainHudPanel : RevUIPanel
{
    protected override void OnBindView() { }
}
```

`Static` 放常驻且基本不变的 Scene 内容，`Dynamic` 放常驻且频繁变化的 Scene 内容；`Common`（默认）放其余所有面板。现有混合静态/动态内容的预制体若要分到两个画布，需要拆成两个 Scene 面板，**只配置一个属性不会自动把面板内部控件分离**。`Normal` / `Popup` / `Toast` / `Guide` / `Top` 声明成 `Static` 或 `Dynamic` 会被放回 `Common` 并告警，以保证弹窗和引导遮罩位于最上层。切换后继续测 CPU 合批和 Draw Call；没有改善就退回单 Canvas。详见[《架构解析》第五章 · 决策 11](UI系统架构解析.md)。

### 面板 / Part / 控件的动画（一行加动效，不依赖 DOTween）

框架自带一套 **UI 专用**的轻量动画库（代码在 `Runtime\RevUISystem\Animation\`，入口 `RevUIAnim`）：零第三方依赖 ✓、每帧零 GC ✓、掉帧不改变动画总时长 ✓（采样模型 + 帧余量结转）。

**面板：一行预设属性**（显示动画播完才算"打开完成"✓，隐藏动画播完才真正回收 ✓）：

```csharp
[RevUIPanel("UI/Panel")]
public sealed class BagPanel : RevUIPanel<BagData>
{
    protected override RevUIAnimPreset ShowAnimation => RevUIAnimPreset.PopIn;    // 打开时自动播
    protected override RevUIAnimPreset HideAnimation => RevUIAnimPreset.PopOut;   // 关闭时自动播（播完才关）

    // 想自己掌控节奏（与预设并存；★ 结束时必须调 onDone）
    protected override void PlayOpenTransition(Action onDone)
        => RevUIAnim.SlideIn(this, RevUISlideDirection.Top, 0.25f, onDone, owner: this);
}
```

**Part** 同理（重写 `ShowAnimation` ✓）。**任意控件**直接用门面（面板自己 / Image / Text / Button / RectTransform / CanvasGroup 都能传 ✓）：

```csharp
RevUIAnim.FadeIn(icon);                                        // 淡入
RevUIAnim.SlideIn(this, RevUISlideDirection.Top);              // 从上滑入
RevUIAnim.ScaleTo(icon, 1.2f, 0.12f);                          // 缩放到 1.2 倍（相对基准缩放）
RevUIAnim.FadeTo(icon, 0.5f);                                  // 淡到半透明
RevUIAnim.Breathe(tipIcon);                                    // 呼吸（透明度 0.45 ↔ 1 来回）
RevUIAnim.Breathe(tipIcon, useScale: true);                    // 换成缩放呼吸
RevUIAnim.AddHoverFeedback(btnClose);                          // 按钮：悬停放大 + 按下缩小
RevUIAnim.Play(icon, RevUIAnimPreset.PopIn, 0.3f, () => Tip("播完"), owner: this);
RevUIAnim.ApplyEnd(panel, RevUIAnimPreset.PopIn);              // 跳过动画：直接落到终态
RevUIAnim.StopAllOf(this);                                     // 停掉这个对象的全部动画
```

| 预设（共 14 个） | 效果 |
|---|---|
| `FadeIn` / `FadeOut` | 淡入 / 淡出（默认 0.18s） |
| `PopIn` / `PopOut` | 淡入 + 缩放回弹 / 淡出 + 缩小（默认 0.25s，**面板默认手感**） |
| `ScaleIn` / `ScaleOut` | 只做缩放（0.9 ↔ 1） |
| `SlideInFromTop` / `Bottom` / `Left` / `Right` | 从四个方向滑入（默认 0.28s） |
| `SlideOutToTop` / `Bottom` / `Left` / `Right` | 往四个方向滑出 |
| `None` | 不做动画（默认值；行为与没有动画库时完全一致） |

**门面 API 一览**（真实签名，可直接抄）：

| 我想… | 这么写 |
|---|---|
| 按预设播 | `Play(控件, 预设, 时长 = 0, 完成回调 = null, owner = null)` → 返回 `RevUIAnimHandle` |
| 淡入 / 淡出 / 弹入 / 弹出 / 缩放进出 | `FadeIn` · `FadeOut` · `PopIn` · `PopOut` · `ScaleIn` · `ScaleOut`（控件, 时长 = 0, 完成回调 = null, owner = null） |
| 四方向滑入 / 滑出 | `SlideIn(控件, RevUISlideDirection.Top, 时长 = 0, …)` · `SlideOut(控件, 方向, …)` |
| 到某个目标值 | `ScaleTo(控件, 1.2f, 时长 = 0.12f, ease = RevUIEase.CubicOut, …)` · `FadeTo(控件, 0.5f, …)` |
| 呼吸 / 闪烁（无限往返） | `Breathe(控件, 时长 = 0.6f, from = 0.45f, to = 1f, useScale = false)` |
| 控件反馈（悬停 / 按下） | `AddHoverFeedback(控件, hoverScale = 1.06f, pressScale = 0.94f, 时长 = 0.09f)` |
| 立即到终态（跳过动画） | `ApplyEnd(控件, 预设)` |
| 停止 / 查询 / 复位 | `Stop(句柄)` · `StopAllOf(owner)` → 返回停掉几个 · `StopAll()` · `IsPlaying(控件)` · `RestoreBase(控件)` |
| 诊断 | `RevUIAnim.ActiveCount`（正在播几个）· `Debug.Log(RevUIAnim.Dump())` |

> [!WARNING]
> **六条要记住的**
> · **时长不传（或 ≤ 0）= 用该预设的默认时长** —— 想改节奏就显式传，例如 `RevUIAnim.PopIn(panel, 0.35f)`。
> · **一定要传 `owner`**（面板 / Part 传自己）—— 关闭或销毁时框架会 `StopAllOf(owner)` 一行清干净；不传就可能留在池化过的面板上继续算。
> · **同一个控件上只留一个动画**：再起一个会自动顶掉上一个（不会两个动画抢同一个属性）；`Play` 返回的句柄可用 `.IsValid` 查它是否还在播。
> · **动画走 `unscaledDeltaTime`**：暂停（`timeScale = 0`）时 UI 动画照常播；全局倍速改 `RevUIAnim.GlobalSpeed` 一个数。
> · **要关动效**：`RevUISetting.UIAnimationsEnabled = false` —— 所有预设直接写终态，业务代码一行都不用改；只想跳过单个面板用 `RevUIAnim.ApplyEnd(控件, 预设)`。
> · **透明度写 `CanvasGroup`**（没有会自动补一个），缩放 / 位移写 `RectTransform`；要恢复基准态用 `RevUIAnim.RestoreBase(控件)`。

## 六、新手最容易踩的 6 个坑

| 坑 | 正确做法 |
|---|---|
| ① 自己 `Instantiate` 面板预制体 | 一律 `RevUI.Open` —— 否则层级/缓存/事件分发都失效 |
| ② 忘了实现 `OnBindView()` | 它是抽象方法，编译期就会拦你；里面做绑定，不要做业务 |
| ③ 在 `OnBindView` 里开计时器/协程 | 那是一次性的；每次打开要做的事写 `OnOpen` |
| ④ 关闭时不停计时器、不退订事件 | 面板会被**缓存复用**，早期计时器会在下次打开时乱跑 → 全放 `OnClose` 里清 |
| ⑤ 用 `CloseAll()` 当"关一个"用 | 要关单个用 `Close<T>()`；切场景才用 `ShutdownAll()` |
| ⑥ 打不开却没有任何提示 | 检查三处：预制体是否在资源根目录的对应路径下 · 特性里目录/名字是否写对 · 「打包」页签是否已生成映射 |

### 进阶：另外 6 条（多数和"复用 / 生命周期"有关）

| 坑 | 正确做法 |
|---|---|
| ⑦ 事件没用 `owner: this` 注册 | 关闭面板时框架会 `RevEvent.RemoveAllByOwner(this)`，但它只摘"登记在你名下"的；用别的 owner 注册的监听（例如挂在某个长期服务上）框架摘不掉，那类监听要自己按生命周期管 |
| ⑧ 在 `OnRefreshView` 里发请求 / 改数据 | 那就不是"落屏"而是逻辑了，会出现"刷新一次发一次请求"的死循环 → 请求放 `OnOpen` / 交互回调里 |
| ⑨ 面板里直接 `Destroy(gameObject)` | 绕过管理器会让索引、池、资源引用对不上 → 关自己请用 `CloseSelf()` |
| ⑩ 把飘字放在参与返回栈的层 | `Toast` 层不参与 `Back()`；自定义面板若不想被返回键关掉，设 `InBackStack = false` |
| ⑪ 遮罩把不该挡的挡住了 | 想"看一眼但不打断操作"的浮层，显式写 `Mask = RevUIMaskMode.None`（`Popup` / `Guide` / `Top` 层默认是挡的） |
| ⑫ 池里实例占内存 | `KeepAlive` 的界面会一直留着一份实例（连同它端的预制体引用）→ 大界面用 `CacheMode = DestroyOnClose`，或把 `MaxCachedPanels` 调小 |

> [!NOTE]
> **最贵的一课：面板会复用**
> 从池里拿、关掉放回
> OnOpen
> OnClose

---


## 七、相关文档

- [《UI 系统 · 架构解析》](UI系统架构解析.md) —— 设计论证：面板池策略、绑定机制、层级与遮罩的实现、为什么会复用
- [《资源加载系统 · 使用说明》](../资源加载系统/资源加载系统使用说明.md) —— 面板里加载图标的正确姿势（记得 Release）
- [《公共 Mono 模块 · 使用说明》](../公共Mono模块/公共Mono模块使用说明.md) —— 面板里要每帧跑的逻辑怎么写
- [文档总入口](../index.html) · [在线文档站](https://yokino337088.github.io/Revolution/)

---

在线文档站
