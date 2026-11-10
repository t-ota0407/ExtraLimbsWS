using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Tracking.Meta
{
    /// <summary>
    /// コントローラーを Meta XR SDK（OVRInput）と Unity の XR 入力の両方で読み、大きい方を使う（片方が読めない環境でも動くように）。
    /// 振動は OVRInput で出し、指定の秒数で止める。OVRManager がないシーンでは Unity の XR 入力だけを使う。
    /// 再生を始めるときに ControllerInput.Provider をこれに差し替える。
    /// </summary>
    public class MetaControllerInput : IControllerInput
    {
        readonly XRControllerInput xr = new XRControllerInput();
        readonly float[] vibrateUntil = { -1f, -1f };
        float time;

        public string Name => "OVRInput＋XR入力";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => ControllerInput.Provider = new MetaControllerInput();

        static bool OvrReady => OVRManager.instance != null;

        static OVRInput.Controller Controller(ControllerHand hand) =>
            hand == ControllerHand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

        public bool IsConnected(ControllerHand hand) =>
            (OvrReady && OVRInput.IsControllerConnected(Controller(hand))) || xr.IsConnected(hand);

        public float GetTrigger(ControllerHand hand)
        {
            float ovr = OvrReady ? OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, Controller(hand)) : 0f;
            return Mathf.Max(ovr, xr.GetTrigger(hand));
        }

        public float GetGrip(ControllerHand hand)
        {
            float ovr = OvrReady ? OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, Controller(hand)) : 0f;
            return Mathf.Max(ovr, xr.GetGrip(hand));
        }

        public void Vibrate(ControllerHand hand, float amplitude, float seconds)
        {
            if (!OvrReady)
            {
                xr.Vibrate(hand, amplitude, seconds);
                return;
            }
            OVRInput.SetControllerVibration(1f, Mathf.Clamp01(amplitude), Controller(hand));
            vibrateUntil[(int)hand] = time + seconds;
        }

        public void Tick(float deltaTime)
        {
            time += deltaTime;
            for (int i = 0; i < vibrateUntil.Length; i++)
            {
                if (vibrateUntil[i] < 0f || time < vibrateUntil[i]) continue;
                vibrateUntil[i] = -1f;
                if (OvrReady) OVRInput.SetControllerVibration(0f, 0f, Controller((ControllerHand)i));
            }
        }
    }
}
