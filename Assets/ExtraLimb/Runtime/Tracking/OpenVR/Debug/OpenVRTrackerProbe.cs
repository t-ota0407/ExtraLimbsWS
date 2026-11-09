using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Valve.VR;

namespace ExtraLimb.Tracking.OpenVR
{
    /// <summary>
    /// Step 0-3 の検証用。SteamVR（nullドライバでヘッドセットなし）に、描画しない補助アプリとして接続し、
    /// VIVEトラッカーの姿勢を読み取ってモニタに表示する。Quest Link（Metaのランタイム）と同時に動くかを確かめる。
    /// 基準トラッカー（腰の役）から見た入力トラッカーの変位も表示する。
    /// </summary>
    public class OpenVRTrackerProbe : MonoBehaviour
    {
        [Tooltip("RawAndUncalibrated ならSteamVRのルームセットアップなしで動く。変位だけを見るので十分")]
        public ETrackingUniverseOrigin universe = ETrackingUniverseOrigin.TrackingUniverseRawAndUncalibrated;
        [Tooltip("HMD内で動きを見るための立方体を並べる位置（ワールド座標、メートル）")]
        public Vector3 vizOrigin = new Vector3(0f, 1.2f, 0.6f);
        [Tooltip("立方体どうしの横方向の間隔（メートル）")]
        public float vizSpacing = 0.25f;
        public float vizCubeSize = 0.05f;

        class Device
        {
            public uint index;
            public string serial;
            public bool connected, valid;
            public ETrackingResult result;
            public Vector3 pos;
            public Quaternion rot = Quaternion.identity;
            public Vector3 firstPos;
            public bool hasFirst;
            public Transform viz;
        }

        static readonly Color[] Palette = { new Color(1f, 0.5f, 0.1f), new Color(0.2f, 0.7f, 1f), new Color(0.4f, 1f, 0.4f), new Color(1f, 0.3f, 0.6f), new Color(1f, 1f, 0.3f) };

        CVRSystem system;
        EVRInitError initError = EVRInitError.None;
        readonly TrackedDevicePose_t[] poses = new TrackedDevicePose_t[Valve.VR.OpenVR.k_unMaxTrackedDeviceCount];
        readonly List<Device> devices = new List<Device>();
        int refIdx = -1, inputIdx = -1;
        Vector3 neutralPos;
        Quaternion neutralRot = Quaternion.identity;
        bool hasNeutral;
        float fps;

        void Start()
        {
            system = Valve.VR.OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Background);
            if (initError != EVRInitError.None)
            {
                Debug.LogWarning("[ExtraLimb] OpenVRに接続できませんでした: " + initError);
                system = null;
            }
            else
            {
                Debug.Log("[ExtraLimb] OpenVRに補助アプリとして接続しました");
            }
        }

        void Update()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            if (system == null) return;

            var ev = new VREvent_t();
            uint evSize = (uint)Marshal.SizeOf(typeof(VREvent_t));
            while (system.PollNextEvent(ref ev, evSize))
            {
                if ((EVREventType)ev.eventType == EVREventType.VREvent_Quit)
                {
                    system.AcknowledgeQuit_Exiting();
                    Shutdown();
                    return;
                }
            }

            system.GetDeviceToAbsoluteTrackingPose(universe, 0f, poses);
            for (uint i = 0; i < poses.Length; i++)
            {
                if (system.GetTrackedDeviceClass(i) != ETrackedDeviceClass.GenericTracker) continue;
                var d = devices.Find(x => x.index == i);
                if (d == null)
                {
                    d = new Device { index = i, serial = GetSerial(i) };
                    devices.Add(d);
                    d.viz = CreateViz(d.serial, devices.Count - 1);
                    Debug.Log("[ExtraLimb] トラッカーを検出: " + d.serial + " (index " + i + ")");
                }
                var p = poses[i];
                d.connected = p.bDeviceIsConnected;
                d.valid = p.bPoseIsValid;
                d.result = p.eTrackingResult;
                if (d.valid)
                {
                    ToUnity(p.mDeviceToAbsoluteTracking, out d.pos, out d.rot);
                    if (!d.hasFirst) { d.firstPos = d.pos; d.hasFirst = true; }
                }
                if (d.viz != null)
                {
                    int slot = devices.IndexOf(d);
                    d.viz.position = vizOrigin + Vector3.right * vizSpacing * slot + (d.pos - d.firstPos);
                    d.viz.rotation = d.rot;
                    d.viz.gameObject.SetActive(d.valid);
                }
            }
            if (refIdx < 0 && devices.Count >= 1) refIdx = 0;
            if (inputIdx < 0 && devices.Count >= 2) inputIdx = 1;
        }

        string GetSerial(uint i)
        {
            var err = ETrackedPropertyError.TrackedProp_Success;
            var sb = new StringBuilder(64);
            system.GetStringTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_SerialNumber_String, sb, (uint)sb.Capacity, ref err);
            return err == ETrackedPropertyError.TrackedProp_Success ? sb.ToString() : "device" + i;
        }

        Transform CreateViz(string serial, int slot)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TrackerViz_" + serial;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, true);
            go.transform.localScale = Vector3.one * vizCubeSize;
            var r = go.GetComponent<Renderer>();
            var c = Palette[slot % Palette.Length];
            r.material.SetColor("_BaseColor", c);
            r.material.color = c;
            return go.transform;
        }

        // OpenVR（右手系）の3x4行列を、Unity（左手系）の位置と回転に変換する
        static void ToUnity(HmdMatrix34_t m, out Vector3 pos, out Quaternion rot)
        {
            var mat = Matrix4x4.identity;
            mat.m00 = m.m0; mat.m01 = m.m1; mat.m02 = -m.m2; mat.m03 = m.m3;
            mat.m10 = m.m4; mat.m11 = m.m5; mat.m12 = -m.m6; mat.m13 = m.m7;
            mat.m20 = -m.m8; mat.m21 = -m.m9; mat.m22 = m.m10; mat.m23 = -m.m11;
            pos = new Vector3(mat.m03, mat.m13, mat.m23);
            rot = Quaternion.LookRotation(mat.GetColumn(2), mat.GetColumn(1));
        }

        // 基準トラッカーの座標系で見た、入力トラッカーの位置と回転
        static void Relative(Device input, Device reference, out Vector3 pos, out Quaternion rot)
        {
            var inv = Quaternion.Inverse(reference.rot);
            pos = inv * (input.pos - reference.pos);
            rot = inv * input.rot;
        }

        void Recenter()
        {
            if (!Ready()) return;
            Relative(devices[inputIdx], devices[refIdx], out neutralPos, out neutralRot);
            hasNeutral = true;
            Debug.Log("[ExtraLimb] Re-center");
        }

        bool Ready()
        {
            return refIdx >= 0 && inputIdx >= 0 && refIdx != inputIdx && refIdx < devices.Count && inputIdx < devices.Count;
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 720, 600), GUI.skin.box);
            GUILayout.Label("<b>OpenVR トラッカー検証（Step 0-3）</b>  画面更新 " + fps.ToString("F0") + " fps", Rich());
            if (system == null)
            {
                string hint = initError == EVRInitError.Init_NoServerForBackgroundApp ? "（SteamVRが起動していません。先にSteamVRを起動してから再生してください）" : "";
                GUILayout.Label("接続できていません: " + initError + hint);
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label("接続: OK（補助アプリ）  空間: " + universe + "  検出したトラッカー: " + devices.Count + "台");
            for (int k = 0; k < devices.Count; k++)
            {
                var d = devices[k];
                GUILayout.BeginHorizontal();
                var c = Palette[k % Palette.Length];
                GUILayout.Label("<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">■</color> " + d.serial, Rich(), GUILayout.Width(170));
                GUILayout.Label((d.connected ? "接続" : "切断") + " / " + (d.valid ? "追跡中" : "追跡なし") + " / " + d.result, GUILayout.Width(260));
                GUILayout.Label("位置 " + d.pos.ToString("F3") + " m", GUILayout.Width(170));
                if (GUILayout.Button(k == refIdx ? "[基準]" : "基準にする", GUILayout.Width(80))) { refIdx = k; hasNeutral = false; }
                if (GUILayout.Button(k == inputIdx ? "[入力]" : "入力にする", GUILayout.Width(80))) { inputIdx = k; hasNeutral = false; }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);
            if (Ready())
            {
                Relative(devices[inputIdx], devices[refIdx], out var rp, out var rr);
                if (!hasNeutral) Recenter();
                var dp = rp - neutralPos;
                float dAngle = Quaternion.Angle(neutralRot, rr);
                GUILayout.Label("<b>基準から見た入力の変位</b>（Re-center時を0とする）", Rich());
                GUILayout.Label(string.Format("Δx {0,7:F1} cm   Δy {1,7:F1} cm   Δz {2,7:F1} cm   |Δ| {3,6:F1} cm   回転 {4,6:F1}°", dp.x * 100f, dp.y * 100f, dp.z * 100f, dp.magnitude * 100f, dAngle));
                if (GUILayout.Button("Re-center（今の姿勢を0にする）", GUILayout.Width(260))) Recenter();
            }
            else
            {
                GUILayout.Label(devices.Count < 2 ? "変位の表示には2台目のトラッカーが必要です（1台の場合は上の位置の変化で確認）" : "基準と入力に別々のトラッカーを選んでください");
            }
            GUILayout.EndArea();
        }

        static GUIStyle rich;
        static GUIStyle Rich()
        {
            if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true };
            return rich;
        }

        void Shutdown()
        {
            if (system != null)
            {
                Valve.VR.OpenVR.Shutdown();
                system = null;
                Debug.Log("[ExtraLimb] OpenVRから切断しました");
            }
        }

        void OnDestroy() { Shutdown(); }
        void OnApplicationQuit() { Shutdown(); }
    }
}
