# 动作序列（RevActionSequence）—— 3 分钟上手

> **一句话**：用代码写"先 A 后 B、B 要等条件、中途能取消"的演出流程。
> 整套文件里，**你要读的只有 3 个**（约 300 行）。

## 一、三行跑起来

```csharp
// ① 构建清单（加载期一次，缓存到 static readonly）
static readonly RevSequenceDefinition ChestOpen = RevSequence
    .Create("宝箱开箱")
    .Do("播出现音效", ctx => ctx.Get<ISoundService>()?.Play("Play_Box_Appear"))
    .WaitUntil("等玩家点击", ctx => ctx.Get<IInputService>()?.Clicked == true, 15f)
    .Do("开门", ctx => ctx.Get<IDoorService>()?.Open())
    .Build();

// ② 播放（Unity 里一行，零配置：框架自动每帧驱动）
RevSequencePlayer.Default.Play(ChestOpen, source: gameObject);
```

## 二、只需要读这 3 个文件

| 顺序 | 文件 | 内容 |
|---|---|---|
| ① | `Core\RevSequence.cs` | 构建入口 + 三条铁律 + 常见错（最短） |
| ② | `Core\RevSequenceBuilder.cs` | 常用 6 个方法：`Do` / `Wait` / `WaitFrames` / `WaitUntil` / `OnCancel` / `Build` |
| ③ | `Core\RevSequenceBuilder.Advanced.cs` | 用到才查：并行 / 重复 / 嵌套 / 事件 / 等异步 / 自定义步骤 |

**其余都不用读：**

| 目录 | 是什么 | 要不要读 |
|---|---|---|
| `Engine\` | 引擎内部（Runner / Run / Handle / 池化 / 并发策略 / 事件总线） | 不用读；调优排查时再进 |
| `Steps\` | 内置步骤库（等待 / 委托 / 事件 / 并行 / 重复 / 嵌套的实现） | 不用读；看名字就懂 |
| `Extras\` | 可选能力（区域触发源 / 事件触发源） | 用到再读；整块删掉也不影响主流程 |
| `Unity\` | 宿主适配（每帧 Tick 的驱动组件） | 不用读；但它就是你 Play 的那一行 |

## 三、三条铁律（踩了必出 bug）

1. **清单只 Build 一次并缓存** —— `Build()` 之后再改会抛异常，运行期反复构建是浪费；
2. **状态别存在构建期字段里** —— 用业务服务，或用自定义步骤的"状态槽"（否则两个玩家同时触发会互踩）；
3. **占用了什么（生成的物件、推近的镜头），就在 `.OnCancel` 里还回去** —— 取消是常态（切场景、掉线），不是异常。

## 四、卡住了怎么办

框架**自身不打任何日志**（等框架日志系统统一接入后再接）、**也不提供任何查询**（没有「现在几条在跑」「卡在哪一步」这类 API）。观察只有两个口子：

- **查"卡在哪一步"**：在 `.OnCompleted(run => …)` / `.OnCancelled(run => …)` / `runner.RunFinished` 里自己记（`run` 的 ToString 自带序列名与进度、`run.Status`、`run.Elapsed`、`run.StepCount`）；
- **查"现在有几条在跑"**：框架不提供 —— 要看「还有多少在跑」，自己在回调里计数（Demo 的 `_finishedCount` 就是示范）；
- **三个最常见的错**：
  - **Play 了没反应** → 忘了 Tick（用 `RevSequencePlayer.Default.Play(...)` 就不会），或忘了 `.Build()` / 传了 null（会直接抛异常告诉你）；
  - **速度翻倍 / 等待时间减半** → 场景里有两套驱动（框架会自动关掉第二个，并在 Console 打一条告警）；
  - **第一次跑对、第二次数据不对** → 状态存进了构建期字段（改成业务服务或状态槽）。
- **想打日志**：在步骤里自己打 —— `.Do("日志", ctx => Debug.Log($"[序列] 现在 xxx"))`。

## 五、完整教程

`Revolution.Document\动作序列\`
- **使用说明** = 从"能跑"到"写对"（含五个王者真实场景 Demo 讲解）
- **使用指南** = 设计动机、通用性论证、与原体系（`2026年9月24日\动作序列执行系统`）的逐条对照

代码 Demo：`Assets\Revolution.Demo\RevActionSequence.Demo\`（宝箱 / 灵果 / 农场礼盒 / NPC 特写 / 火箭演出）
