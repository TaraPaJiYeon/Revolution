# GM 指令框架（RevGMCommand）· 使用说明

> 这份文档只回答一个问题：**我该怎么用它？**
> 读完你能做到：3 分钟写出第一条 GM 指令 · 会用面板边打边联想地执行 · 知道参数该怎么写 · 知道 8 个最容易白干的坑 · 知道怎么保证它不上线。

配套代码：
- 运行时：`Assets\Revolution\Runtime\RevGMCommand\`（11 个 `.cs` / 1023 行，**纯 C#、不引用 UnityEngine**）
- 编辑器面板：`Assets\Revolution\Editor\RevGMCommand\`（2 个 `.cs` / 820 行，**EditorWindow，不占运行时、不进包体**）
- 开箱示例：`Assets\Revolution.Demo\RevGMCommand.Demo\RevGMCommandDemo.cs`（9 条命令，可直接抄）

---

## 〇、它是干什么的（一句话）

**一行代码注册一条 GM 指令；在编辑器面板里边打边联想、回车执行，成功/失败与耗时直接回显。**

```csharp
// 一行一条：名字里的 / 会自动长成分组树
RevGM.Register("经济/加金币", "给当前玩家加金币", args => AddGold(args.Int(0, 1000)),
               RevGMArg.Int("数量", 1000));
```

面板长这样（菜单 `Revolution.Tools/GM 指令面板`，快捷键 `Ctrl+Shift+G`）：

```
┌───────────────────────────────────────────────────────────────────────────┐
│ ● Play 中（可执行）   命令 42 条                          [命令来源][刷新][帮助] │
│ 命令 [加金_____________________________] [清空] [执行 (Enter)]              │
│ ┌───────────────────────────────────────────────────────────────────────┐ │
│ │ 经济/加金币          <数量>   给当前玩家加金币        ← 命中字高亮、↑↓ 选  │ │
│ │ 经济/清空金币                 把金币清零                              │ │
│ │ 战斗/清空全场敌人  ⚠         把所有敌人血量清零（高危：执行前二次确认）  │ │
│ └───────────────────────────────────────────────────────────────────────┘ │
│ ┌── 分组树 ────────────┐ ┌── 详情 ───────────────────────────────────────┐ │
│ │ ▼ 经济 (2)           │ │ 经济/加金币                                   │ │
│ │   加金币             │ │ 给当前玩家加金币                              │ │
│ │   清空金币           │ │ 参数：· 数量|整数(默认 1000)                  │ │
│ │ ▼ 战斗 (1)           │ │ [填入输入框] [复制命令名] [执行]               │ │
│ └──────────────────────┘ └───────────────────────────────────────────────┘ │
│ ✔ 执行成功（1.20 ms）  经济/加金币 500 → 金币 = 1500                        │
│ 历史（点一条重新填入） 12:31:07 ✔ 经济/加金币 500                          │
└───────────────────────────────────────────────────────────────────────────┘
```

**什么时候用它**：任何"想在跑着的游戏里立刻改点东西/看看状态"的场合 —— 加资源、跳关卡、改数值、开无敌、切频道、模拟协议回包、复现 bug 的前置状态。

---

## 一、3 分钟跑起来（可粘贴）

### 第 1 步：写一个注册入口（放一个静态类里）

```csharp
using Revolution;

public static class MyGameCommands
{
    private static int _gold;

    [RevGMEntry("战斗 / 经济 相关")]        // ← 想让面板在"不进 Play"时也能列出命令，就加它
    public static void Register()
    {
        // ★ 一行注册：名字 / 说明 / 干什么 / 参数说明
        RevGM.Register("经济/加金币", "给当前玩家加金币",
                       args => { _gold += args.Int(0, 1000); return $"金币 = {_gold}"; },
                       RevGMArg.Int("数量", 1000));
    }
}
```

### 第 2 步：让游戏启动时调一次

```csharp
// 你的启动流程里（游戏入口 / 场景 Boot / 或者一个 [RuntimeInitializeOnLoadMethod]）：
MyGameCommands.Register();
```

> 正式包怎么办？**不调 `Register` 就等于不存在**（没有静态构造、没有反射扫描、没有 Update）。详见第九章。

### 第 3 步：打开面板执行

1. 菜单 `Revolution.Tools/GM 指令面板`（或 `Ctrl+Shift+G`）；
2. 搜索框里打 `加金` → 联想列表立刻出现 `经济/加金币`；
3. `Tab` 补全 → 空格 → 输入 `500` → `Enter`；
4. 下方显示：`✔ 执行成功（1.20 ms） 经济/加金币 500 → 金币 = 1500`。

**也可以不用面板**（游戏内控制台、自动化脚本、单测都行）：

```csharp
RevGMResult r = RevGM.Execute("经济/加金币 500");
if (!r.Success) Debug.LogError(r.Message);      // Message 是人话，直接能看
```

---

## 二、面板怎么用（4 步 + 键位表）

| 步骤 | 操作 | 说明 |
|---|---|---|
| ① 找命令 | 在搜索框里边打边看联想 | **前缀 / 连续子串 / 字符级缩写**三种命中都能用，命中字高亮 |
| ② 选 / 补全 | `↑` `↓` 选联想项；`Tab` 用它补全 | 补全后会**自动补一个空格**，接着打参数就行 |
| ③ 执行 | `Enter` 执行；`Ctrl+Enter` 跳过高危确认 | 结果、成功失败、耗时显示在下方 |
| ④ 看详情 | 左侧分组树点一条 → 右侧看说明与参数 | **双击 = 填入输入框**；枚举参数点候选值按钮直接填 |

**键位表**

| 键 | 作用 |
|---|---|
| `↑` `↓` | 在联想列表里上下选择 |
| `Tab` | 用选中的联想项补全（补完自动加空格） |
| `Enter` | 执行输入框里的命令 |
| `Ctrl + Enter` | 强制执行（跳过"高危命令"的二次确认） |
| `Esc` | 清空输入框 |

**两种模式（很重要）**

| 模式 | 命令清单来自 | 能不能执行 |
|---|---|---|
| **编辑模式**（没点 Play） | 调一遍所有 `[RevGMEntry]` 注册入口拿到的**快照** | ❌ 不能（命令体常要碰运行时对象，编辑器里会崩） |
| **Play 模式**（游戏跑着） | 运行期**真实注册表** | ✅ 能 —— 被测环境就是游戏本身，结果真实 |

---

## 三、一行注册的六种形态（挑一种用）

```csharp
// ① 最简单：无参数、无返回值（框架自动替你回 done）
RevGM.Register("经济/清空金币", "把金币清零", args => _gold = 0);

// ② 要回显一句话：返回 string 就是面板上那行结果
RevGM.Register("工具/概览", "打印当前状态", args => $"金币={_gold}");

// ③ 带参数说明（推荐）：面板显示帮助 + 执行前自动校验
RevGM.Register("战斗/缩放主角", "把主控英雄缩放成指定倍数",
               args => { _scale = args.Float(0); return $"缩放 = {_scale}"; },
               RevGMArg.Float("倍率", 1f));

// ④ 高危：面板执行前二次确认（Ctrl+Enter 跳过）
RevGM.Register("战斗/清空全场敌人", "把所有敌人血量清零", args => ClearEnemy(),
               RevGMFlags.HighRisk);

// ⑤ 隐藏：不进联想列表，但直接写完整名仍可执行（给自己留的临时/废弃命令）
RevGM.Register("工具/内部复位", "把演示状态复位", args => Reset(), RevGMFlags.Hidden);

// ⑥ 逻辑复杂就别写大 lambda：把方法直接传进来（方法组）
RevGM.Register("聊天/发消息", "往指定频道发消息", SendChatMessage,
               RevGMArg.Enum("频道", "lobby", "guild", "customteam"), RevGMArg.Str("内容", ""));

private static string SendChatMessage(RevGMArgs args)
    => $"已发送到 {args.Str(0)}：{args.Str(1, "（空）")}";
```

**lambda 支持情况**（已逐条实测）：表达式 lambda / 块 lambda / 捕获外部变量的闭包 / `_ =>` 忽略参数 / 方法组 / 局部函数 —— 都能直接注册。只有两种签名：

| 签名 | 什么时候用 |
|---|---|
| `Action<RevGMArgs>` | 不需要回显（框架自动返回 `done`） |
| `Func<RevGMArgs, string>` | 返回值**就是**面板上那行结果（返回 `null` 也算 `done`） |

---

## 四、参数怎么写（新手最容易含糊的地方）

### 4.1 声明参数说明：`RevGMArg`

| 写法 | 含义 | 帮助里显示成 |
|---|---|---|
| `RevGMArg.Int("数量", 1000)` | 整数，可省略（默认 1000） | `数量\|整数(默认 1000)` |
| `RevGMArg.IntRequired("英雄ID")` | 整数，**必填** | `英雄ID\|整数(必填)` |
| `RevGMArg.Float("倍率", 1f)` | 小数，可省略 | `倍率\|小数(默认 1)` |
| `RevGMArg.Bool("开启", true)` | 开关（`true/false`、`1/0`、`on/off`、`是/否`、`开/关` 都认） | `开启\|开关(默认 true)` |
| `RevGMArg.Str("内容")` | 文本，必填 | `内容\|文本(必填)` |
| `RevGMArg.Str("备注", "")` | 文本，可空 | `备注\|文本(可空)` |
| `RevGMArg.Enum("频道", "lobby", "guild")` | 枚举，候选值会**列在面板上**（点一下就填） | `频道\|枚举(lobby/guild)` |

### 4.2 取参数：`args`（**永远不要自己解析字符串**）

```csharp
args.Count          // 参数个数
args.Has(1)         // 第 2 个参数有没有传
args.Raw(0)         // 原始字符串
args.Text           // 参数整体（原样拼回一行）
args.Str(0, "默认")  // 文本
args.Int(0, 1000)   // 整数（解析失败 → 抛一句人话）
args.Float(0, 1f)   // 小数
args.Bool(0, true)  // 开关
args.Enum<ChatChannel>(0, ChatChannel.Lobby)   // 枚举
```

### 4.3 框架会**执行前**先校验一遍（只要有参数说明）

所以下面这些错误**在业务代码跑之前**就被拦下并回显，不用等到业务里爆异常：

| 你输入 | 面板显示 |
|---|---|
| `经济/加金币 abc` | `经济/加金币 → 参数「数量」应该是整数，实际收到 "abc"` |
| `经济/加金币`（必填时） | `经济/加金币 → 缺少必填参数「数量」（数量\|整数(必填)）` |
| `聊天/发消息 notachannel` | `聊天/发消息 → 参数「频道」应该是 lobby / guild / customteam 之一，实际收到 "notachannel"` |
| `工具/回显 a b c`（只声明 1 个参数） | `参数多了 2 个（本命令最多接受 1 个参数；多个词请用引号括起来，例如 "王者 荣耀"）` |

### 4.4 带空格的参数要用引号

```text
工具/回显 "王者 荣耀" 第二段        → 第 1 个参数是「王者 荣耀」，第 2 个是「第二段」
```

规则：命令名与参数用**空格**分开（中文全角空格、Tab 也算分隔符）；引号内的空格不拆。

> 注意：**命令名里不能有空格**（空格是分隔符），分组请用 `/`。框架在注册时就会报错提醒，不会让你带着这个坑跑。

---

## 五、命令体怎么写得"有话说"（这是最容易踩的坑）

### 5.1 成功：返回一句话（或什么都不返回）

```csharp
RevGM.Register("经济/加金币", "给当前玩家加金币", args =>
{
    AddGold(args.Int(0, 1000));
    return "已发放";                 // ← 面板上显示这句话；返回 null 也没问题（显示 done）
});
```

### 5.2 "业务上说不行"（环境不满足 / 前置条件没到）→ 抛 `RevGMUsageException`

```csharp
RevGM.Register("工具/领取每日奖励", "领取今天的奖励", args =>
{
    if (AlreadyClaimedToday) throw new RevGMUsageException("今天已经领过了");   // ← 这句话原样显示在面板上
    Claim();
    return "领取成功";
});
```

> 这是王者那套最缺的一环：老框架里"界面上连报错都没有"，测试同学只能猜。本框架把"用法错误"和"真 bug"分开处理。

### 5.3 真 bug（自己写错了）→ 什么都别做，让框架兜

```csharp
RevGM.Register("工具/会崩的命令", "演示异常", args => throw new InvalidOperationException("故意崩一下"));
// 面板显示：执行「工具/会崩的命令」时抛异常：InvalidOperationException: 故意崩一下
//            at 你的堆栈首行...
```

**框架统一兜异常**：`RevGMUsageException` → 原样回显人话；其它异常 → 显示类型 + 消息 + 堆栈首行（方便直接定位）。绝不会出现"点了没反应"。

---

## 六、"我要做 X" 对照表

| 我想做的 | 写法 |
|---|---|
| 注册一条无参数命令 | `RevGM.Register("组/名", "说明", args => { ... })` |
| 注册一条要回显的命令 | `RevGM.Register("组/名", "说明", args => "结果文本")` |
| 注册带参数说明的命令 | `..., handler, RevGMArg.Int("数量", 1000)` |
| 加一个**必填**参数 | `RevGMArg.IntRequired("英雄ID")` |
| 加一个**枚举**参数（面板列候选） | `RevGMArg.Enum("频道", "lobby", "guild")` |
| 标记**高危**（执行前二次确认） | `RevGM.Register(..., RevGMFlags.HighRisk)` |
| **不进联想**（临时/废弃命令） | `RevGM.Register(..., RevGMFlags.Hidden)` |
| 动态注销一条命令 | `RevGM.Unregister("组/名")` |
| 清空所有命令（收尾 / 重进 Play） | `RevGM.Clear()` |
| 临时关闭 GM（注册保留） | `RevGM.Enabled = false` |
| 代码里执行 / 自动化脚本 | `RevGM.Execute("组/名 参数")` → 判 `result.Success` |
| 自己做一个输入联想 | `RevGM.Suggest("加金", 8)` |
| 让面板在编辑期也能列命令 | 给注册方法加 `[RevGMEntry("说明")]`（方法**必须是 static、只做注册**） |
| 拿命令的当前数量 / 清单 | `RevGM.Count` / `RevGM.Commands` |

---

## 七、八个最容易白干的坑

| # | 坑 | 现象 / 报错 | 正确做法 |
|---|---|---|---|
| 1 | **命令名里带空格** | 注册时直接抛：`GM 命令名里不能有空格：「工具/打印 GM 概览」` | 名字里用 `/` 分组，不要空格（`工具/打印GM概览`） |
| 2 | **两条命令重名** | 注册时抛：`GM 命令「x」已经注册过了` | 合并，或改分组（框架刻意不做静默覆盖） |
| 3 | **忘了调注册入口** | 面板一条命令都没有 | 启动流程里调一次；或给方法加 `[RevGMEntry]` 让编辑期也能看到 |
| 4 | **编辑模式下点执行** | 失败：`还没进入 Play：编辑模式只能查看与联想` | 按 Play 再来（面板上的执行按钮也会置灰） |
| 5 | **只写末段名但重名** | 失败：`「重置」匹配到 2 条命令（…）—— 请写完整名（含分组）` | 写完整名（或从面板联想里选） |
| 6 | **参数说明与实际用法不一致** | 执行前被拦下（类型/必填/枚举不符） | 让 `RevGMArg` 列表与 `args.Int(0)` 一一对应 |
| 7 | **lambda 捕获了场景对象** | 对象不释放、或用的时候已被销毁 | 只捕获**服务接口 / 静态数据**，运行时再从定位器取真实对象（见第八章） |
| 8 | **联想打拼音缩写（如 `jjb`）** | 没有匹配 | 联想是**字符级**的：中文打中文子串（`加金`）；英文名命令（`Battle/AddGoldInstant`）可用缩写 `agi` |

---

## 八、完整实战：把 GM 指令接上服务定位器

真实项目里，GM 命令不该自己 new 业务系统，而是**从服务定位器里取**（两者是天然搭配）。

**① 启动期：装配服务 + 注册 GM（组合根，只写一处）**

```csharp
public static class GameBoot
{
    // 服务定位器那份文档里说过：真要"全局入口"，就自己在启动代码里放一个 static readonly 字段
    public static readonly RevServiceLocator Services = RevServiceLocator.Create()
        .AddSingleton<IPlayerService, PlayerService>()
        .AddSingleton<IBattleService, BattleService>()
        .Build();

    [RuntimeInitializeOnLoadMethod]          // 或者你的游戏入口
    public static void Boot()
    {
        GameCommands.Register();             // 注册 GM 指令（正式包不调这一行 = 不存在）
    }
}
```

**② GM 指令只做"翻译"：把面板输入翻译成业务调用**

```csharp
public static class GameCommands
{
    [RevGMEntry("局内：经济 / 战斗")]
    public static void Register()
    {
        RevGM.Register("经济/加金币", "给当前玩家加金币（不填默认 1000）",
            args =>
            {
                IPlayerService player = GameBoot.Services.GetRequired<IPlayerService>();   // ← 运行时取服务
                int amount = args.Int(0, 1000);
                player.AddGold(amount);
                return $"金币 = {player.Gold}";
            },
            RevGMArg.Int("数量", 1000));

        RevGM.Register("战斗/秒杀当前目标", "把当前选中目标血量清零",
            args =>
            {
                IBattleService battle = GameBoot.Services.Get<IBattleService>();     // 可选能力：没进战斗就是 null
                if (battle == null) throw new RevGMUsageException("当前不在战斗中");
                if (!battle.HasTarget) throw new RevGMUsageException("没有选中目标");
                battle.KillTarget();
                return "已秒杀";
            },
            RevGMFlags.HighRisk);        // ← 高危：面板会二次确认
    }
}
```

**③ 为什么要这么写**

- 命令体**只做翻译**（把参数变成业务调用），业务逻辑仍在 `IPlayerService` / `IBattleService` 里 —— 这样 GM 指令不会长成第二个业务系统；
- 环境不满足时抛 `RevGMUsageException`，测试同学看到的是"当前不在战斗中"，而不是空引用；
- 高危命令加 `RevGMFlags.HighRisk`，避免手滑。

---

## 九、正式包策略（怎么保证它不上线）

本框架**没有用编译宏裁剪**，而是用更硬的一条：

| 手段 | 说明 |
|---|---|
| **不注册 = 不存在**（推荐） | 正式包里没人调 `RevGM.Register(...)` → 注册表是空的；程序集里只有几十行"空转"代码，**没有静态构造、没有反射扫描、没有每帧开销** |
| **一键关闭** | `RevGM.Enabled = false`（注册保留，所有执行一律被拒绝） |
| **整块删除** | 删掉注册入口 + 命令实现文件即可（框架代码可以留着，也可以一起删，没有耦合） |
| **真正的权限** | 客户端不做权限伪造：`HighRisk` 只是"提醒 + 二次确认"，**真权限必须由服务端裁决**（这一点与王者的结论一致） |

---

## 十、一页速查卡（可打印）

**注册（一行一条）**

- `RevGM.Register("组/名", "说明", args => { ... })`
- `RevGM.Register("组/名", "说明", args => "回显文本")`
- `..., RevGMArg.Int("数量", 1000)` / `RevGMArg.IntRequired("ID")` / `RevGMArg.Enum("频道","a","b")`
- `..., RevGMFlags.HighRisk` / `RevGMFlags.Hidden`
- `RevGM.Unregister("组/名")` · `RevGM.Clear()` · `RevGM.Enabled`

**取参数（别自己解析）**

- `args.Int(0, 默认)` · `args.Float(0, 默认)` · `args.Bool(0, 默认)` · `args.Str(0, 默认)` · `args.Enum<T>(0)` · `args.Count` / `args.Has(i)` / `args.Text`

**命令体**

- 成功：`return "一句话"`（或什么都不返回 → 自动 `done`）
- 业务说不行：`throw new RevGMUsageException("为什么不行")` ← 原样显示在面板
- 真 bug：随便抛，框架会显示类型 + 消息 + 堆栈首行

**执行**

- 面板：`Revolution.Tools/GM 指令面板`（`Ctrl+Shift+G`）；`↑↓` 选、`Tab` 补全、`Enter` 执行、`Ctrl+Enter` 跳过确认、`Esc` 清空
- 代码：`RevGM.Execute("组/名 参数")` → `result.Success` / `result.Message` / `result.ElapsedMs`
- 联想：`RevGM.Suggest("加金", 8)`

**三条铁律**

1. 注册只写在一处（组合根），命令体里不要再注册别的命令；
2. 参数不要自己解析字符串 —— 用 `args.Int(0, 默认值)`，解析失败会给你一句人话；
3. 业务拒绝要"有话说" —— 抛 `RevGMUsageException`，别用静默 `return`。

---

## 十一、与王者 GM 框架的对照（一句话版）

完整 12 条精华 + 16 条糟粕（每条带王者出处）在 `Assets\Revolution\Runtime\RevGMCommand\README.md` 第四节。

| ✓ 保留的精华 | ✗ 改掉的糟粕 |
|---|---|
| 命令名即层级（`组/子组/名` → 自动分组树） | 反射扫全程序集注册（IL2CPP/AOT 风险，真码要靠 `[Preserve]` 打补丁） |
| 参数元数据自动拼帮助（`加金币 <数量\|Int32>`） | 同名命令静默覆盖 |
| 模板方法固定流程 + `done` 约定 | 必须手输完整名、没有补全 |
| 只写末段名也能执行 | 参数靠业务手解字符串（失败抛英文异常） |
| "永不静默失败"（异常转可读返回） | 枚举候选值不校验，只能执行完回一段长文案 |
| 面板要能模糊搜（且不用"去掉 `/`"再搜） | 异常处理推给业务（实测"界面上连报错都没有"） |
| 风险/平台可见性标记 | 没有风险等级，只能把注意事项写进命令名 |
| 一行注册（TS 侧的 `RegisteCommand` 形态） | 靠多层编译宏裁剪（宏矩阵复杂、难体检） |
| — | 面板占运行时（PC/手机/性能三套视图） |
| — | 多套入口并存（类式/静态方法式/网络式/帧式/TS/控制台变量） |
| — | 单文件 7000 行塞 384 条命令；89% 的命令处于注释死代码状态 |

---

## 十二、已知边界（诚实清单）

1. **联想是"字符级"匹配**：中文命令请打中文子串（`加金` → `经济/加金币`）；**拼音首字母缩写（如 `jjb`）不支持**（需要一张拼音表，不在轻量范围内）。英文名命令支持缩写（`agi` → `Battle/AddGoldInstant`）。
2. **编辑模式不执行命令**：避免"命令体碰了运行时对象、在编辑器里直接崩"。
3. **不做客户端权限伪造**：`HighRisk` = 提醒 + 二次确认，真权限留给服务端。
4. **命令名不能有空格**（空格是命令与参数的分隔符），分组用 `/`。
5. **面板不记录跨会话历史**（只在当前编辑器会话内保留最近 50 条）。

---

## 十三、文件都在哪（你只需要读两个）

| 文件 | 行数 | 要不要读 |
|---|---|---|
| `Runtime\RevGMCommand\Core\RevGM.cs` | 入口 | ★ 必读：一行注册（6 个重载）/ `Execute` / `Suggest` |
| `Runtime\RevGMCommand\Core\RevGMArgs.cs` | ~200 | ★ 必读：取参数（`Int` / `Float` / `Bool` / `Str` / `Enum`） |
| `Core\RevGMArg.cs` · `RevGMFlags.cs` · `RevGMResult.cs` · `RevGMUsageException.cs` | 小 | 用到再读（参数说明 / 标记 / 结果 / 用法错误） |
| `Engine\RevGMRegistry.cs` · `RevGMCommand.cs` · `RevGMParser.cs` · `RevGMMatcher.cs` | 引擎 | 不用读（注册表 / 记录 / 分词 / 匹配打分） |
| `Extras\RevGMEntryAttribute.cs` | 小 | 想让面板编辑期列命令时读 |
| `Editor\RevGMCommand\RevGMWindow.cs` · `RevGMEditorCatalog.cs` | 820 | 想改面板时读 |
| `Assets\Revolution.Demo\RevGMCommand.Demo\RevGMCommandDemo.cs` | 72 | ★ 建议先读这个（9 条示例，直接抄） |
