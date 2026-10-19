# Revolution 自动化工具 · 新手版使用说明

> **一句话**：这是给 Revolution 工程用的"**自动驾驶仪**" —— 你不用手点 Unity，用一行命令就能让编辑器帮你打包、导表、进 Play、跑 GM 指令、跑性能测试，并自动出一份报告。

---

## 0. 先说人话：它到底在干嘛

想象你去餐厅点菜：

| 餐厅 | 这套工具 |
|---|---|
| 你把菜单写好递给服务员 | 你写一个 **JSON 请求文件**放进 `inbox/` 文件夹 |
| 服务员把单子送进厨房 | Unity 编辑器每 0.1 秒**看一眼** `inbox/`，取走单子 |
| 厨房做菜 | Unity 执行动作（打包 / 导表 / 进 Play / 跑 GM） |
| 菜端回来 | Unity 把结果写成 **`response.json`** |
| 你等菜上桌 | 你的命令行每 0.08 秒看一眼 `response.json` 在不在 |

**没有网络、没有端口、没有 socket** —— 全程就是两个人在同一个文件夹里**递纸条**。好处是极稳（防火墙、杀毒、代理都影响不到），代价是有一点点延迟（通常几百毫秒）。

三种"递纸条"的模式：

| 模式 | 谁在跑 Unity | 适合 |
|---|---|---|
| `live` | **你手动开着** Unity，工具把纸条塞进它的 inbox | 边开发边调、需要看画面 |
| `batch` | 工具**自己起一个** `Unity.exe -batchmode` 进程，跑完自动退出 | CI / 无人值守打包导表 |
| `auto`（默认） | 有活着的编辑器就用它，没有就退化成 batch | 90% 的场景直接用这个 |

---

## 1. 它到底能帮你干什么

| 你原本要手动做的事 | 现在一行命令 |
|---|---|
| 点菜单打包 AssetBundle | `rev.cmd ab build --target Android` |
| 打开导表窗口、选表、点导出 | `rev.cmd excel export --source Assets/Excel` |
| 手动进 Play、手点 GM 按钮 | `rev.cmd gm exec "背包/添加道具 1001 2"` |
| 反复进 Play 测 100 次帧率，自己记数 | `rev.cmd scenario run Automation/examples/business.scenario.json` |
| 生成 UI 面板脚本、绑定控件 | `rev.cmd ui generate --prefab Assets/UI/Bag.prefab` |
| 检查工程配置对不对 | `rev.cmd doctor` |

它**不会**替你做业务：GM 指令、场景脚本里的断言，都要项目自己先注册/写好（见第 6 节）。

---

## 2. 30 秒跑通（复制粘贴就行）

命令从**仓库根目录**执行（`rev.cmd` 默认用 `py -3`；没有 Python Launcher 就设 `$env:REV_PYTHON` 指向 python.exe）：

```powershell
# ① 先自检：工程路径、Unity 位置、桥接是否就绪
.\Automation\rev.cmd doctor

# ② 看看有哪些命令（离线清单，不需要 Unity）
.\Automation\rev.cmd commands

# ③ 编辑器已经开着的话，问一句它现在什么状态
.\Automation\rev.cmd --mode live editor status
```

想让它**自己把 Unity 起起来**（需要告诉它 Unity.exe 在哪）：

```powershell
$env:UNITY_EDITOR = 'E:\你的Unity安装目录\Editor\Unity.exe'
.\Automation\rev.cmd --timeout 300 editor launch
.\Automation\rev.cmd --mode live editor status
```

> **全局选项必须写在子命令前面**：`--project`、`--unity`、`--mode`、`--timeout`、`--confirm`、`--output`、`--no-graphics`。
> 例如 `rev.cmd --mode live --timeout 300 gm exec "..."` ✅ ／ `rev.cmd gm exec "..." --mode live` ❌

---

## 3. 三分钟看懂"为什么有时候会失败"

这四条规则能解释你 90% 的报错：

| 规则 | 为什么 | 报错长什么样 |
|---|---|---|
| **改资源的动作必须退出 Play** | Play 时改资源会被 Unity 打断，轻则改不进去，重则重载脚本 | `编辑器资产操作需要先退出 Play` |
| **GM / 场景测试必须在"未暂停的 Play"里** | 它们操作的是**运行中的游戏**，编辑模式下没有游戏可操作 | `GM 执行需要已进入且未暂停的 Play` |
| **同一时刻只能有 1 个请求** | 编辑器主线程就一条，并发会互相踩 | `Busy`（等上一个跑完再发） |
| **执行期间 Unity 只要报红，这次请求就算失败** | 自动化要的是"干净环境"，有错误还继续跑等于自欺欺人 | `success=false`，`unity-errors.jsonl` 里有原文 |

另外两个时间概念（**新手最容易混**）：

- **`--timeout`**（CLI 参数）= **你愿意等多久**；
- **`deadlineUtc`**（请求里）= Unity 认为这张单子**什么时候过期**。
  单子过期还没轮到执行，Unity 会直接回 `Expired` —— 这是为了防止"你早就关掉终端了，Unity 还在傻跑"。

---

## 4. 常用操作（照抄改参数）

### 4.1 查状态 / 刷新

```powershell
.\Automation\rev.cmd --mode live editor status     # 版本、工程、是否在 Play、是否在编译、GM 数量
.\Automation\rev.cmd --mode live editor refresh    # 让编辑器刷新资源
.\Automation\rev.cmd --mode live editor menus      # 列出可自动打开的菜单（只挑工具窗口）
```

### 4.2 进 Play / 出 Play

```powershell
.\Automation\rev.cmd --mode live editor play --scene 'Assets/Scenes/Battle.unity'
.\Automation\rev.cmd --mode live editor stop
```
> ⚠️ 成功只代表"**已经进入 Play 且观察到 2 帧**"，**不代表你的游戏初始化完了**。要等初始化，用 `scenario` 里的 `until` 步骤。

### 4.3 执行 GM 指令（项目自己的业务命令）

```powershell
.\Automation\rev.cmd --mode live gm list                       # 看有哪些命令、参数、是否高危
.\Automation\rev.cmd --mode live gm exec "背包/添加道具 1001 2"  # 执行
.\Automation\rev.cmd --mode live --confirm gm exec "存档/清空"  # 高危命令要显式 --confirm
```

### 4.4 打包 / 导表 / 生成 UI（都要退出 Play）

```powershell
.\Automation\rev.cmd ab validate --target Android
.\Automation\rev.cmd ab build    --target Android
.\Automation\rev.cmd ab diff                                   # 和上次快照比，看看包结构变了没

.\Automation\rev.cmd excel read   --source Assets/Excel
.\Automation\rev.cmd excel plan   --source Assets/Excel
.\Automation\rev.cmd excel export --source Assets/Excel --force

.\Automation\rev.cmd ui scan     --prefab Assets/UI/Bag.prefab
.\Automation\rev.cmd ui generate --prefab Assets/UI/Bag.prefab --class-name BagPanel
```

### 4.5 跑性能测试并出报告

```powershell
.\Automation\rev.cmd --timeout 300 scenario run Automation/examples/business.scenario.json --repeat 50 --warmup 5
.\Automation\rev.cmd report Automation/results/xxx/scenario-result.json --directory Automation/results/report
```

---

## 5. 一张图记住文件都在哪

```text
<你的工程>/Library/RevolutionAutomation/
├─ inbox/                      ← 外部写给 Unity 的"工单"（每张一个 <32位id>.json）
│     （Unity 取走后会立刻删除，避免重复执行）
└─ jobs/<32位id>/              ← 一次请求一个文件夹，所有过程文件都在里面
      request.json             ← 请求原文（Unity 存一份，便于事后复盘）
      response.json            ← ★ 最终结果（success / error / message / dataJson）
      cancel                   ← 外部想取消：创建这个空文件即可
      progress.json            ← 场景跑到第几轮/第几步（**仅供参考，不能当完成信号**）
      scenario-result.json     ← 场景测试的汇总指标
      iterations.jsonl         ← 每一轮一行（几百轮也不会爆内存）
      unity-errors.jsonl       ← 测试期间 Unity 报的错（最多前 50 条）
      unity.log                ← batch 模式的完整 Unity 日志
```

**判断"跑完了没"永远只看一个东西：`response.json` 是否存在**（以及里面的 `success`）。
`progress.json`、`endpoint.json` 都是"尽力而为"的旁路信息。

---

## 6. 写第一个测试场景（10 分钟）

场景就是一段 JSON，告诉工具：**先做什么 → 重复做什么 → 最后清理什么**。

```jsonc
{
  "name": "战斗冒烟",
  "repeat": 3,                  // 正式跑 3 轮
  "warmup": 1,                  // 先空跑 1 轮预热（不计入成绩）
  "stopOnFailure": true,        // 任一轮失败就停
  "failOnUnityError": true,     // Unity 报红即失败（建议保持 true）
  "setup": [                    // 每轮之前跑一次
    { "kind": "gm", "command": "战斗/进入关卡 1-1" }
  ],
  "steps": [                    // ★ 必填、不能为空：这是每轮真正要测的东西
    { "kind": "gm", "command": "战斗/开始" },
    { "kind": "until", "command": "战斗/查询状态", "expected": "running", "timeoutSeconds": 10 },
    { "kind": "wait", "seconds": 5 },
    { "kind": "assert", "command": "战斗/查询结果", "expected": "win" }
  ],
  "cleanup": [                  // 每轮结束（失败也会尽量执行）
    { "kind": "gm", "command": "战斗/退出" }
  ],
  "teardown": [                 // 全部跑完
    { "kind": "gm", "command": "战斗/回主界面" }
  ],
  "thresholds": {               // 0 表示不检查；超了就判失败
    "maxFrameP95Ms": 16,
    "maxFrameMs": 50,
    "maxMonoUsedBytes": 0
  }
}
```

### 4 种步骤，记住这张表就够了

| kind | 干什么 | 必填字段 | 备注 |
|---|---|---|---|
| `gm` | 执行一条 GM 指令 | `command` | 失败即中断（除非在 `cleanup` 里） |
| `wait` | 等一会儿 | `seconds` 或 `frames` | 两个都给时**都满足**才继续 |
| `until` | 反复执行 GM，直到返回等于期望 | `command`、`expected`、`timeoutSeconds` | **超时必须 > 0**，防死等 |
| `assert` | 一次性断言 | `command`、`expected` | 不等立刻失败 |

> **`expected` 是"精确字符串相等"**，不是"包含"。GM 返回 `"win"` 就写 `"win"`，不能写 `"胜"`。
> 想知道某条 GM 到底返回什么：先 `gm exec` 手动跑一次，看输出。

### 命令里的占位符

| 写法 | 会被替换成 |
|---|---|
| `{iteration}` | 当前正式轮号（从 0 开始） |
| `{round}` | 含预热在内的总轮号 |

---

## 7. 报告怎么看

`scenario-result.json` 里最重要的几个字段：

| 字段 | 含义 | 怎么判断好坏 |
|---|---|---|
| `success` | 整体是否通过 | **只信这个** |
| `completed` / `repeat` | 完成轮数 / 要求轮数 | 必须相等，否则 `success=false` |
| `frameMeanMs` / `frameP95Ms` / `frameMaxMs` | 帧耗时（毫秒） | P95 超过 `maxFrameP95Ms` 就失败 |
| `peakMonoUsedBytes` | Mono 堆峰值 | 看有没有内存泄漏趋势（多轮对比） |
| `unityErrors` | 测试期间 Unity 报错数 | `failOnUnityError=true` 时 >0 就失败 |
| `errors[]` | 人读的失败原因列表 | 第一条通常就是根因 |

还有一条容易忽略的规则：**配了帧门禁但一个样本都没采到 → 判失败**（防止"空跑也绿灯"）。

---

## 8. 常见问题（照着查）

| 现象 | 原因 | 怎么办 |
|---|---|---|
| `Busy` | 上一个请求还在跑 | 等它结束，或 `job <id> --cancel` 取消 |
| `Expired` | 单子过期了（排队太久） | 增大 `--timeout`，或减少并发 |
| `Session` | 编辑器重启过，会话变了 | 重新 `editor status` 确认后重发 |
| `编辑器资产操作需要先退出 Play` | 你在 Play 模式下点了打包/导表 | 先 `editor stop` |
| `GM 执行需要已进入且未暂停的 Play` | 没进 Play，或游戏被暂停 | 先 `editor play`，别暂停 |
| `未知 Unity BuildTarget` | `--target` 拼错 | 用 `StandaloneWindows64` / `Android` / `iOS` / `WebGL` |
| `路径必须在当前工程 Assets 内` | 传了工程外/带 `../` 的路径 | 改成 `Assets/...` 相对路径 |
| `动作不存在或重复` | 扩展动作名写错，或同名注册了两份 | 全局搜 `RevAutomationAction("名字")` |
| 请求成功但结果不对 | 界面/业务还没初始化好 | 用 `until` 等状态，别用 `wait` 猜 |
| 想看 Unity 到底在干嘛 | —— | batch 看 `unity.log`；live 看 `unity-errors.jsonl` |
| `InterruptedByReload` | 执行中改了脚本，Unity 重载了域 | **不会自动重放**（防重复副作用），重发一次即可 |

---

## 9. 三条铁律 + 最容易踩的 8 个坑

**铁律**

1. **不要并发**：一次只发一个请求，串行跑。
2. **改资源的动作先退出 Play**：打包、导表、生成 UI、生成 link.xml 都属于此类。
3. **失败要看原因，不要重试掩盖**：错误码 + `errors[]` 基本能直接定位。

**坑**

1. `ab.mark` 里 **bundle 传空字符串 = 移除标记**，不是"不设包名"。
2. `ab.snapshot` 会**覆盖**上次基准；`ab.diff` 没基准会直接报"请先 ab.snapshot"。
3. `ab.sizes` 是**源码字节估算**，真实压缩后大小看 `ab.artifacts`（产物文件大小）。
4. `excel.mark` 复用 `bundle` 字段传"数据目录"，**语义和 AB 的 bundle 完全不同**。
5. `excel.clean` / 高危 GM **必须显式 `--confirm`**（故意加的门槛）。
6. `editor.menu` **只能打开工具窗口**（白名单 6 个），不能点任意菜单 —— 否则会卡在确认弹窗上。
7. batch 模式**不能**做 `play/stop/gm/scenario/菜单`，它没有交互能力。
8. 编辑器被杀掉时，可能残留 `.json.tmp` 文件（写入采用"临时文件 + 原子替换"），删掉即可。

---

## 10. 命令速查（按前缀）

| 前缀 | 数量 | 用途 | 典型命令 |
|---|---|---|---|
| `doctor` / `commands` | 2 | 自检 / 离线清单 | `doctor` |
| `job` | 2 | 查进度 / 取消 | `job <id> --cancel` |
| `pipeline` | 1 | 顺序执行多个动作，失败即停（最后跑 finally） | `pipeline x.pipeline.json` |
| `report` | 1 | 场景结果 → JSON/CSV/HTML | `report result.json --directory out` |
| `editor.*` | 5 | 状态/刷新/菜单/进 Play/出 Play | `editor status` |
| `gm.*` | 2 | 列指令 / 执行 | `gm exec "..."` |
| `ab.*` | 23 | 标记/改名/扫描/依赖/体积/快照/校验/构建/拷贝 | `ab build --target Android` |
| `excel.*` | 8 | 读表/计划/孤儿/清理/导出 | `excel export --force` |
| `ui.*` | 3 | 扫描预制体 / 预览代码 / 生成面板脚本 | `ui generate --prefab ...` |
| `platform.*` | 2 | 首包资源审计 / 生成 link.xml | `platform.linkxml` |
| `hot.*` | 6 | 热更清单/自检/本地演示 | `hot manifest` |
| `scenario` | 1 | 跑测试场景 | `scenario run x.json` |

完整参数见 `Automation/commands.json`（离线可读），或 `rev.cmd commands`。

---

## 11. 给 CI / Codex 的最小集成

```powershell
# CI 里不依赖桌面 Unity：直接用 batch 起进程跑完就退
$env:UNITY_EDITOR = 'C:\Program Files\Unity\Editor\Unity.exe'
.\Automation\rev.cmd --mode batch --timeout 600 ab build --target Android
if ($LASTEXITCODE -ne 0) { throw "打包失败" }

# 跑性能测试并判门禁（退出码非 0 = 不达标）
.\Automation\rev.cmd --mode batch --timeout 900 scenario run Automation/examples/business.scenario.json
if ($LASTEXITCODE -ne 0) { throw "性能门禁未通过" }
```

**关键**：脚本只看**退出码**（0 成功 / 非 0 失败）和 `response.json` 的 `success`，不用解析日志。

---

## 12. 术语表

| 词 | 人话 |
|---|---|
| 动作（action） | 编辑器能做的一件事，如 `ab.build`、`gm.exec` |
| 场景（scenario） | 一组按顺序执行的测试步骤 + 性能门禁 |
| 轮（round / iteration） | 把场景里的 `steps` 完整跑一遍 |
| 预热（warmup） | 跑但不计分的轮次，用来消除首次运行的抖动 |
| 门禁（threshold） | 性能红线，超了就算失败 |
| 桥（bridge） | Unity 侧那个负责收发文件的组件（`RevAutomationBridge`） |
| 作业（job） | 一次请求 = 一个 job 目录 |
| 会话（session） | 一次编辑器进程的生命周期；重启后旧请求作废 |
| 域重载（domain reload） | 改脚本后 Unity 重新加载程序集；正在跑的动作会被打断且**不重放** |

---

> 看完还是不知道从哪下手？**就跑第 2 节那三条命令**（`doctor` / `commands` / `editor status`）。
> 三条都通了，说明环境没问题，接着抄第 4 节的命令改参数即可。
