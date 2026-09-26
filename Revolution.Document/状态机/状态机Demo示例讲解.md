# 状态机 Demo 实战讲解（对标王者真实业务）

> 代码位置：`RevolutionFramework\Revolution.Demo\Unity\RevStateMachine\`
> 配套文档：`状态机使用指南.md`（框架 API 全貌）/ `状态机使用指南.html`（含动画）
> **HTML 版**：`状态机Demo示例讲解.html` —— 含三段流转动画（主流程 / UI 栈 / Boss 双状态机）

---

## 〇、三个 Demo 一览

| # | Demo | 用哪套机器 | 对应的王者业务 | 覆盖的关键能力 |
|---|---|---|---|---|
| 1 | **游戏主流程**<br>`LightweightFlow\` | 轻量级**单状态机** | 王者① `StateMachine` 的 8+ 使用方之一：**游戏主流程**（登录 → 大厅 → 匹配 → 战斗） | 注册（实例 / 工厂）、`ChangeTo` vs `ChangeToAsync`、**`PendingState` 兜底释放（= 王者①的 tarState）**、异步回来先验状态 |
| 2 | **UI 界面栈**<br>`LightweightUiStack\` | 轻量级**栈状态机** | 王者③ `StackStateMachine<T>` 的官方用例：**UI 界面栈**（主界面 → 背包 → 设置/帮助） | `Push`/`Pop`/`Change`/`Clear`、**`OnSuspend`/`OnResume` 配对**、工厂注册（可叠多层）、**只有栈顶被驱动**、面包屑自维护 |
| 3 | **Boss AI**<br>`HeavyweightBossAI\` | **重量级**（枚举 + 行为接口） | 王者④ `FSM<T>` 的正主场景：**怪物 / Boss AI**（待机 → 巡逻 → 追击 → 战斗 → 二阶段 → 死亡） | 枚举当键、行为接口、三路驱动、**事务式切换（二阶段等资源与演出）**、全局规则集中、**组合两台状态机**、切换历史 |

**一句话选择标准**：业务是"按类型切、状态固定"→ 轻量；业务是"要按枚举做条件判断 / 查表 / 存档"→ 重量。

三个 Demo 都**不需要任何场景布置**（不需要 UI、Prefab、模型），挂一个空物体就能跑，左上角有实时面板，Console 有完整流转日志。

---

## 一、怎么跑

```text
1. 把 Revolution.Demo\Unity\RevStateMachine\ 整个目录拷进 Unity 工程的 Assets 下（位置随意）
2. 场景里新建空物体，按需挂上三个组件之一：
   · RevDemoGameFlow      —— Demo 1（轻量·主流程）
   · RevDemoUiStackDemo   —— Demo 2（轻量·UI 栈）
   · RevDemoBossAI        —— Demo 3（重量级·Boss AI）
3. Play，按左上角面板上的提示按键
```

目录结构（按场景分文件夹，每个场景 2~3 个文件；状态类按场景聚合在一个文件里并分区注释，避免 20 个小文件）：

```text
RevStateMachine\
├── LightweightFlow\            Demo 1：轻量级·单状态机（游戏主流程）
│   ├── RevDemoBattleAssetService.cs   模拟"战斗资源"异步加载/释放（真实项目=资源系统）
│   ├── RevDemoGameFlowStates.cs       5 个流程状态（登录/大厅/匹配/战斗加载/战斗）
│   └── RevDemoGameFlow.cs             ★ 驱动：注册状态、驱动 Update、假 UI 按键
├── LightweightUiStack\         Demo 2：轻量级·栈状态机（UI 界面栈）
│   ├── RevDemoUiStates.cs             4 个界面状态（主界面/背包/设置/帮助）
│   └── RevDemoUiStackDemo.cs          ★ 驱动：Push/Pop、面包屑快照、栈面板
└── HeavyweightBossAI\          Demo 3：重量级（枚举 + 行为接口）
    ├── RevDemoBossDefine.cs           枚举 ×2 + 宿主行为接口
    ├── RevDemoBossStates.cs           主状态机 6 个状态 + 技能状态机 3 个状态
    └── RevDemoBossAI.cs               ★ 宿主：注册状态、三路驱动、全局规则、两台机器
```

> 命名全部带 `RevDemo` 前缀，与框架的 `Rev` 前缀保持一致的观感；namespace 统一 `Revolution.Demo`
> （它是 `Revolution` 的子命名空间，所以**不需要** `using Revolution;` 就能直接用到框架类型）。

---

## 二、Demo 1：轻量级·单状态机 —— 游戏主流程

### 2.1 王者业务背景

王者①的 `StateMachine`（非泛型栈式）有 8+ 业务在用，"游戏主流程"就是其中一条：登录 → 大厅 → 匹配 → 战斗加载 → 战斗。

这条链路之所以适合当 Demo，是因为它**同时包含两种切换**：

| 场景 | 用什么 | 为什么 |
|---|---|---|
| 点按钮进匹配、取消回大厅 | `ChangeTo(...)` 同步 | 必须马上切，玩家点了就要有反应 |
| 读条进战斗 | `ChangeToAsync(...)` 事务式 | 资源没就绪就切过去 = 白屏/穿帮 |
| 离开读条态时判断"本来要去哪" | `PendingState` | **王者 `LoadingState` 的真实用法**：目标不是战斗 → 兜底释放战斗资源 |

### 2.2 状态流转

```text
        ┌──────────┐  登录成功(异步)   ┌────────┐  按[1]   ┌──────────┐
        │ Login    │ ───────────────▶ │ Lobby  │ ───────▶ │ Matching │
        └──────────┘                  └────────┘          └──────────┘
                                         ▲ 按[2]取消          │ 匹配成功(异步)
                                         │                    ▼
                                         │            ┌──────────────────┐
                                         │            │ BattleLoading    │← 读条中按[2]取消
                                         │            └──────────────────┘
                                         │                    │ 资源就绪 → ChangeToAsync
                                         │                    ▼（等 Battle 的 PrepareEnterAsync）
                                         │            ┌──────────────────┐
                                         └── 战斗结束 ─│ Battle           │
                                                       └──────────────────┘
```

### 2.3 关键代码逐段讲解

**① 注册：固定状态用实例、带参数的状态用工厂**

```csharp
// 固定状态 → 注册实例（复用同一个对象：切到"已经是当前状态"时会被自动跳过）
_sm.Register(new RevDemoLoginState(_sm));
_sm.Register(new RevDemoLobbyState(_sm));
_sm.Register(new RevDemoMatchingState(_sm));
_sm.Register(new RevDemoBattleLoadingState(_sm));

// 带参数的状态 → 注册工厂（每局给新实例：战斗模式可能不同）
_sm.Register<RevDemoBattleState>(() => new RevDemoBattleState(_sm, _nextBattleMode));

// 之后按类型切
_sm.ChangeTo<RevDemoMatchingState>();
```

> **为什么工厂**：`RevDemoBattleState` 需要"本局模式"这个构造参数；而且每次进战斗都该是干净的新实例。
> 这就是"注册实例"与"注册工厂"的分工 —— 前者省对象，后者要留参数/要新对象。

**② 同步切换 vs 事务式切换**

```csharp
// 同步：立刻交割（输入响应类）
_sm.ChangeTo<RevDemoMatchingState>();

// 事务式：等目标状态自己说"我准备好了"（RevDemoBattleState.PrepareEnterAsync 播完进场演出）
await _sm.ChangeToAsync<RevDemoBattleState>();

// ★ await 回来必须确认（被更新的请求取代时，状态不变且不抛异常）
if (!_sm.Is<RevDemoBattleState>()) Debug.Log("本次切换被取代/取消");
```

**③ `PendingState` 兜底释放 —— 本 Demo 最值钱的一段**

```csharp
public override void OnExit()
{
    // PendingState = "当前事务要去哪"（= 王者①的 tarState，持久保留语义）
    if (_sm.PendingState is RevDemoBattleState)
        Debug.Log("离开加载态且目标就是战斗 → 资源保留给战斗使用");
    else
    {
        Debug.Log("离开加载态，但目标不是战斗 → ★ 兜底释放战斗资源");
        RevDemoBattleAssetService.Release();
    }
}
```

它为什么能工作（细节很关键）：

| 离开方式 | 交割顺序 | `OnExit` 时 `PendingState` | 结论 |
|---|---|---|---|
| 异步交割进战斗 | 先 `OnExit`，再清 `_pending` | **= 战斗态实例** | 资源保留 ✓ |
| 玩家读条中点取消（同步切回大厅） | `ChangeTo` 先**作废事务**（清 `_pending`），再交割 | **null** | 兜底释放 ✓ |

**④ 异步流程回来先"验活"**

```csharp
private async RevTask MatchAsync()
{
    await RevTask.Delay(2000);
    // ★ 期间玩家可能已经取消（切回大厅）→ 不能继续推进流程
    if (!ReferenceEquals(_sm.CurrentState, this)) { Debug.Log("结果回来时已不在匹配态 → 丢弃"); return; }
    _sm.ChangeTo<RevDemoBattleLoadingState>();
}
```

> 这是"状态机 + 异步"最经典的一类 bug：**需求以为"匹配成功就进战斗"，实际玩家可能已经退出了**。
> 同理，`RevDemoLoginState` / `RevDemoBattleLoadingState` 里都做了这个校验。

### 2.4 跑起来会看到什么（Console 日志节选）

```text
[Boss]…（Demo 3 才有）
[主流程] 进入【登录】：开始异步登录（模拟 1 秒）
[主流程] 登录成功 → 切到大厅
[主流程] 状态变化：RevDemoLoginState → RevDemoLobbyState
[主流程] 进入【大厅】：可以开始匹配了
[按键] 开始匹配
[主流程] 进入【匹配中】：开始匹配（约 2 秒，期间可按 [2] 取消）
[主流程] 匹配成功 → 切到【战斗加载】
[主流程] 进入【战斗加载】：读条开始（按 [2] 可在读条中取消，观察资源兜底释放）
[资源] 战斗资源加载中… 33% / 66% / 100%
[主流程] 资源就绪 → 发起事务式切换（等战斗态准备好）
[战斗] 进入准备：播放进场演出（5v5）…
[主流程] 离开【战斗加载】，目标就是战斗 → 资源保留给战斗使用
[战斗] 进入【战斗】（5v5）：开打！3 秒后自动结束
```

**故意在读条中按 [2]**，就会看到兜底那条：

```text
[主流程] 离开【战斗加载】，但目标不是战斗 → ★ 兜底释放战斗资源
[资源] ★ 战斗资源已释放
```

### 2.5 这个 Demo 能答的面试题

- **"活动/流程切换要不要等资源？"** → 分两类：必须马上切的用同步，等资源/等演出的事务式；两边混用才是常态。
- **"加载到一半玩家退出了，会不会漏资源？"** → 不会：用 `PendingState`（tarState）判"本来要去哪"，目标不对就兜底释放 —— 这是王者 `LoadingState` 的真实做法。
- **"异步回调回来时状态已经变了怎么办？"** → 先验活（`CurrentState == this`），不满足就丢弃结果。

---

## 三、Demo 2：轻量级·栈状态机 —— UI 界面栈

### 3.1 王者业务背景

王者③ `StackStateMachine<T>` 的官方示例场景就是 **UI 界面栈**。栈语义的灵魂在 `OnSuspend` / `OnResume`：

```text
主界面 ─Push─▶ 背包 ─Push─▶ 设置 ─Push─▶ 帮助·第1层 ─Push─▶ 帮助·第2层
   ▲ OnSuspend（禁用点击，但不销毁）
   │                                       Pop◀─ 逐级 OnResume（数据都还在，不用重拉）
```

如果换成"普通状态机（只有 Enter/Exit）"，进背包就得销毁主界面、返回时重建 —— 重建意味着重新拉数据、丢滚动位置、重播入场动画。

### 3.2 关键代码逐段讲解

**① 实例注册 vs 工厂注册（这里体现得最明显）**

```csharp
// 同一时刻只可能有一个 → 实例注册
_sm.Register(new RevDemoUiLobbyState());
_sm.Register(new RevDemoUiBagState());
_sm.Register(new RevDemoUiSettingsState());

// 帮助弹窗可以叠多层 → 工厂注册（每次 Push 都拿新实例）
_sm.Register<RevDemoUiTipsState>(() => new RevDemoUiTipsState(++_tipsLayer));
```

> **为什么这里必须用工厂**：栈状态机有个保护 —— **同一个实例不能连着压两次**（栈里出现同一个对象两份，Pop 时会对它先 OnExit 再 OnResume）。
> 按 [6] 可以现场看到这条保护：框架抛出 `InvalidOperationException`，Demo 把它 catch 下来打成了警告日志。

**② 只有栈顶被驱动（可量化验证）**

```csharp
// 每个状态自己数帧：OnUpdate 里 UpdatedFrames++
public override void OnUpdate(float deltaTime) => UpdatedFrames++;
```

Demo 面板会把每个状态的帧数摊出来：**栈顶那个数字一直在涨，被压住的全部停住** —— 这就是"只有栈顶被 Update 驱动"的直观证据。

**③ 面包屑：框架不给，就自己维护（推荐做法）**

```csharp
// 栈用 Stack<T> 承载（只有 LIFO，读不了"倒数第 N 层"），所以业务订阅事件同步一份路径快照
_sm.StatePushed += state => _breadcrumb.Add(state);
_sm.StatePopped += state => _breadcrumb.Remove(state);
```

> 这也回答了上一个设计问题：**想要"整条返回路径"，不该把状态机的存储换成数组，而是让业务维护一份快照**（成本极低，且永不与真栈不同步）。
> 只有"上一层是谁"这种单层需求，才用框架自带的 `Under`。

**④ 四种结构操作的区别**

| 操作 | 语义 | 回调序列 |
|---|---|---|
| `Push(x)` | 压栈 | 旧栈顶 `OnSuspend` → x `OnEnter` |
| `Pop()` | 弹栈 | 栈顶 `OnExit` → 新栈顶 `OnResume` |
| `Change(x)` | **替换**栈顶（栈深不变） | 旧栈顶 `OnExit`（此时可读 `TargetState`，= tarState）→ x `OnEnter` |
| `Clear()` | 清场 | 逐层 `OnExit`（**不**触发 `OnResume`） |

### 3.3 真实用途对照

| Demo 里的状态 | 王者里的对应 |
|---|---|
| 主界面被压住时 `OnSuspend` | "大厅输入"被"战斗输入"压住：**禁用点击但不销毁**，打完一局回来直接 `OnResume` |
| 背包 `OnSuspend` 暂停刷新 | 被盖住的界面停止列表刷新（省性能，也避免看不见的界面还在响应） |
| `Change` 替换栈顶 | 王者① `ChangeState`：**栈深不变**的替换（如"读条页"直接换成"错误页"） |

### 3.4 这个 Demo 能答的面试题

- **"为什么 UI 栈不能只用 Enter/Exit？"** → 因为返回时不该重建：`OnSuspend`/`OnResume` 保住了数据、滚动位置、入场动画状态。
- **"栈里的状态谁在被 Update？"** → 只有栈顶；被压住的暂停（Demo 里的帧数计数就是证据）。
- **"怎么画面包屑？"** → 订阅 `StatePushed`/`StatePopped` 自己维护一份路径快照，而不是要求框架提供按下标访问。

---

## 四、Demo 3：重量级 —— Boss AI（枚举 + 行为接口）

### 4.1 王者业务背景

怪物 AI / Boss AI 是重量级状态机的正主（对应王者④ `FSM<T>`：**枚举当键 + 事务式切换**）。为什么这类业务要这两样东西：

| 需求 | 为什么枚举 / 行为接口能满足 |
|---|---|
| 按状态做条件判断、查表、上报、存档 | 枚举是值类型，能比较、能当字典键、能直接写进存档 |
| 状态逻辑要能单元测试 | 状态只依赖 `IRevDemoBoss` 行为接口，测试时给个假实现即可（不需要真模型/真场景） |
| 阶段推进要等资源/等演出 | `PrepareEnterAsync` 让"准备"与"进入"分开（事务式交割） |

### 4.2 组合两台状态机（而不是分层状态机）

```text
RevDemoBossAI（宿主）
├── 行为机 RevHeavyFsm<RevDemoBossStateType, IRevDemoBoss>   Idle/Patrol/Chase/Combat/Phase2/Dead
└── 技能机 RevHeavyFsm<RevDemoBossSkillType, IRevDemoBoss>   None/Casting/Cooldown
```

两者的接线只有一行 —— 状态通过宿主发起请求，宿主决定交给哪台机器：

```csharp
// 战斗状态里：请求放技能（状态只管"要放"，不关心技能机怎么实现）
Owner.RequestCastSkill();

// 宿主里：转发给技能机
public void RequestCastSkill()
{
    if (_skillFsm.IsTransitioning || _skillFsm.CurrentStateType != RevDemoBossSkillType.None) return;
    _skillFsm.ChangeState(RevDemoBossSkillType.Casting, "战斗状态下请求释放技能");
}
```

> **对比分层状态机**：如果用"父状态里套子状态机"，技能机就得挂在战斗状态的子节点上，
> 于是"战斗结束了技能还在吟唱""谁负责 Dispose 子机"这类问题全来了。
> 各开一台、各自驱动，"阶段"与"技能"互不干扰 —— 这正是"组合优于继承"。

### 4.3 关键代码逐段讲解

**① 三路驱动：位移放 FixedUpdate**

```csharp
private void Update()      { CheckGlobalRules(); _fsm.UpdateState(Time.deltaTime); _skillFsm.UpdateState(Time.deltaTime); }
private void FixedUpdate() { _fsm.FixedUpdateState(Time.fixedDeltaTime); _skillFsm.FixedUpdateState(Time.fixedDeltaTime); }
private void LateUpdate()  { _fsm.LateUpdateState(Time.deltaTime); }   // 表现纠偏
```

**② 全局规则集中判断（AI 里最容易踩的坑）**

```csharp
private void CheckGlobalRules()
{
    if (_hp > 0f) return;
    if (_fsm.CurrentStateType == RevDemoBossStateType.Dead) return;
    _fsm.ChangeState(RevDemoBossStateType.Dead, "血量归零");
}
```

> 死亡是"任何状态下都成立"的规则。如果把它分别写进 6 个状态的 `OnUpdate`，**必然漏掉一个** ——
> 线上表现就是"打死了还在攻击"。集中判断只需 3 行，且不可能漏。

**③ 事务式切换：二阶段等资源与演出**

```csharp
// 战斗状态里：血量过半 → 事务式切二阶段（期间战斗照常进行，玩家不会看到僵住）
ChangeToAsync(RevDemoBossStateType.Phase2, "血量低于 50%").Forget();

// 二阶段状态里：资源与演出没完，就不允许交割
public override async RevTask PrepareEnterAsync(RevCancellationToken token)
{
    await RevTask.Delay(900);   // 真实项目：await ResManager.LoadAsync<GameObject>("Boss/Phase2Fx", token)
}
```

老写法（在 `OnEnter` 里边演边加载）会出现"先变身、特效后到"的穿帮；事务式把"准备"与"进入"分开，交割那一刻资源一定就绪。

**④ 切换原因 + 历史（王者 2024 才补的能力）**

```csharp
_fsm.ChangeState(RevDemoBossStateType.Chase, "发现玩家");        // 带原因

// 按 [L] 打印：
//     12.34s  Idle → Combat（玩家已在攻击范围内）
//     15.02s  Combat → Phase2（血量低于 50%）
foreach (var t in _fsm.GetHistory()) Debug.Log($"    {t.Time:F2}s  {t}");
```

> 排 AI 问题最常问的两句话："它怎么走到这一步的""为什么刚切走"。
> 前者靠 `GetHistory()`（带时刻），后者靠 `reason`（带原因）。

**⑤ 日志出口可接管**

```csharp
RevHeavyFsmLog.Sink = message => Debug.Log(message);   // 接到 Unity 控制台；真实项目换成你们统一日志
```

框架的诊断日志是 `[Conditional("UNITY_EDITOR")]` 的（正式包零开销），需要线上排查时把它接到自研日志系统即可。

### 4.4 这个 Demo 能答的面试题

- **"AI 为什么用状态机而不是一堆 if-else？"** → 条件只在自己状态下成立、进出有明确回调、能查历史原因；`if-else` 堆叠到 6 个状态以上就无法维护。
- **"Boss 二阶段既要加载又要演出，怎么保证不出穿帮？"** → 事务式切换：`ChangeToAsync` + `PrepareEnterAsync`，资源/演出就绪才交割。
- **"多个状态机怎么协作？"** → 组合多台（行为机 + 技能机），由宿主做接线；不要"父状态套子状态机"。
- **"死了还在攻击这种 bug 怎么根除？"** → 全局规则集中判断（`CheckGlobalRules`），不散落到每个状态。

---

## 五、三个 Demo 的技术点清单（可直接用于简历 / 面试）

| 技术点 | 出现在 | 一句话说法 |
|---|---|---|
| 插件化注册（类型当键 + 实例/工厂两种注册） | Demo 1、2 | 状态实例集中注册，按类型切换；需要参数或需要新实例的走工厂 |
| 同步切换 / 事务式异步切换双入口 | Demo 1、3 | "必须马上切"和"等准备完再切"分开，混用是常态 |
| tarState 兜底（`PendingState`） | Demo 1 | 离开加载态时判断"本来要去哪"，目标不对就释放资源 |
| 异步回调"验活" | Demo 1 | `CurrentState == this` 不成立就丢弃结果，避免把玩家强行拉进流程 |
| 栈语义（Suspend/Resume 配对 + 只有栈顶被驱动） | Demo 2 | 被压住不销毁、只暂停；返回不重建 |
| 重复压栈保护 + 工厂注册 | Demo 2 | 同一实例不能连压两次（栈里出现同一对象两份）；要叠多层必须用工厂 |
| 面包屑自维护 | Demo 2 | 路径快照由业务订阅事件维护，而不是要求框架支持随机访问 |
| 枚举当键 + 行为接口 | Demo 3 | 能条件判断/查表/存档；状态可脱离 MonoBehaviour 做单测 |
| 三路驱动（Update/FixedUpdate/LateUpdate） | Demo 3 | 位移放 FixedUpdate、表现纠偏放 LateUpdate |
| 事务式切换 + 异步准备 | Demo 3 | 二阶段等资源与演出，杜绝"先变身再加载"的穿帮 |
| 组合多台状态机 | Demo 3 | 行为机 + 技能机由宿主接线，替代分层状态机 |
| 全局规则集中 + 切换原因 + 历史 | Demo 3 | 死亡只判一次；`GetHistory()` + `reason` 让 AI 可回溯 |

---

## 六、验证结果与已知取舍

| 检查 | 结果 |
|---|---|
| 编译（8 个 Demo 文件 + 框架一起编，Debug 配置） | **0 错误 0 警告** |
| 运行依赖 | 无需场景布置；只需把目录拷进工程、挂一个组件 |
| 未覆盖的 API | 栈机的 `TargetState` 只在 [7] 替换栈顶时顺带演示；重量级的 `PrepareExitAsync`、`CanChangeTo`、`MaxHistorySize` 未在 Demo 里展开（在《使用指南》里有说明） |

**已知取舍（写出来，避免误读）**：

1. Demo 用日志代替表现层（不播真动画、不生成 UI）—— 目的是让"状态机的行为"直接可见；
2. `RevHeavyFsmLog.Sink` 是**静态**的，Demo 在 `Awake` 里设置它会影响全局；真实项目里初始化一次即可；
3. Demo 里的"假 UI 按键"是演示便利，真实项目里这些入口是按钮/事件/战斗系统回调 —— 调用的 API 完全一样。

---

> **一句话总结**：
> 轻量级的两套机器解决"**流程要切得快、界面要回得来**"；
> 重量级解决"**AI 要能判断、能等待、能回溯**"。
> 三个 Demo 合起来，正好把"王者四套状态机"的核心场景都覆盖了一遍 —— 而它们背后的框架，只有 10 个文件。
