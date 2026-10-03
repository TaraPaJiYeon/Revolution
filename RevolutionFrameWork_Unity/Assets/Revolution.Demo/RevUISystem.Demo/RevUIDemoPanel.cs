// ============================================================
// RevUIDemoPanel.cs —— UI 系统演示面板（一个"业务面板"的标准写法）
//
// 位置：Assets\Revolution.Demo\RevUISystem.Demo\
//
// 【这就是业务面板的全部样子】
//   · 类上 [RevUIPanel(资源目录, 层级)]：声明预制体在哪、挂哪一层（其余配置全有默认值）；
//   · [RevBind] 字段：按节点名自动拿控件（OnBindView 时已赋值）；
//   · OnBindView：只做"表现装配"（纪律：不写业务规则）；
//   · OnClick(节点名)：按钮点击统一分发（不想写绑定字段时用它）；
//   · OnOpen / OnClose：每次打开 / 关闭的业务钩子。
//
// 【预制体从哪来】
//   Editor\RevUIDemoPrefabBuilder.cs 里有一个"一键生成演示预制体"的菜单
//   （用代码搭出 Panel → 按钮 / 文本 的结构并存成 prefab）。生成一次即可，之后随用随开。
//   ★ 生成到【资源根目录】下：Assets/GameRes/RevUIDemo/RevUIDemoPanel.prefab
//     因为编辑器直读/AB 都按「资源根目录 + 逻辑路径」找资源 —— 这里声明的 "RevUIDemo"
//     就是相对 Assets/GameRes/ 的逻辑路径（不是完整的 Assets/... 路径）。
// ============================================================
using UnityEngine;
using UnityEngine.UI;

namespace Revolution.Demo.UI
{
    // 层级用 Popup：框架会自动推断"这个层带遮罩挡点击"（Mask = RevUIMaskMode.Auto 的默认推断）
    // 第一个参数是**相对「资源根目录」（Assets/GameRes/）的逻辑目录** —— 少了这段前缀就找不到预制体。
    [RevUIPanel("RevUIDemo", RevUILayer.Popup)]
    public sealed class RevUIDemoPanel : RevUIPanel
    {
        // ---------- [RevBind]：按节点名自动拿控件（路径默认 = 字段名去掉下划线） ----------
        [RevBind] private Button _btnAdd;          // 节点 btnAdd：加一条日志
        [RevBind] private Text _txtInfo;           // 节点 txtInfo：信息文本
        [RevBind] private Button _btnClose;        // 节点 btnClose：关闭面板

        private int _clicks;                        // 演示数据：按钮点击次数

        // ---------- 表现装配（只拿控件 / 只挂表现，不写业务） ----------
        protected override void OnBindView()
        {
            _txtInfo.text = "演示面板已打开。\n点「加一条日志」按钮试试；点右上角按钮关闭。";
        }

        // ---------- 每次打开的业务准备 ----------
        protected override void OnOpen()
        {
            _clicks = 0;
            Refresh();
        }

        // ---------- 按钮点击（按节点名分发；与字段绑定可混用，同一按钮只走一条路） ----------
        protected override void OnClick(string nodeName)
        {
            switch (nodeName)
            {
                case "btnAdd":
                    _clicks++;
                    Refresh();
                    break;

                case "btnClose":
                    CloseSelf();                     // 关自己：走框架关闭流程（动画 → OnClose → 回池/销毁）
                    break;
            }
        }

        /// <summary>把点击次数刷到文本上（数据落屏只在本类内完成）。</summary>
        private void Refresh() => _txtInfo.text = $"演示面板已打开。\n「加一条日志」点了 {_clicks} 次。\n点右上角 ✕ 关闭。";

        public override string ToString() => "RevUIDemoPanel";

        //下面这种就是直接通过特性的方式来监听
        [RevButtonClick("btnAdd")]
        private void OnBtnAddClick()
        {
            RevLog.Info("按钮被点击");
        }
    }
}
