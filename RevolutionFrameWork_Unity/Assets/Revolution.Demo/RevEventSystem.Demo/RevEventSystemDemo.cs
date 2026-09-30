// ============================================================
// RevEventSystemDemo.cs —— 事件系统开箱示例
//
// 位置：Assets\Revolution.Demo\RevEventSystem.Demo\
//
// 【它解决什么】
//   "A 发生了一件 B 关心的事，但 A 不该认识 B" —— 用字符串事件名解耦：
//   发送方只管 Dispatch，接收方只管订阅（owner 一行防泄漏）。
//
// 【怎么用】打开配套场景 RevEventSystemDemo.unity → 点 Play → 左侧按钮逐个点。
//
// 【本示例演示什么】
//   ① 无参事件：AddEventListener + DispatchEvent；
//   ② 带参事件（最多 4 个参数）：把"发生了什么"连数据一起带过去；
//   ③ 优先级：同一事件的多个监听者按 priority 从小到大依次执行；
//   ④ 返回值 = 收到事件的监听者数量（发出去没人听也能发现）；
//   ⑤ owner：随对象销毁一行清干净（与计时器 / 输入模块同一套纪律）；
//   ⑥ 隔离：某个监听者抛异常不影响其他监听者，也不传染给派发方。
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Revolution.Demo.EventSystem
{
    public sealed class RevEventSystemDemo : MonoBehaviour
    {
        private readonly List<string> _ui = new List<string>();
        private Vector2 _scroll;

        private int _killedCount;       // 演示数据：怪物死亡计数（事件携带的数据）
        private Action _onBossDead;     // 存下委托才能按引用退订（RemoveEventListener 是按引用匹配）

        private void Ui(string line)
        {
            _ui.Add(line);
            if (_ui.Count > 200) _ui.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void Start()
        {
            // ---------- 订阅（一般在 OnEnable / OnBindView 里做） ----------
            // ① 无参事件：任务系统关心"boss 死了"
            RevEvent.AddEventListener("demo/boss_dead", OnBossDeadLog, owner: this);

            // ② 带参事件：掉落系统关心"谁死了、死在哪"（数据随事件一起带过去）
            RevEvent.AddEventListener<string, int>("demo/boss_dead", (name, gold) =>
                Ui($"[掉落系统] {name} 死亡 → 掉落 {gold} 金币"), owner: this);

            // ③ 优先级：priority 小的先执行（日志系统想最先被通知，就给个更小的数）
            RevEvent.AddEventListener("demo/boss_dead", () => Ui("[统计(优先)  priority=-10] 先收到通知"), -10, owner: this);

            _onBossDead = () => Ui("[普通监听  priority=0] 后收到通知");
            RevEvent.AddEventListener("demo/boss_dead", _onBossDead, 0, owner: this);

            Ui("已订阅 demo/boss_dead（4 个监听者：1 个带参数、1 个负优先级先执行）。点「击杀 Boss」派发。");
        }

        private void OnBossDeadLog() => Ui("[任务系统] boss_dead 事件已收到（任务完成 +1）");

        // ==================== OnGUI ====================

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20));
            GUILayout.Label("<b>RevEvent 事件系统演示</b>", TitleStyle());
            GUILayout.Label($"监听者：{RevEvent.GetListenerCount("demo/boss_dead")}　历史击杀：{_killedCount}", HintStyle());

            // ① 带参派发：返回值 = 收到事件的监听者数量（发出去没人听是能发现的）
            if (GUILayout.Button("① 击杀 Boss（派发带参事件）"))
            {
                _killedCount++;
                int received = RevEvent.DispatchEvent("demo/boss_dead", "暗影领主", 500 + _killedCount * 10);
                Ui($"派发完成：{received} 个监听者收到（返回值让你发现『发出去没人听』这种接错名的情况）。");
            }

            // ② 派发一个没有任何监听者的事件：LogNoListener 打开后会有提示
            if (GUILayout.Button("② 派发没人听的事件"))
            {
                RevEvent.LogNoListener = true;                       // 打开后：派发无人监听的事件会打一条日志
                int received = RevEvent.DispatchEvent("demo/nobody_listens");
                Ui($"0 个监听者收到（LogNoListener=true 时 Console 会提示你事件名可能拼错了）。");
            }

            // ③ 异常隔离：某个监听者抛异常，其他监听者照常、派发方不被传染
            if (GUILayout.Button("③ 异常隔离演示"))
            {
                // 退订按"委托引用"匹配 —— 要退订就把委托存下来（这也是所有事件系统的通用纪律）
                Action boom = () => throw new System.InvalidOperationException("监听者故意的");
                RevEvent.AddEventListener("demo/boom", boom, owner: this);
                RevEvent.AddEventListener("demo/boom", () => Ui("抛异常的监听者后面这个，照常执行 ✓"), owner: this);

                int received = RevEvent.DispatchEvent("demo/boom");
                Ui($"派发完成：{received} 个监听者被调用（抛异常那个已被隔离，不影响后面）。");
                RevEvent.RemoveEventListener("demo/boom", boom);     // 引用相同 → 退订成功
            }

            // ④ owner 清理：退掉本对象注册的全部监听（含上面演示加的）
            if (GUILayout.Button("④ RemoveAllByOwner(this)：一行清干净"))
            {
                int removed = RevEvent.RemoveAllByOwner(this);
                Ui($"按 owner 清掉了 {removed} 个监听 —— 这就是 OnDestroy 里该写的那一行。");
            }

            if (GUILayout.Button("⑤ 重新订阅（清完再玩一轮）"))
            {
                RevEvent.AddEventListener("demo/boss_dead", OnBossDeadLog, owner: this);
                Ui($"重新订阅完成（当前 {RevEvent.GetListenerCount("demo/boss_dead")} 个监听者）。");
            }

            GUILayout.Space(6);
            if (GUILayout.Button("清空演示日志")) _ui.Clear();

            GUILayout.Label("事件名是字符串：建议集中在常量类里定义，避免拼错（拼错 = 发出去没人听，靠返回值与 LogNoListener 发现）。", HintStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            // ---------- 右侧：演示日志 ----------
            GUILayout.BeginArea(new Rect(Screen.width - 470, 10, 460, Screen.height - 20));
            GUILayout.Label("<b>演示步骤日志</b>", TitleStyle());
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _ui) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // 防泄漏示范：本示例以 this 为 owner 的监听全部带走
        private void OnDestroy() => RevEvent.RemoveAllByOwner(this);

        // ---------- 简单样式 ----------
        private static GUIStyle _title, _hint;
        private static GUIStyle TitleStyle() => _title ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        private static GUIStyle HintStyle() => _hint ??= new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, wordWrap = true };
    }
}
