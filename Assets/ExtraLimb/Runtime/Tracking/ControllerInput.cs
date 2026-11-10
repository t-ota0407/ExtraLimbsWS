using ExtraLimb.Core;
using UnityEngine.XR;

namespace ExtraLimb.Tracking
{
    /// <summary>コントローラーのトリガー・グリップの押し込み量（0〜1）と振動。</summary>
    public interface IControllerInput
    {
        string Name { get; }
        bool IsConnected(ControllerHand hand);

        /// <summary>人差し指のトリガー（0〜1）。</summary>
        float GetTrigger(ControllerHand hand);

        /// <summary>中指のグリップ（0〜1）。</summary>
        float GetGrip(ControllerHand hand);

        /// <summary>短く振動させる。</summary>
        void Vibrate(ControllerHand hand, float amplitude, float seconds);

        /// <summary>毎フレーム呼ぶ（振動を止める時刻の管理など）。</summary>
        void Tick(float deltaTime);
    }

    /// <summary>
    /// 使うコントローラー入力。既定は Unity の XR 入力（OpenXR の Oculus Touch プロファイル）。
    /// Meta XR SDK があるときは ExtraLimb.Tracking.Meta が起動時に OVRInput も読むものに差し替える。
    /// </summary>
    public static class ControllerInput
    {
        static IControllerInput provider;

        public static IControllerInput Provider
        {
            get => provider ??= new XRControllerInput();
            set => provider = value;
        }
    }

    /// <summary>Unity の XR 入力（InputDevices）でコントローラーを読む。</summary>
    public class XRControllerInput : IControllerInput
    {
        public string Name => "XR入力";

        static InputDevice Device(ControllerHand hand) =>
            InputDevices.GetDeviceAtXRNode(hand == ControllerHand.Left ? XRNode.LeftHand : XRNode.RightHand);

        public bool IsConnected(ControllerHand hand) => Device(hand).isValid;

        public float GetTrigger(ControllerHand hand) =>
            Device(hand).TryGetFeatureValue(CommonUsages.trigger, out float value) ? value : 0f;

        public float GetGrip(ControllerHand hand) =>
            Device(hand).TryGetFeatureValue(CommonUsages.grip, out float value) ? value : 0f;

        public void Vibrate(ControllerHand hand, float amplitude, float seconds)
        {
            var device = Device(hand);
            if (device.isValid && device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                device.SendHapticImpulse(0, amplitude, seconds);
        }

        public void Tick(float deltaTime)
        {
        }
    }
}
