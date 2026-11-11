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
        float fps;
        int colorCursor;

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
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
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
            int rows = registry != null ? registry.Sources.Count : 0;
            float width = collapsed ? 400f : 520f;
            float height = collapsed ? 30f + 22f * (providers.Count + 1) : 200f + 22f * providers.Count + 25f * rows;
            GUILayout.BeginArea(new Rect(guiPosition.x, guiPosition.y, width, height), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>入力ソースの一覧</b>　画面更新 {fps:F0} fps", Rich);
            if (GUILayout.Button(collapsed ? "一覧を開く" : "たたむ", GUILayout.Width(90f))) collapsed = !collapsed;
            GUILayout.EndHorizontal();
            foreach (var provider in providers)
                GUILayout.Label($"{provider.ProviderName}：{provider.StatusText}");
            if (collapsed)
            {
                GUILayout.EndArea();
                return;
            }

            if (registry == null)
            {
                GUILayout.Label("TrackerRegistryHost がシーンにありません");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label(bindings != null
                ? "部位のボタンで SteamVR のトラッカーの部位を切り替える（保存されます。使用中の部位は飛ばします）。トラッカーを振ると「動き」が強調されます"
                : "TrackerBindingService がないため、部位を割り当てられません");
            GUILayout.Space(2f);
            foreach (var source in registry.Sources)
            {
                var viz = GetViz(source);
                bool tracked = source.TryGetPose(out _);
                bool room = source is FixedPoseSource;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(room ? Color.gray : viz.color)}>■</color> {source.Id}", Rich, GUILayout.Width(140f));
                if (bindings != null && source is IAssignableSite)
                {
                    if (GUILayout.Button(BodySiteNames.Japanese(source.Site), GUILayout.Width(90f))) bindings.CycleSite(source.Id);
                }
                else
                {
                    GUILayout.Label(room ? "部屋（固定）" : BodySiteNames.Japanese(source.Site), GUILayout.Width(90f));
                }
                GUILayout.Label(source.Space.ToString(), GUILayout.Width(60f));
                GUILayout.Label(tracked ? "追跡中" : "追跡なし", GUILayout.Width(55f));
                float speed = motion.GetSpeed(source.Id) * 100f;
                GUILayout.Label(room ? "" : speed >= movingThresholdCmPerSecond ? $"<color=#ffcc44><b>動き {speed,4:F0}</b></color>" : $"動き {speed,4:F0}", Rich, GUILayout.Width(75f));
                GUILayout.Label(source.Id == inputId ? "<b>入力</b>" : source.Id == referenceId ? "<b>基準</b>" : "", Rich, GUILayout.Width(40f));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
            if (hasDelta)
            {
                var dp = lastDelta.position * 100f;
                float angle = Quaternion.Angle(Quaternion.identity, lastDelta.rotation);
                bool roomReference = SourceSelector.IsRoom(registry, referenceId);
                GUILayout.Label($"<b>基準（{(roomReference ? "部屋" : referenceId)}）から見た入力の変位</b>（Re-center時を0、基準の座標）", Rich);
                GUILayout.Label($"Δx {dp.x,6:F1}　Δy {dp.y,6:F1}　Δz {dp.z,6:F1} cm　|Δ| {dp.magnitude,5:F1} cm　回転 {angle,5:F1}°");
            }
            else
            {
                GUILayout.Label(deltaMessage);
            }
            if (GUILayout.Button("Re-center（今の姿勢を0にする）", GUILayout.Width(260f))) recenterRequested = true;
            GUILayout.EndArea();
        }

        static GUIStyle rich;
        static GUIStyle Rich => rich ??= new GUIStyle(GUI.skin.label) { richText = true };
    }
}
