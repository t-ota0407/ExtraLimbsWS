using System;
using ExtraLimb.Body;
using ExtraLimb.Experiment;
using UnityEngine;
using UnityEngine.XR;

namespace ExtraLimb.UI
{
    /// <summary>
    /// モニタに映す第三者視点のカメラ（Step 4-2b、計画書の Part 2-4）。HMD には描かず、参加者を斜め後方などから映す。
    /// <list type="bullet">
    /// <item>視点は参加者の体（Re-center のときの腰の位置と向き）からの相対で決め、V で順に切り替える。最後の視点は「HMD の映像」（モニタに HMD のミラーを出す）。</item>
    /// <item>モニタだけの可視化（DisplayLayers.MonitorOnly）を描き、HMD への通知（HmdOnly）は描かない。ほかのカメラ（HMD）は逆にする。
    /// 「参加者にも可視化を見せる」（フェーズC）をオンにすると、HMD のカメラにも可視化を描く。</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(10070)]
    [RequireComponent(typeof(Camera))]
    public class SpectatorCamera : MonoBehaviour
    {
        /// <summary>視点：体の位置と向き（前が +z、右が +x、床が y=0）から見たカメラの位置と、見る点。</summary>
        [Serializable]
        public struct Viewpoint
        {
            public string name;
            public Vector3 offset;
            public Vector3 lookAt;

            public Viewpoint(string name, Vector3 offset, Vector3 lookAt)
            {
                this.name = name;
                this.offset = offset;
                this.lookAt = lookAt;
            }
        }

        [Tooltip("空ならシーンから探す")]
        [SerializeField] LimbSession session;
        [Tooltip("空ならシーンから探す（体の位置と向きに使う）")]
        [SerializeField] BodyFrameProvider body;
        [SerializeField] Viewpoint[] viewpoints =
        {
            new Viewpoint("斜め後方（右）", new Vector3(0.9f, 1.55f, -1.4f), new Vector3(0f, 1.0f, 0.35f)),
            new Viewpoint("斜め後方（左）", new Vector3(-0.9f, 1.55f, -1.4f), new Vector3(0f, 1.0f, 0.35f)),
            new Viewpoint("右横", new Vector3(1.7f, 1.25f, 0.25f), new Vector3(0f, 1.0f, 0.25f)),
            new Viewpoint("斜め前（右）", new Vector3(1.3f, 1.6f, 1.05f), new Vector3(0f, 1.0f, 0.1f)),
        };
        [SerializeField] int viewIndex;
        [SerializeField] float fieldOfView = 55f;
        [Tooltip("V の切替に「HMD の映像」を含めるか（HMD のないシーンではオフ）")]
        [SerializeField] bool allowHmdView = true;
        [Tooltip("実験者のための可視化を、参加者（HMD）にも見せる（フェーズC）")]
        [SerializeField] bool overlayInHmd;

        Camera view;
        Pose anchor = new Pose(Vector3.zero, Quaternion.identity);
        bool anchored;
        float nextMaskUpdate;

        /// <summary>モニタに HMD の映像を出しているか。</summary>
        public bool IsHmdView { get; private set; }

        /// <summary>今の視点の名前。</summary>
        public string ViewName => IsHmdView ? "HMD の映像" : viewpoints.Length > 0 ? viewpoints[Mathf.Clamp(viewIndex, 0, viewpoints.Length - 1)].name : "—";

        /// <summary>視点の基準（体の位置と向き）。</summary>
        public Pose Anchor => anchor;

        /// <summary>画面の右で、実験者パネルが覆っている幅（px）。第三者視点はその左に描く（参加者がパネルに隠れないように）。</summary>
        public float RightMarginPixels { get; set; }

        /// <summary>実験者のための可視化を、参加者（HMD）にも見せるか。切り替えると記録に残す。</summary>
        public bool OverlayInHmd
        {
            get => overlayInHmd;
            set
            {
                if (overlayInHmd == value) return;
                overlayInHmd = value;
                ApplyMasks();
                session?.Report(SessionEventTypes.OverlayInHmd, "session", "visible", value ? "false" : "true", value ? "true" : "false",
                    value ? "可視化を参加者にも見せます" : "可視化を参加者に見せません");
            }
        }

        /// <summary>視点の位置と向き（基準と視点から）。</summary>
        public static Pose ComputePose(Pose anchorPose, Viewpoint viewpoint)
        {
            var position = anchorPose.position + anchorPose.rotation * viewpoint.offset;
            var target = anchorPose.position + anchorPose.rotation * viewpoint.lookAt;
            var direction = target - position;
            var rotation = direction.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(direction, Vector3.up) : anchorPose.rotation;
            return new Pose(position, rotation);
        }

        /// <summary>体の姿勢から、視点の基準（床の上、水平の向き）を作る。</summary>
        public static Pose AnchorFrom(Pose bodyPose)
        {
            var forward = Vector3.ProjectOnPlane(bodyPose.rotation * Vector3.forward, Vector3.up);
            var rotation = forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            return new Pose(new Vector3(bodyPose.position.x, 0f, bodyPose.position.z), rotation);
        }

        void Awake()
        {
            view = GetComponent<Camera>();
            // モニタ用のカメラ：XR には描かない
            view.stereoTargetEye = StereoTargetEyeMask.None;
            view.fieldOfView = fieldOfView;
            view.depth = Mathf.Max(view.depth, 10f);
        }

        void OnEnable()
        {
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            if (body == null) body = FindFirstObjectByType<BodyFrameProvider>();
            if (session != null) session.Recentered += UpdateAnchor;
        }

        void OnDisable()
        {
            if (session != null) session.Recentered -= UpdateAnchor;
        }

        void Start()
        {
            ApplyView();
        }

        /// <summary>次の視点にする（最後は HMD の映像、その次は最初の視点）。</summary>
        public void NextView()
        {
            int count = viewpoints.Length + (allowHmdView ? 1 : 0);
            if (count == 0) return;
            int current = IsHmdView ? viewpoints.Length : viewIndex;
            int next = (current + 1) % count;
            IsHmdView = next >= viewpoints.Length;
            if (!IsHmdView) viewIndex = next;
            ApplyView();
            Debug.Log($"[ExtraLimb] モニタの視点：{ViewName}");
        }

        /// <summary>今の体の位置と向きを、視点の基準にする（Re-center のたびにも呼ばれる）。</summary>
        public void UpdateAnchor()
        {
            if (body == null || !body.IsReady || !body.TryGetFrame(HumanBodyBones.Hips, out var hips)) return;
            anchor = AnchorFrom(hips);
            anchored = true;
        }

        void ApplyView()
        {
            if (view == null) return;
            view.enabled = !IsHmdView;
            // HMD の映像を出すときだけ、XR のミラーをモニタに描く
            if (XRSettings.enabled) XRSettings.gameViewRenderMode = IsHmdView ? GameViewRenderMode.LeftEye : GameViewRenderMode.None;
            ApplyMasks();
        }

        void ApplyMasks()
        {
            if (view == null) return;
            view.cullingMask = DisplayLayers.MonitorMask(view.cullingMask);
            foreach (var camera in Camera.allCameras)
                if (camera != view) camera.cullingMask = DisplayLayers.HmdMask(camera.cullingMask, overlayInHmd);
        }

        void LateUpdate()
        {
            if (!anchored) UpdateAnchor();
            // カメラは後から増えることがある（OVRCameraRig など）ので、ときどき振り分け直す
            if (Time.unscaledTime >= nextMaskUpdate)
            {
                nextMaskUpdate = Time.unscaledTime + 0.5f;
                ApplyMasks();
            }
            if (IsHmdView || viewpoints.Length == 0) return;
            float width = Screen.width > 0 ? 1f - RightMarginPixels / Screen.width : 1f;
            var rect = new Rect(0f, 0f, Mathf.Clamp(width, 0.3f, 1f), 1f);
            if (view.rect != rect) view.rect = rect;
            var pose = ComputePose(anchor, viewpoints[Mathf.Clamp(viewIndex, 0, viewpoints.Length - 1)]);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }
    }
}
