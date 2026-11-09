using System;
using ExtraLimb.Body;
using ExtraLimb.Core;
using ExtraLimb.Limb;
using ExtraLimb.Tracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 追加肢1本を、入力（例：右足首）の腰に対する変位で動かす。設定（LimbRuntimeConfig）はここが持つ。
    /// 自分のアバター（VRIK）が体を解き終えた直後に、変位→制御則→IK の順で計算するので、根元が1フレーム遅れない。
    /// アバターがないシーンでは LateUpdate で計算する。
    /// <para>操作：Space＝Re-center（今の姿勢を変位0にする）、F＝前を記録（入力の部位を前へ出した状態で押す）。</para>
    /// </summary>
    [DefaultExecutionOrder(10010)]
    public class LimbSession : MonoBehaviour
    {
        [SerializeField] TrackerRegistryHost registryHost;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] SelfAvatarRig avatar;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] ExtraLimbRig limb;
        [SerializeField] LimbRuntimeConfig config = new LimbRuntimeConfig();

        [Header("入力ソースの自動選択（設定にIDがないとき）")]
        [Tooltip("入力として優先する部位。基準（腰）と同じ空間のものから選ぶ")]
        [SerializeField] BodySite preferredInputSite = BodySite.RightAnkle;

        [Header("前を記録")]
        [Tooltip("前を記録するのに必要な、水平の変位（cm）")]
        [SerializeField] float minForwardGestureCm = 5f;

        [Header("表示")]
        [SerializeField] bool showTargetMarker = true;
        [SerializeField] bool showGui = true;

        LimbControlPipeline pipeline;
        TrackerRegistry registry;
        string referenceId, inputId;
        bool recenterRequested = true, forwardRequested, inputValid, wasInputValid, tickedAfterBody;
        int lastTickFrame = -1;
        string statusMessage = "";
        string forwardMessage = "";
        Transform targetMarker;

        public LimbRuntimeConfig Config => config;
        public LimbControlPipeline Pipeline => pipeline;
        public ExtraLimbRig Limb => limb;
        public string ReferenceId => referenceId;
        public string InputId => inputId;

        /// <summary>基準と入力の両方を追跡できているか。</summary>
        public bool IsInputValid => inputValid;

        /// <summary>Re-centerして、基準位置で腕を解いた直後。</summary>
        public event Action Recentered;

        /// <summary>毎フレーム、追加肢を解いた直後。</summary>
        public event Action LimbSolved;

        void Awake()
        {
            pipeline = new LimbControlPipeline(config);
        }

        void OnEnable()
        {
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            registry = registryHost != null ? registryHost.Registry : null;
            if (registry == null) Debug.LogError("[ExtraLimb] TrackerRegistryHost がシーンにないため、追加肢を動かせません", this);
            if (avatar == null) avatar = FindFirstObjectByType<SelfAvatarRig>();
            if (limb == null)
            {
                // 表示している追加肢のうち最初のもの
                foreach (var candidate in FindObjectsByType<ExtraLimbRig>(FindObjectsSortMode.None))
                {
                    if (!candidate.Visible) continue;
                    limb = candidate;
                    break;
                }
            }
            if (limb == null) Debug.LogError("[ExtraLimb] ExtraLimbRig がシーンにないため、追加肢を動かせません", this);
            if (avatar != null) avatar.BodySolved += OnBodySolved;
        }

        void OnDisable()
        {
            if (avatar != null) avatar.BodySolved -= OnBodySolved;
        }

        void OnBodySolved() => Tick(afterBody: true);

        void Start()
        {
            if (limb == null || !limb.IsReady) return;
            pipeline.Workspace = limb.CreateWorkspace();
            pipeline.SetBasePosition(limb.RestHandPosition);
            targetMarker = CreateMarker("ExtraLimb_IKTarget", new Color(0.3f, 0.9f, 1f), 0.03f);
        }

        /// <summary>設定を差し替える（複製して持つ）。制御方式が変わったらターゲットを基準位置に戻す。</summary>
        public void Apply(LimbRuntimeConfig newConfig)
        {
            config = (newConfig ?? new LimbRuntimeConfig()).Clone();
            pipeline.Apply(config);
        }

        /// <summary>次の計算で、今の姿勢を変位0にする。</summary>
        public void RequestRecenter() => recenterRequested = true;

        /// <summary>次の計算で、今の変位の水平成分を「前」として記録する。</summary>
        public void RequestRecordForward() => forwardRequested = true;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || keyboard.ctrlKey.isPressed || keyboard.altKey.isPressed) return;
            if (keyboard.spaceKey.wasPressedThisFrame) RequestRecenter();
            if (keyboard.fKey.wasPressedThisFrame) RequestRecordForward();
        }

        // 体を解いた通知が来なかったフレーム（アバターがないなど）はここで計算する。実行順を VRIK（9998）より後にしてある
        void LateUpdate()
        {
            if (lastTickFrame != Time.frameCount) Tick(afterBody: false);
        }

        void Tick(bool afterBody)
        {
            if (lastTickFrame == Time.frameCount) return;
            lastTickFrame = Time.frameCount;
            tickedAfterBody = afterBody;
            if (registry == null || limb == null || !limb.IsReady) return;

            AutoSelect();
            bool recentered = false;
            inputValid = TryGetPoses(out var referencePose, out var inputPose);
            if (inputValid)
            {
                if (recenterRequested || !pipeline.IsRecentered)
                {
                    pipeline.Recenter(referencePose, inputPose);
                    recenterRequested = false;
                    recentered = true;
                    Debug.Log($"[ExtraLimb] 追加肢：Re-center（基準 {referenceId}、入力 {inputId}）");
                }
                else if (!wasInputValid)
                {
                    pipeline.ResetFilter();
                }
                if (forwardRequested) RecordForward(referencePose, inputPose);
                pipeline.Step(referencePose, inputPose, Time.deltaTime);
            }
            wasInputValid = inputValid;

            // 追跡が切れている間は、最後のターゲットのまま保つ
            var targetWorld = limb.FrameToWorld(pipeline.Target);
            limb.Solve(targetWorld);
            if (targetMarker != null)
            {
                targetMarker.gameObject.SetActive(showTargetMarker);
                targetMarker.position = targetWorld;
            }

            if (recentered) Recentered?.Invoke();
            LimbSolved?.Invoke();
        }

        void RecordForward(Pose referencePose, Pose inputPose)
        {
            forwardRequested = false;
            var horizontal = Vector3.ProjectOnPlane(pipeline.Calibrator.Compute(referencePose, inputPose).position, pipeline.BodyFrame.Up);
            if (pipeline.TryRecordForward(referencePose, inputPose, minForwardGestureCm * 0.01f))
            {
                forwardMessage = $"前を記録しました（水平の変位 {horizontal.magnitude * 100f:F1} cm）";
                Debug.Log($"[ExtraLimb] 追加肢：{forwardMessage}。体の前＝腰トラッカーの座標で {pipeline.BodyFrame.Forward.ToString("F2")}");
            }
            else
            {
                forwardMessage = $"前を記録できません：水平の変位が {horizontal.magnitude * 100f:F1} cm（{minForwardGestureCm:F0} cm 以上、前へ出して押す）";
                Debug.LogWarning($"[ExtraLimb] 追加肢：{forwardMessage}");
            }
        }

        // ---- 入力ソースの選択 ----

        void AutoSelect()
        {
            string reference = Exists(config.referenceSourceId) ? config.referenceSourceId : AutoReference();
            string input = Exists(config.inputSourceId) && config.inputSourceId != reference ? config.inputSourceId : AutoInput(reference);
            if (reference == referenceId && input == inputId) return;
            referenceId = reference;
            inputId = input;
            recenterRequested = true;
            Debug.Log($"[ExtraLimb] 追加肢：基準 {referenceId ?? "なし"}、入力 {inputId ?? "なし"}");
        }

        bool Exists(string id) => !string.IsNullOrEmpty(id) && registry.TryGet(id, out _);

        // 腰（SteamVR を優先）。腰の部位が分からなければ、SteamVR → 仮想トラッカーの順に最初のソース
        string AutoReference()
        {
            var waist = registry.FindBySite(BodySite.Waist, PoseSpace.SteamVR) ?? registry.FindBySite(BodySite.Waist);
            if (waist != null) return waist.Id;
            foreach (var space in new[] { PoseSpace.SteamVR, PoseSpace.Virtual })
                foreach (var source in registry.Sources)
                    if (source.Space == space) return source.Id;
            return null;
        }

        // 基準と同じ空間で、優先する部位 → 部位が分からないもの → それ以外の順
        string AutoInput(string reference)
        {
            if (reference == null || !registry.TryGet(reference, out var referenceSource)) return null;
            ITrackedPoseSource unknown = null, other = null;
            foreach (var source in registry.Sources)
            {
                if (source.Id == reference || source.Space != referenceSource.Space) continue;
                if (source.Site == preferredInputSite) return source.Id;
                if (source.Site == BodySite.Unknown) unknown ??= source;
                else other ??= source;
            }
            return (unknown ?? other)?.Id;
        }

        bool TryGetPoses(out Pose referencePose, out Pose inputPose)
        {
            referencePose = inputPose = default;
            if (!registry.TryGet(referenceId, out var reference) || !registry.TryGet(inputId, out var input))
            {
                statusMessage = "基準（腰）と入力のソースがありません";
                return false;
            }
            if (reference.Space != input.Space)
            {
                statusMessage = $"基準と入力の空間が違います（{reference.Space} と {input.Space}）";
                return false;
            }
            if (!reference.TryGetPose(out referencePose))
            {
                statusMessage = "基準（腰）の追跡が切れています。手先は止めています";
                return false;
            }
            if (!input.TryGetPose(out inputPose))
            {
                statusMessage = "入力の追跡が切れています。手先は止めています";
                return false;
            }
            statusMessage = "動作中";
            return true;
        }

        /// <summary>基準か入力を、登録されているソースの中で次のものに切り替える。</summary>
        void CycleSource(bool isReference)
        {
            var sources = registry.Sources;
            if (sources.Count == 0) return;
            string current = isReference ? referenceId : inputId;
            string other = isReference ? inputId : referenceId;
            int start = 0;
            for (int i = 0; i < sources.Count; i++)
                if (sources[i].Id == current) start = i + 1;
            for (int k = 0; k < sources.Count; k++)
            {
                var candidate = sources[(start + k) % sources.Count];
                if (candidate.Id == other || candidate.Id == current) continue;
                // 選んだ組を設定に書いて、自動選択で変わらないようにする
                config.referenceSourceId = (isReference ? candidate.Id : referenceId) ?? "";
                config.inputSourceId = (isReference ? inputId : candidate.Id) ?? "";
                return;
            }
        }

        void SwapSources()
        {
            if (referenceId == null || inputId == null) return;
            config.referenceSourceId = inputId;
            config.inputSourceId = referenceId;
        }

        // ---- 表示 ----

        Transform CreateMarker(string markerName, Color color, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = markerName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
            go.GetComponent<Renderer>().material = material;
            return go.transform;
        }

        string Describe(string id)
        {
            if (id == null || registry == null || !registry.TryGet(id, out var source)) return "なし";
            return $"{id}（{source.Site}・{source.Space}）";
        }

        void OnGUI()
        {
            if (!showGui) return;
            const float width = 420f;
            GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, 330f), GUI.skin.box);
            GUILayout.Label("<b>追加肢（Step 2-3：位置制御）</b>　Space：Re-center　F：前を記録", Rich);
            if (registry == null || limb == null || !limb.IsReady)
            {
                GUILayout.Label("TrackerRegistryHost か ExtraLimbRig がありません");
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"基準（腰）：{Describe(referenceId)}", GUILayout.Width(300f));
            if (GUILayout.Button("切替")) CycleSource(isReference: true);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"入力：{Describe(inputId)}", GUILayout.Width(300f));
            if (GUILayout.Button("切替")) CycleSource(isReference: false);
            if (GUILayout.Button("入替")) SwapSources();
            GUILayout.EndHorizontal();

            GUILayout.Label($"状態：{statusMessage}（{(tickedAfterBody ? "体を解いた直後に計算" : "LateUpdateで計算")}）");
            GUILayout.Label(pipeline.BodyFrame.HasForward
                ? "前の向き：記録済み"
                : "<color=#ffcc44>前の向き：未記録（腰トラッカーの軸で代用）。足を前へ出して F</color>", Rich);
            if (forwardMessage.Length > 0) GUILayout.Label(forwardMessage);

            var u = pipeline.LastDisplacement * 100f;
            var target = pipeline.Target * 100f;
            GUILayout.Label($"入力の変位 u（体の向き）：右 {u.x,6:F1}　上 {u.y,6:F1}　前 {u.z,6:F1} cm　|u| {u.magnitude,5:F1}");
            GUILayout.Label($"手首のターゲット（根元から）：右 {target.x,6:F1}　上 {target.y,6:F1}　前 {target.z,6:F1} cm" +
                            (pipeline.AtWorkspaceBoundary ? "　<color=#ff8866>範囲の端</color>" : ""), Rich);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"位置ゲイン Gp：{config.positionGain:F1}", GUILayout.Width(150f));
            if (GUILayout.Button("−", GUILayout.Width(36f))) SetPositionGain(config.positionGain - 0.5f);
            if (GUILayout.Button("＋", GUILayout.Width(36f))) SetPositionGain(config.positionGain + 0.5f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-center（Space）")) RequestRecenter();
            if (GUILayout.Button("前を記録（F）")) RequestRecordForward();
            GUILayout.EndHorizontal();
            showTargetMarker = GUILayout.Toggle(showTargetMarker, "手首のターゲットを表示（水色の球）");
            GUILayout.EndArea();
        }

        void SetPositionGain(float gain)
        {
            var next = config.Clone();
            next.positionGain = Mathf.Clamp(gain, 0.5f, 5f);
            Apply(next);
        }

        static GUIStyle rich;
        static GUIStyle Rich => rich ??= new GUIStyle(GUI.skin.label) { richText = true };
    }
}
