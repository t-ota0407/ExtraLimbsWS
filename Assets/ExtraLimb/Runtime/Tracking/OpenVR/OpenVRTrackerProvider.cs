using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;

namespace ExtraLimb.Tracking.OpenVR
{
    /// <summary>
    /// SteamVR（nullドライバでヘッドセットなし）に、描画しない補助アプリとして接続し、
    /// VIVE トラッカーを OpenVRTrackerSource として TrackerRegistry に登録する。
    /// SteamVR が起動していなければ一定間隔で接続し直すので、再生中に SteamVR を起動してもよい。
    /// 手順は Docs/SteamVR_NullDriver_Setup.md。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class OpenVRTrackerProvider : MonoBehaviour, ITrackerProviderStatus
    {
        [SerializeField] TrackerRegistryHost registryHost;

        [Tooltip("RawAndUncalibrated ならSteamVRのルームセットアップなしで動く。変位は腰から見た値なので十分")]
        [SerializeField] ETrackingUniverseOrigin universe = ETrackingUniverseOrigin.TrackingUniverseRawAndUncalibrated;

        [Tooltip("SteamVRに接続できないときに接続し直す間隔（秒）")]
        [SerializeField] float retryIntervalSeconds = 2f;

        TrackerRegistry registry;
        CVRSystem system;
        EVRInitError lastError = EVRInitError.None;
        float nextRetryTime;
        readonly TrackedDevicePose_t[] poses = new TrackedDevicePose_t[Valve.VR.OpenVR.k_unMaxTrackedDeviceCount];
        readonly Dictionary<string, OpenVRTrackerSource> sourcesBySerial = new Dictionary<string, OpenVRTrackerSource>();
        readonly OpenVRTrackerSource[] sourcesByIndex = new OpenVRTrackerSource[Valve.VR.OpenVR.k_unMaxTrackedDeviceCount];

        public bool IsConnected => system != null;
        public IReadOnlyCollection<OpenVRTrackerSource> Sources => sourcesBySerial.Values;
        public string ProviderName => "SteamVR";

        public string StatusText
        {
            get
            {
                if (system != null)
                {
                    int tracked = 0;
                    foreach (var source in sourcesBySerial.Values) if (source.TryGetPose(out _)) tracked++;
                    return $"接続中（トラッカー {sourcesBySerial.Count}台、追跡中 {tracked}台）";
                }
                if (lastError == EVRInitError.Init_NoServerForBackgroundApp)
                    return "SteamVRが起動していません（起動すると自動で接続します）";
                return $"未接続（{lastError}）。{retryIntervalSeconds:F0}秒ごとに接続し直します";
            }
        }

        void OnEnable()
        {
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            if (registryHost == null)
            {
                Debug.LogError("[ExtraLimb] TrackerRegistryHost がシーンにないため、SteamVRのトラッカーを登録できません");
                enabled = false;
                return;
            }
            registry = registryHost.Registry;
            nextRetryTime = 0f;
        }

        void OnDisable()
        {
            Disconnect();
            if (registry != null)
                foreach (var serial in sourcesBySerial.Keys) registry.Unregister(serial);
            sourcesBySerial.Clear();
            System.Array.Clear(sourcesByIndex, 0, sourcesByIndex.Length);
        }

        void OnApplicationQuit() => Disconnect();

        void Update()
        {
            if (system == null)
            {
                if (Time.unscaledTime >= nextRetryTime) TryConnect();
                if (system == null) return;
            }

            if (!PollEvents()) return;

            system.GetDeviceToAbsoluteTrackingPose(universe, 0f, poses);
            for (uint i = 0; i < poses.Length; i++)
            {
                var deviceClass = system.GetTrackedDeviceClass(i);
                if (deviceClass != ETrackedDeviceClass.GenericTracker)
                {
                    // その番号の機器が外れた、または別の種類に変わった
                    if (sourcesByIndex[i] != null)
                    {
                        sourcesByIndex[i].IsConnected = false;
                        sourcesByIndex[i] = null;
                    }
                    continue;
                }

                var source = sourcesByIndex[i] ?? Attach(i);
                if (source == null) continue;
                var pose = poses[i];
                source.IsConnected = pose.bDeviceIsConnected;
                source.IsPoseValid = pose.bPoseIsValid;
                source.TrackingResult = pose.eTrackingResult;
                if (pose.bPoseIsValid) source.LastPose = OpenVRPoseUtility.ToUnityPose(pose.mDeviceToAbsoluteTracking);
            }
        }

        // 新しく見つかったトラッカーを登録する。同じシリアルが前にあれば、それを使い直す
        OpenVRTrackerSource Attach(uint index)
        {
            string serial = OpenVRPoseUtility.GetStringProperty(system, index, ETrackedDeviceProperty.Prop_SerialNumber_String);
            if (string.IsNullOrEmpty(serial)) return null;

            if (!sourcesBySerial.TryGetValue(serial, out var source))
            {
                source = new OpenVRTrackerSource(serial, index);
                string deviceType = OpenVRPoseUtility.GetStringProperty(system, index, ETrackedDeviceProperty.Prop_RegisteredDeviceType_String);
                source.SteamVRRole = OpenVRPoseUtility.GetTrackerRole(deviceType);
                source.Site = OpenVRPoseUtility.SiteFromTrackerRole(source.SteamVRRole);
                sourcesBySerial.Add(serial, source);
                registry.Register(source);
                Debug.Log($"[ExtraLimb] SteamVRのトラッカーを登録: {serial}（役割 {source.SteamVRRole ?? "なし"} → {source.Site}）");
            }
            source.DeviceIndex = index;
            sourcesByIndex[index] = source;
            return source;
        }

        void TryConnect()
        {
            nextRetryTime = Time.unscaledTime + retryIntervalSeconds;
            var error = EVRInitError.None;
            var connected = Valve.VR.OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
            if (error != EVRInitError.None)
            {
                if (error != lastError) Debug.Log($"[ExtraLimb] SteamVRに接続できません: {error}");
                lastError = error;
                return;
            }
            system = connected;
            lastError = EVRInitError.None;
            Debug.Log("[ExtraLimb] SteamVRに補助アプリとして接続しました");
        }

        // false を返したら、SteamVR が終了した
        bool PollEvents()
        {
            var vrEvent = new VREvent_t();
            uint size = (uint)Marshal.SizeOf(typeof(VREvent_t));
            while (system.PollNextEvent(ref vrEvent, size))
            {
                if ((EVREventType)vrEvent.eventType == EVREventType.VREvent_Quit)
                {
                    system.AcknowledgeQuit_Exiting();
                    Disconnect();
                    return false;
                }
            }
            return true;
        }

        void Disconnect()
        {
            if (system == null) return;
            Valve.VR.OpenVR.Shutdown();
            system = null;
            foreach (var source in sourcesBySerial.Values) source.IsConnected = false;
            System.Array.Clear(sourcesByIndex, 0, sourcesByIndex.Length);
            nextRetryTime = Time.unscaledTime + retryIntervalSeconds;
            Debug.Log("[ExtraLimb] SteamVRから切断しました");
        }
    }
}
