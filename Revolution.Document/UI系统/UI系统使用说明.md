# UI 系统 · 使用说明（手把手教程）

> 使用说明 · 从零到能写业务
>
> 这份文档只回答一个问题：**我该怎么用它？**（一个特性声明面板、一行打开、事件自动绑到方法）
> 读完你能做到：3 分钟写出第一个面板 · 分清 7 个生命周期回调谁先谁后 · 会给面板传数据 · 知道返回栈与层级怎么用

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

*入口统一是 `RevUI`。所有泛型参数都要求 `T : RevUIPanel`。*

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


## 六、新手最容易踩的 6 个坑

| 坑 | 正确做法 |
|---|---|
| ① 自己 `Instantiate` 面板预制体 | 一律 `RevUI.Open` —— 否则层级/缓存/事件分发都失效 |
| ② 忘了实现 `OnBindView()` | 它是抽象方法，编译期就会拦你；里面做绑定，不要做业务 |
| ③ 在 `OnBindView` 里开计时器/协程 | 那是一次性的；每次打开要做的事写 `OnOpen` |
| ④ 关闭时不停计时器、不退订事件 | 面板会被**缓存复用**，早期计时器会在下次打开时乱跑 → 全放 `OnClose` 里清 |
| ⑤ 用 `CloseAll()` 当"关一个"用 | 要关单个用 `Close<T>()`；切场景才用 `ShutdownAll()` |
| ⑥ 打不开却没有任何提示 | 检查三处：预制体是否在资源根目录的对应路径下 · 特性里目录/名字是否写对 · 「打包」页签是否已生成映射 |

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
