using ExtraLimb.Experiment;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtraLimb.UI
{
    /// <summary>
    /// HMD の中の通知（Step 4-2b、計画書の Part 2-4「HMD内」）：「条件が変わります」、課題開始のカウントダウン（3, 2, 1, はじめ）。
    /// UI Toolkit で文字を RenderTexture に描き、HMD のカメラの前（頭に付いて動く）に置いた板に貼る。板は DisplayLayers.HmdOnly に置くので、モニタには映らない。
    /// 今出している文は実験者パネルの上の表示にも出す。出した通知は記録に残す（hmd_notice）。
    /// 生やす位置・入力の部位・マッピングの型・2本目の表示を、パネルかホットキーで変えたときは、自動で「条件が変わります」を出す。
    /// HMD のカメラがないシーン（DesktopDev）では、確認用にモニタのカメラの前に出す。
    /// </summary>
    [DefaultExecutionOrder(10080)]
    public class HmdNotice : MonoBehaviour
    {
        [Tooltip("空ならシーンから探す")]
        [SerializeField] LimbSession session;
        [Tooltip("空なら、第三者視点のカメラ以外で XR に描くカメラ（OVRCameraRig の CenterEyeAnchor）")]
        [SerializeField] Camera hmdCamera;
        [Tooltip("文字の見た目のテーマ。空ならシーンの UIDocument のものを使う")]
        [SerializeField] ThemeStyleSheet theme;
        [Tooltip("目からの距離（m）")]
        [SerializeField] float distance = 1.2f;
        [Tooltip("目の高さからのずれ（m、下が負）")]
        [SerializeField] float heightOffset = -0.08f;
        [Tooltip("板の大きさ（m）")]
        [SerializeField] Vector2 size = new Vector2(0.56f, 0.14f);
        [SerializeField] Vector2Int resolution = new Vector2Int(1024, 256);
        [SerializeField] float fontSize = 110f;
        [SerializeField] float defaultSeconds = 2.5f;
        [Tooltip("生やす位置・入力の部位・型・2本目を変えたら、自動で通知する")]
        [SerializeField] bool notifyConditionChanges = true;
        [SerializeField] string conditionChangedText = "条件が変わります";
        [Tooltip("HMD のカメラがないとき、確認用にモニタのカメラの前に出す")]
        [SerializeField] bool fallbackToMonitor = true;

        readonly NoticeSchedule schedule = new NoticeSchedule();
        RenderTexture texture;
        PanelSettings settings;
        UIDocument document;
        GameObject documentObject;
        Label label;
        Transform board;
        Camera attachedTo;
        string shown = "";
        string lastReported = "";
        float lastReportTime = float.NegativeInfinity;
        float nextCameraCheck;

        /// <summary>今 HMD に出している文（なければ空文字）。</summary>
        public string CurrentText => shown;

        /// <summary>HMD のカメラに付いているか（false ならモニタのカメラの前に出している）。</summary>
        public bool OnHmd { get; private set; }

        void OnEnable()
        {
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            if (session != null) session.EventRaised += OnSessionEvent;
        }

        void OnDisable()
        {
            if (session != null) session.EventRaised -= OnSessionEvent;
        }

        void OnDestroy()
        {
            if (documentObject != null) Destroy(documentObject);
            if (texture != null) texture.Release();
            if (settings != null) Destroy(settings);
        }

        /// <summary>文を seconds 秒（省略すると既定の秒数）だけ出す。</summary>
        public void Show(string text, float seconds = -1f)
        {
            schedule.Show(Time.unscaledTimeAsDouble, text, seconds > 0f ? seconds : defaultSeconds);
            Report(text);
        }

        /// <summary>from から1秒ずつ数え、最後に doneText を出す（課題の開始など）。</summary>
        public void Countdown(int from = 3, string doneText = "はじめ")
        {
            schedule.Countdown(Time.unscaledTimeAsDouble, from, doneText);
            Report($"カウントダウン {from}→{doneText}");
        }

        /// <summary>出している通知を消す。</summary>
        public void Clear() => schedule.Clear();

        void Report(string text)
        {
            // 同じ文を続けて出したとき（F1 で左右の2本が変わるなど）は1回だけ記録する
            if (text == lastReported && Time.unscaledTime - lastReportTime < 0.5f) return;
            lastReported = text;
            lastReportTime = Time.unscaledTime;
            session?.Report(SessionEventTypes.HmdNotice, "participant", "text", "", text, $"HMD に「{text}」を出しました");
        }

        void OnSessionEvent(SessionEvent e)
        {
            if (!notifyConditionChanges || e.origin == ChangeOrigins.System) return;
            bool condition = e.type == SessionEventTypes.TwoLimbs
                             || (e.type == SessionEventTypes.Config && (e.key == "attachPointId" || e.key == "inputSite" || e.key == "mappingPresetId"));
            if (condition) Show(conditionChangedText);
        }

        void LateUpdate()
        {
            if (document == null) Build();
            if (board == null) return;
            AttachToCamera();

            string text = schedule.TextAt(Time.unscaledTimeAsDouble);
            if (text != shown)
            {
                shown = text;
                if (label != null) label.text = text;
            }
            board.gameObject.SetActive(shown.Length > 0 && attachedTo != null);
        }

        void AttachToCamera()
        {
            // カメラを探すのは 0.5 秒ごと（Camera.allCameras は毎回配列を作るため）
            if (attachedTo != null && Time.unscaledTime < nextCameraCheck) return;
            nextCameraCheck = Time.unscaledTime + 0.5f;
            var camera = hmdCamera != null ? hmdCamera : FindHmdCamera();
            bool onHmd = camera != null;
            if (camera == null && fallbackToMonitor)
            {
                var spectator = FindFirstObjectByType<SpectatorCamera>();
                camera = spectator != null ? spectator.GetComponent<Camera>() : Camera.main;
            }
            if (camera == attachedTo) return;
            attachedTo = camera;
            OnHmd = onHmd;
            if (camera == null) return;
            board.SetParent(camera.transform, false);
            board.localPosition = new Vector3(0f, heightOffset, distance);
            board.localRotation = Quaternion.identity;
            // HMD にだけ描く。確認用にモニタに出すときは、モニタにだけ描く
            DisplayLayers.SetLayerRecursively(board.gameObject, onHmd ? DisplayLayers.HmdOnly : DisplayLayers.MonitorOnly);
        }

        static Camera FindHmdCamera()
        {
            Camera found = null;
            foreach (var camera in Camera.allCameras)
            {
                if (camera.GetComponent<SpectatorCamera>() != null || camera.stereoTargetEye == StereoTargetEyeMask.None || camera.targetTexture != null) continue;
                if (camera.CompareTag("MainCamera")) return camera;
                if (found == null) found = camera;
            }
            return found;
        }

        void Build()
        {
            texture = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32) { name = "HmdNotice" };
            texture.Create();

            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "HmdNoticePanel";
            settings.targetTexture = texture;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.clearColor = true;
            settings.colorClearValue = new Color(0.06f, 0.07f, 0.09f, 1f);
            settings.themeStyleSheet = theme != null ? theme : FindTheme();

            // UIDocument の子に置くと親のパネル（画面）に入ってしまうので、シーンの一番上に置く
            var go = new GameObject("HmdNotice_UI");
            go.SetActive(false);
            documentObject = go;
            document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
            go.SetActive(true);

            var root = document.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;
            root.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
            UIFonts.ApplyJapanese(root);
            label = new Label("");
            label.style.fontSize = fontSize;
            label.style.color = Color.white;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            root.Add(label);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "HmdNotice_Board";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            var renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            board = quad.transform;
            board.SetParent(transform, false);
            quad.SetActive(false);
        }

        static ThemeStyleSheet FindTheme()
        {
            foreach (var other in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                if (other.panelSettings != null && other.panelSettings.themeStyleSheet != null) return other.panelSettings.themeStyleSheet;
            return null;
        }
    }
}
