using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// 開発・確認用のモニタ。TrackerRegistry にあるすべての入力ソースを一覧にし、基準から見た入力の変位を表示する。
    /// 基準と入力は、シーンに追加肢（ISourceSelection）があればそれと同じものを使う。なければ SourceSelector の規則（入力は右足首、基準は部屋）。
    /// SteamVR のトラッカーは、行の部位のボタンで部位を順に切り替えられる（TrackerBindingService に保存。ほかのトラッカーが使っている部位は飛ばす）。
    /// 「動き」の欄は速さで、トラッカーを振ると強調されるので、どの行がどの実物かを確かめられる。
    /// シーンには各ソースを立方体で描く：基準が部屋のときは入力の Re-center 時の位置を、腰のときは腰を vizOrigin に置く。
    /// Space で Re-center。
    /// <para>
    /// 実験者パネルがあるときは、機器の状態はパネルの左上の表示に出すので（StatusShownElsewhere）、ここは一覧を開いたときだけ描く。
    /// 見た目は実験者パネルにそろえる（同じ字・同じ色）。
    /// </para>
    /// </summary>
    public class TrackerMonitor : MonoBehaviour
    {
        [SerializeField] TrackerRegistryHost registryHost;

        [Tooltip("基準を描く位置（ワールド座標、m）。基準が部屋のときは、入力の Re-center 時の位置をここに描く")]
        [SerializeField] Vector3 vizOrigin = new Vector3(0f, 0.95f, 0f);

        [Tooltip("立方体の並びの縮尺。HMDの中で目の前に小さく出すときは0.3〜0.5にする")]
        [SerializeField] float vizScale = 1f;

        [SerializeField] float cubeSize = 0.05f;
        [SerializeField] bool showGui = true;
        [Tooltip("一覧の左上の位置（画面の左上から、px）。上には実験者パネルの大きな表示がある")]
        [SerializeField] Vector2 guiPosition = new Vector2(10f, 120f);
        [Tooltip("一覧をたたんで、状態だけを小さく出す（ほかのパネルと重ならないように）")]
        [SerializeField] bool collapsed;

        [Tooltip("追加肢がシーンにないときに、入力として探す部位")]
        [SerializeField] BodySite defaultInputSite = BodySite.RightAnkle;

        [Tooltip("この速さ（cm/s）を超えたソースの「動き」を強調する")]
        [SerializeField] float movingThresholdCmPerSecond = 15f;

        static readonly Color[] Palette =
        {
            new Color(1f, 0.55f, 0.1f), new Color(0.25f, 0.7f, 1f), new Color(0.45f, 0.95f, 0.45f),
            new Color(1f, 0.35f, 0.65f), new Color(1f, 0.95f, 0.35f), new Color(0.7f, 0.5f, 1f),
            new Color(0.4f, 1f, 0.9f), new Color(1f, 0.7f, 0.6f),
        };

        class Viz
        {
            public Transform transform;
            public Color color;
        }

        TrackerRegistry registry;
        TrackerBindingService bindings;
        ISourceSelection selection;
        readonly SourceMotionTracker motion = new SourceMotionTracker();
        readonly Dictionary<string, Viz> vizById = new Dictionary<string, Viz>();
        readonly List<ITrackerProviderStatus> providers = new List<ITrackerProviderStatus>();
        readonly NeutralPoseCalibrator calibrator = new NeutralPoseCalibrator();
        string referenceId, inputId;
        bool recenterRequested = true;
        PoseDelta lastDelta = PoseDelta.Zero;
        bool hasDelta;
        string deltaMessage = "";
        Transform neutralMarker;
        LineRenderer deltaLine;
        int colorCursor;

        /// <summary>トラッカーの一覧を開いているか。</summary>
        public bool ListOpen
        {
            get => !collapsed;
            set => collapsed = !value;
        }

        /// <summary>機器の状態をほかの表示（実験者パネル）が出しているか。true なら、一覧を開いたときだけ描く。</summary>
        public bool StatusShownElsewhere { get; set; }

        /// <summary>一覧の幅の上限（px）。右に実験者パネルがあるときに、重ならないように狭める。</summary>
        public float GuiMaxWidth { get; set; } = float.PositiveInfinity;

        /// <summary>一覧の左上の位置（画面の左上から、px）。</summary>
        public Vector2 GuiPosition
        {
            get => guiPosition;
            set => guiPosition = value;
        }

        void Start()
        {
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            registry = registryHost != null ? registryHost.Registry : null;
            bindings = TrackerBindingService.Find();
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is ITrackerProviderStatus status) providers.Add(status);
                if (selection == null && behaviour is ISourceSelection found) selection = found;
            }

            neutralMarker = CreatePrimitive(PrimitiveType.Sphere, "Monitor_Neutral", cubeSize * 0.5f, Color.white).transform;
            neutralMarker.gameObject.SetActive(false);
            deltaLine = new GameObject("Monitor_Delta").AddComponent<LineRenderer>();
            deltaLine.transform.SetParent(transform, false);
            deltaLine.positionCount = 2;
            deltaLine.startWidth = deltaLine.endWidth = 0.006f;
            deltaLine.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            SetColor(deltaLine.material, Color.white);
            deltaLine.enabled = false;
        }

        void Update()
        {
            if (registry == null) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && !keyboard.ctrlKey.isPressed) recenterRequested = true;

            Select();
            motion.Update(registry, Time.unscaledDeltaTime, Time.unscaledTime);
            UpdateDelta();
            UpdateViz();
        }

        void Select()
        {
            string input, reference;
            if (selection != null)
            {
                input = selection.InputId;
                reference = selection.ReferenceId;
            }
            else
            {
                input = SourceSelector.SelectInput(registry, "", defaultInputSite);
                reference = SourceSelector.SelectReference(registry, "", input);
            }
            if (input == inputId && reference == referenceId) return;
            inputId = input;
            referenceId = reference;
            calibrator.Clear();
            recenterRequested = true;
        }

        void UpdateDelta()
        {
            hasDelta = false;
            if (!registry.TryGet(inputId, out var input))
            {
                deltaMessage = selection != null
                    ? "入力がありません（追加肢のパネルで、入力の部位のトラッカーを割り当てる）"
                    : $"入力がありません（{BodySiteNames.Japanese(defaultInputSite)}を割り当てる）";
                return;
            }
            if (!registry.TryGet(referenceId, out var reference))
            {
                deltaMessage = $"基準がありません（{input.Space} の部屋が登録されていません）";
                return;
            }
            if (!reference.TryGetPose(out var referencePose) || !input.TryGetPose(out var inputPose))
            {
                deltaMessage = "基準か入力の追跡が切れています";
                return;
            }
            if (recenterRequested)
            {
                calibrator.Recenter(referencePose, inputPose);
                recenterRequested = false;
                Debug.Log($"[ExtraLimb] モニタ：Re-center（基準 {reference.Id}、入力 {input.Id}）");
            }
            lastDelta = calibrator.Compute(referencePose, inputPose);
            hasDelta = true;
            deltaMessage = "";
        }

        void UpdateViz()
        {
            bool hasReference = registry.TryGet(referenceId, out var reference);
            Pose referencePose = default;
            bool referenceValid = hasReference && reference.TryGetPose(out referencePose);
            bool roomReference = hasReference && reference is FixedPoseSource;

            // 部屋が基準のときは、入力の Re-center 時の位置を vizOrigin に描く（部屋の原点は遠いことがあるため）
            var anchor = Vector3.zero;
            if (roomReference && registry.TryGet(inputId, out var input) && input.TryGetPose(out var inputPose))
                anchor = calibrator.IsCalibrated ? calibrator.NeutralLocalPose.position : NeutralPoseCalibrator.ToReferenceLocal(referencePose, inputPose).position;

            foreach (var source in registry.Sources)
            {
                var viz = GetViz(source);
                Pose pose = default;
                bool visible = referenceValid && !(source is FixedPoseSource) && source.Space == reference.Space && source.TryGetPose(out pose);
                viz.transform.gameObject.SetActive(visible);
                if (!visible) continue;
                var local = NeutralPoseCalibrator.ToReferenceLocal(referencePose, pose);
                viz.transform.SetPositionAndRotation(vizOrigin + (local.position - anchor) * vizScale, local.rotation);
                viz.transform.localScale = Vector3.one * vizScale * (source.Id == referenceId || source.Id == inputId ? cubeSize * 1.4f : cubeSize);
            }

            bool showNeutral = hasDelta && calibrator.IsCalibrated;
            neutralMarker.gameObject.SetActive(showNeutral);
            deltaLine.enabled = showNeutral;
            if (showNeutral)
            {
                var neutral = vizOrigin + (calibrator.NeutralLocalPose.position - anchor) * vizScale;
                neutralMarker.position = neutral;
                neutralMarker.localScale = Vector3.one * cubeSize * 0.5f * vizScale;
                deltaLine.SetPosition(0, neutral);
                deltaLine.SetPosition(1, neutral + lastDelta.position * vizScale);
                deltaLine.startWidth = deltaLine.endWidth = 0.006f * Mathf.Max(vizScale, 0.5f);
            }
        }

        Viz GetViz(ITrackedPoseSource source)
        {
            if (vizById.TryGetValue(source.Id, out var viz)) return viz;
            var color = Palette[colorCursor++ % Palette.Length];
            var go = CreatePrimitive(PrimitiveType.Cube, "Monitor_" + source.Id, cubeSize, color);
            viz = new Viz { transform = go.transform, color = color };
            vizById.Add(source.Id, viz);
            return viz;
        }

        GameObject CreatePrimitive(PrimitiveType type, string name, float size, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;
            SetColor(go.GetComponent<Renderer>().material, color);
            return go;
        }

        static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
        }

        void OnGUI()
        {
            if (!showGui) return;
            if (StatusShownElsewhere && collapsed) return;
            var ui = MonitorStyles.Get();
            // 状態は、知らせることがある機器だけを出す（StatusText が空なら出さない）
            int shown = 0;
            foreach (var provider in providers)
                if (provider.NeedsAttention) shown++;
            float width = Mathf.Max(320f, Mathf.Min(collapsed ? 400f : 540f, GuiMaxWidth));
            // 枠は中身の高さに合わせる（文が折り返しても切れないように）
            GUILayout.BeginArea(new Rect(guiPosition.x, guiPosition.y, width, Mathf.Max(100f, Screen.height - guiPosition.y)));
            GUILayout.BeginVertical(ui.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(StatusShownElsewhere ? "トラッカーの一覧" : "機器の状態", ui.title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(collapsed ? "トラッカーの一覧" : "閉じる", ui.button)) collapsed = !collapsed;
            GUILayout.EndHorizontal();
            if (!StatusShownElsewhere)
            {
                foreach (var provider in providers)
                {
                    string status = provider.StatusText;
                    if (string.IsNullOrEmpty(status)) continue;
                    GUILayout.Label(provider.NeedsAttention ? $"<color={MonitorStyles.DangerHex}>●</color>  {provider.ProviderName}：{status}" : $"{provider.ProviderName}：{status}",
                        provider.NeedsAttention ? ui.label : ui.muted);
                }
                if (shown == 0) GUILayout.Label("問題はありません", ui.muted);
            }
            if (collapsed || registry == null)
            {
                if (!collapsed) GUILayout.Label("入力ソースの一覧がありません（シーンに TrackerRegistryHost がありません）", ui.muted);
                GUILayout.EndVertical();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label(bindings != null
                ? "部位のボタンを押すと、トラッカーの部位が順に切り替わります（保存されます）。振ったトラッカーは「動き」が黄色になります"
                : "このシーンでは部位を割り当てられません", ui.muted);
            GUILayout.Space(6f);
            foreach (var source in registry.Sources)
            {
                var viz = GetViz(source);
                bool tracked = source.TryGetPose(out _);
                bool room = source is FixedPoseSource;
                GUILayout.BeginHorizontal(GUILayout.Height(34f));
                int colon = source.Id.IndexOf(':');
                string shortId = colon >= 0 ? source.Id.Substring(colon + 1) : source.Id;
                GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(room ? Color.gray : viz.color)}>■</color>  {shortId}", ui.label, GUILayout.Width(150f));
                if (bindings != null && source is IAssignableSite)
                {
                    if (GUILayout.Button(BodySiteNames.Japanese(source.Site), ui.button, GUILayout.Width(96f))) bindings.CycleSite(source.Id);
                }
                else
                {
                    GUILayout.Label(room ? "部屋（固定）" : BodySiteNames.Japanese(source.Site), ui.label, GUILayout.Width(96f));
                }
                GUILayout.Space(8f);
                GUILayout.Label(tracked ? "追跡中" : $"<color={MonitorStyles.DangerHex}>追跡なし</color>", tracked ? ui.muted : ui.label, GUILayout.Width(70f));
                float speed = motion.GetSpeed(source.Id) * 100f;
                GUILayout.Label(room ? "" : speed >= movingThresholdCmPerSecond ? $"<color={MonitorStyles.CautionHex}><b>動き {speed,4:F0}</b></color>" : $"動き {speed,4:F0}",
                    ui.muted, GUILayout.Width(80f));
                GUILayout.Label(source.Id == inputId ? "<b>入力</b>" : source.Id == referenceId ? "<b>基準</b>" : "", ui.label, GUILayout.Width(44f));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10f);
            if (hasDelta)
            {
                var dp = lastDelta.position * 100f;
                float angle = Quaternion.Angle(Quaternion.identity, lastDelta.rotation);
                bool roomReference = SourceSelector.IsRoom(registry, referenceId);
                GUILayout.Label($"<b>入力の変位</b>　基準：{(roomReference ? "部屋" : referenceId)}。下のボタンを押した時点を 0", ui.label);
                GUILayout.Label($"Δx {dp.x,6:F1}　Δy {dp.y,6:F1}　Δz {dp.z,6:F1} cm　|Δ| {dp.magnitude,5:F1} cm　回転 {angle,5:F1}°", ui.muted);
            }
            else
            {
                GUILayout.Label(deltaMessage, ui.muted);
            }
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("変位の表示を 0 にする", ui.button)) recenterRequested = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
