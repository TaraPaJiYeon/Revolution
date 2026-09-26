// ============================================================
// RevUIPopupMask.cs —— 弹窗遮罩（自动挡点击）
//
// 位置：Runtime\RevUISystem\Implementation\
//
// 【它解决什么】
//   弹窗弹出来时，下面的界面必须点不动 —— 否则会出现"弹窗还开着，底下的按钮已经响应了"
//   这种线上最容易被玩家发现的 bug（王者的 Form_TransparentMask 就是干这个的）。
//
// 【为什么由框架自动做，而不是要求每个预制体自带遮罩】
//   要求美术/程序"每个弹窗都记得摆一块全屏 Image"是纪律，纪律迟早会漏；
//   而且遮罩的**层级位置**很讲究（必须夹在"被挡住的面板"和"挡住别人的面板"之间），
//   靠人工摆更容易错。所以框架按层级各准备一块**透明**挡板，自动显示在正确的位置：
//     · 它是透明的（只挡点击、不遮画面）—— 想要半透明黑底，就在**面板预制体里**自己画一层；
//     · 面板声明 Mask = ClickBlock（或该层推断为挡，见 RevUILayerUtil）时才启用；
//     · 点击它默认关掉该层最上面的那个弹窗（RevUISetting.ClickMaskClosesTop，可关）。
//
// 【一个层一块，不是每个面板一块】
//   同一层里连着弹三个窗，也只需要最上面那块遮罩 —— 所以这里按层缓存，按需激活。
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Revolution
{
    /// <summary>遮罩管理器：每个层级一块透明挡板，按需出现、自动摆到正确位置</summary>
    internal sealed class RevUIPopupMask
    {
        private readonly Dictionary<RevUILayer, RectTransform> _masks = new Dictionary<RevUILayer, RectTransform>();

        /// <summary>
        /// 更新某一层的遮罩：
        /// </summary>
        /// <param name="layer">层级</param>
        /// <param name="layerParent">该层的根节点</param>
        /// <param name="owner">该层"最上面那个需要遮罩的面板"；null = 这层不需要遮罩</param>
        public void Apply(RevUILayer layer, RectTransform layerParent, RevUIPanel owner)
        {
            if (layerParent == null) return;

            // 不需要遮罩，而且本来就没造过 → 什么都不做（省一个 GameObject）
            if (owner == null && !_masks.ContainsKey(layer)) return;

            RectTransform mask = Ensure(layer, layerParent);
            if (mask == null) return;

            mask.gameObject.SetActive(owner != null);
            if (owner == null) return;

            // ★ 摆位：把遮罩插到 owner 的"正下方"——
            //   先把它设成 owner 当前的兄弟序号，owner 会被顶上去一位，于是遮罩正好在它下面。
            mask.SetSiblingIndex(owner.transform.GetSiblingIndex());
        }

        /// <summary>清掉所有遮罩（进 Play / 切换工程时那些对象已经随场景没了）</summary>
        public void Clear() => _masks.Clear();

        private RectTransform Ensure(RevUILayer layer, RectTransform layerParent)
        {
            if (_masks.TryGetValue(layer, out RectTransform exist) && exist != null) return exist;

            var go = new GameObject($"RevUIMask_{layer}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RevUIMaskClickCatcher));

            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(layerParent, false);
            RevUIRoot.ApplyUILayer(go);                // ★ 与面板一致：Camera 模式下也要被 UI 相机认到
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image image = go.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);   // 透明：只挡点击，画面由面板自己负责
            image.raycastTarget = true;                // ★ 关键：能吃到点击靠的是这个

            go.GetComponent<RevUIMaskClickCatcher>().Layer = layer;

            _masks[layer] = rt;
            return rt;
        }
    }

    /// <summary>遮罩的点击接收者：点一下关掉该层最上面的弹窗（可关）</summary>
    [DisallowMultipleComponent]
    internal sealed class RevUIMaskClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>自己属于哪一层（决定"点一下该关谁"）</summary>
        public RevUILayer Layer;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!RevUISetting.ClickMaskClosesTop) return;

            RevUIManager.Instance.CloseTopOf(Layer);
        }
    }
}
