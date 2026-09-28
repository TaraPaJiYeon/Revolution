// ============================================================
// RevIInputDevice.cs —— 输入设备契约
//
// 位置：Runtime\RevInput\Interfaces\
//
// 【要解决的问题】
//   默认设备是"Unity 的键鼠 + 触屏"，但输入还可以来自别处：
//     · **回放**：把录下来的采样序列喂回来（复现 bug、做自动化测试）
//     · **AI / 托管**：让脚本驱动角色（训练、观战、新手引导）
//     · **远程调试**：手机画面投到 PC，PC 上操作手机
//   只要实现这一个接口，上层业务代码（`RevInput.Pressed(...)`）一个字都不用改。
//
// 【三条铁律】
//   ① **只写快照、不读业务**：设备不参与判定，也不该知道"跳跃"是什么。
//   ② **必须填相位**：`KeyDown` / `KeyHeld` / `KeyUp` 三份掩码要如实写（内核靠它们算三态）。
//   ③ **不吞异常**：`Poll` 里出错就返回 false 并记一次失败原因，别抛出去（会打断整帧输入）。
//
// 【怎么接自己的设备】
//   <code>
//   RevInput.RegisterDevice(new MyReplayDevice("录屏_20260928"));   // 接管输入源
//   RevInput.RegisterDevice(null);                                  // 还给默认设备
//   </code>
// ============================================================

namespace Revolution
{
    /// <summary>输入设备契约：每帧往快照里写一份原始输入。</summary>
    public interface RevIInputDevice
    {
        /// <summary>设备名（用于日志与自检输出，例如 "Unity" / "Replay" / "AI"）。</summary>
        string Name { get; }

        /// <summary>
        /// 采集一帧。<b>写进 <paramref name="snapshot"/> 的键位 / 鼠标 / 指针字段</b>；
        /// 帧信息（DeltaTime / Realtime / Frame）由内核填，设备不用管。
        /// </summary>
        /// <returns>true = 本帧有数据；false = 本帧不参与（内核按"无输入"处理）。</returns>
        bool Poll(RevInputSnapshot snapshot);
    }
}
