using UnityEngine;

namespace ExtraLimb.Tracking.Meta
{
    /// <summary>シーンの OVRCameraRig から、頭と両手の入力ソースを TrackerRegistry に登録する。</summary>
    [DefaultExecutionOrder(-50)]
    public class OVRDevicePoseProvider : MonoBehaviour, ITrackerProviderStatus
    {
        [SerializeField] TrackerRegistryHost registryHost;
        [Tooltip("指定がなければシーンから探す")]
        [SerializeField] OVRCameraRig cameraRig;

        TrackerRegistry registry;
        OVRDevicePoseSource head, leftHand, rightHand;

        public string ProviderName => "Quest";

        public string StatusText
        {
            get
            {
                if (cameraRig == null) return "OVRCameraRig がシーンにありません";
                if (head == null) return "未登録";
                return $"HMD {State(head)}　左手 {State(leftHand)}　右手 {State(rightHand)}";
            }
        }

        static string State(OVRDevicePoseSource source)
        {
            if (source == null) return "なし";
            if (source.Anchor == null) return "アンカーなし";
            if (!source.TryGetPose(out _)) return "追跡なし";
            return source.IsTrackedByXR() ? "追跡中" : "追跡中（OVRのみ）";
        }

        void OnEnable()
        {
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            if (cameraRig == null) cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (registryHost == null || cameraRig == null)
            {
                Debug.LogWarning("[ExtraLimb] TrackerRegistryHost または OVRCameraRig がないため、Questの入力ソースを登録しません");
                return;
            }
            registry = registryHost.Registry;
            head = new OVRDevicePoseSource(OVRDevice.Head, cameraRig);
            leftHand = new OVRDevicePoseSource(OVRDevice.LeftHand, cameraRig);
            rightHand = new OVRDevicePoseSource(OVRDevice.RightHand, cameraRig);
            registry.Register(head);
            registry.Register(leftHand);
            registry.Register(rightHand);
        }

        void OnDisable()
        {
            if (registry == null) return;
            registry.Unregister(head.Id);
            registry.Unregister(leftHand.Id);
            registry.Unregister(rightHand.Id);
            registry = null;
        }

        /// <summary>追跡の判定に使う値をコンソールに出す（SendMessage("LogDiagnostics") で呼べる）。</summary>
        public void LogDiagnostics()
        {
            if (head == null)
            {
                Debug.Log("[ExtraLimb] Quest診断：ソースが登録されていません");
                return;
            }
            Debug.Log($"[ExtraLimb] Quest診断：isHmdPresent={Safe(() => OVRManager.isHmdPresent)} isUserPresent={Safe(() => OVRManager.instance != null && OVRManager.instance.isUserPresent)}" +
                      $"　頭 {Describe(head)}　左手 {Describe(leftHand)}　右手 {Describe(rightHand)}");
        }

        static string Describe(OVRDevicePoseSource source)
        {
            var anchor = source.Anchor;
            string position = anchor != null ? anchor.position.ToString("F3") : "アンカーなし";
            return $"XR={source.IsTrackedByXR()} OVR={Safe(source.IsTrackedByOVR)} 位置={position}";
        }

        static string Safe(System.Func<bool> read)
        {
            try { return read().ToString(); }
            catch (System.Exception e) { return e.GetType().Name; }
        }
    }
}
