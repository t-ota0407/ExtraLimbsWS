using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// 開発・確認用のモニタ。TrackerRegistry にあるすべての入力ソースを一覧にし、
    /// 選んだ基準（腰）から見た入力の変位を表示する。シーンには、基準を原点に置き直した各ソースを立方体で描く。
    /// 体ごと向きを変えても、立方体と変位は動かない。Space で Re-center。
    /// </summary>
    public class TrackerMonitor : MonoBehaviour
    {
        [SerializeField] TrackerRegistryHost registryHost;

        [Tooltip("基準（腰）を描く位置（ワールド座標、m）。基準の向きはワールドの向きにそろえて描く")]
        [SerializeField] Vector3 vizOrigin = new Vector3(0f, 0.95f, 0f);

        [Tooltip("立方体の並びの縮尺。HMDの中で目の前に小さく出すときは0.3〜0.5にする")]
        [SerializeField] float vizScale = 1f;

        [SerializeField] float cubeSize = 0.05f;
        [SerializeField] bool showGui = true;
        [Tooltip("一覧をたたんで、状態だけを小さく出す（ほかのパネルと重ならないように）")]
        [SerializeField] bool collapsed;

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
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour is ITrackerProviderStatus status) providers.Add(status);

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
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) recenterRequested = true;

            AutoSelect();
            UpdateDelta();
            UpdateViz();
        }

        // 基準：腰のソース（SteamVR を優先）。入力：基準と同じ空間の、腰以外のソース。
        void AutoSelect()
        {
            if (!registry.TryGet(referenceId, out _))
            {
                var reference = registry.FindBySite(BodySite.Waist, PoseSpace.SteamVR) ?? registry.FindBySite(BodySite.Waist);
                if (reference == null && registry.Sources.Count > 0) reference = registry.Sources[0];
                SetReference(reference?.Id);
            }
            if (!registry.TryGet(inputId, out _) && registry.TryGet(referenceId, out var currentReference))
            {
                foreach (var source in registry.Sources)
                {
                    if (source.Id == referenceId || source.Space != currentReference.Space) continue;
                    SetInput(source.Id);
                    break;
                }
            }
        }

        void SetReference(string id)
        {
            if (referenceId == id) return;
            referenceId = id;
            calibrator.Clear();
            recenterRequested = true;
        }

        void SetInput(string id)
        {
            if (inputId == id) return;
            inputId = id;
            calibrator.Clear();
            recenterRequested = true;
        }

        void UpdateDelta()
        {
            hasDelta = false;
            if (!registry.TryGet(referenceId, out var reference) || !registry.TryGet(inputId, out var input))
            {
                deltaMessage = "基準と入力を選んでください";
                return;
            }
            if (reference.Id == input.Id)
            {
                deltaMessage = "基準と入力に別々のソースを選んでください";
                return;
            }
            if (reference.Space != input.Space)
            {
                deltaMessage = $"空間が違うため変位を計算できません（基準 {reference.Space}、入力 {input.Space}）";
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
                Debug.Log($"[ExtraLimb] Re-center（基準 {reference.Id}、入力 {input.Id}）");
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

            foreach (var source in registry.Sources)
            {
                var viz = GetViz(source);
                Pose pose = default;
                bool visible = referenceValid && source.Space == reference.Space && source.TryGetPose(out pose);
                viz.transform.gameObject.SetActive(visible);
                if (!visible) continue;
                var local = NeutralPoseCalibrator.ToReferenceLocal(referencePose, pose);
                viz.transform.SetPositionAndRotation(vizOrigin + local.position * vizScale, local.rotation);
                viz.transform.localScale = Vector3.one * vizScale * (source.Id == referenceId || source.Id == inputId ? cubeSize * 1.4f : cubeSize);
            }

            bool showNeutral = hasDelta && calibrator.IsCalibrated;
            neutralMarker.gameObject.SetActive(showNeutral);
            deltaLine.enabled = showNeutral;
            if (showNeutral)
            {
                var neutral = vizOrigin + calibrator.NeutralLocalPose.position * vizScale;
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
            float width = collapsed ? 400f : 820f;
            float height = collapsed ? 30f + 22f * (providers.Count + 1) : 420f;
            GUILayout.BeginArea(new Rect(10f, 10f, width, height), GUI.skin.box);
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

            GUILayout.Space(4f);
            foreach (var source in registry.Sources)
            {
                var color = vizById.TryGetValue(source.Id, out var viz) ? viz.color : Color.gray;
                bool tracked = source.TryGetPose(out var pose);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(color)}>■</color> {source.Id}", Rich, GUILayout.Width(190f));
                GUILayout.Label(source.Site.ToString(), GUILayout.Width(95f));
                GUILayout.Label(source.Space.ToString(), GUILayout.Width(60f));
                GUILayout.Label(tracked ? "追跡中" : "追跡なし", GUILayout.Width(55f));
                GUILayout.Label(tracked ? $"位置 {pose.position.ToString("F3")}" : "", GUILayout.Width(190f));
                if (GUILayout.Button(source.Id == referenceId ? "[基準]" : "基準にする", GUILayout.Width(80f))) SetReference(source.Id);
                if (GUILayout.Button(source.Id == inputId ? "[入力]" : "入力にする", GUILayout.Width(80f))) SetInput(source.Id);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
            if (hasDelta)
            {
                var dp = lastDelta.position * 100f;
                float angle = Quaternion.Angle(Quaternion.identity, lastDelta.rotation);
                GUILayout.Label($"<b>基準から見た入力の変位</b>（Re-center時を0とする。x=右、y=上、z=前）", Rich);
                GUILayout.Label($"Δx {dp.x,7:F1} cm　Δy {dp.y,7:F1} cm　Δz {dp.z,7:F1} cm　|Δ| {dp.magnitude,6:F1} cm　回転 {angle,6:F1}°");
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
