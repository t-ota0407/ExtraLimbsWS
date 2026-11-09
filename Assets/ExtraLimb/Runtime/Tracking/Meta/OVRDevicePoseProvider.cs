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
                return $"HMD {State(head)}　左手 {State(leftHand)}　右手 {State(rightHand)}";
            }
        }

        static string State(ITrackedPoseSource source) => source != null && source.TryGetPose(out _) ? "追跡中" : "追跡なし";

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
            head = new OVRDevicePoseSource(OVRDevice.Head, cameraRig.centerEyeAnchor);
            leftHand = new OVRDevicePoseSource(OVRDevice.LeftHand, cameraRig.leftHandAnchor);
            rightHand = new OVRDevicePoseSource(OVRDevice.RightHand, cameraRig.rightHandAnchor);
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
    }
}
