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
    /// Attach Point を付け替えている間（LimbMountController の補間中）は、手首を新しい位置の既定位置へ連れていく（制御則の基準位置を毎フレーム置き直す）。
    /// <para>操作：Space＝Re-center（今の姿勢を変位0にする）、F＝前を記録（入力の部位を前へ出した状態で押す）、F1〜＝生やす位置（AttachPointRegistry の順）。</para>
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
        LimbMountController mount;
        BodyFrameProvider body;
        string referenceId, inputId;
        bool recenterRequested = true, forwardRequested, inputValid, wasInputValid, tickedAfterBody;
        bool mountCompletedPending, geometryChanged;
        int lastTickFrame = -1;
        string statusMessage = "";
        string forwardMessage = "";
        string heightText = "", shoulderText = "", bodyMessage = "";
        Transform targetMarker;

        static readonly Key[] AttachPointKeys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5 };

        public LimbRuntimeConfig Config => config;
        public LimbControlPipeline Pipeline => pipeline;
        public ExtraLimbRig Limb => limb;

        /// <summary>付け根を動かす部品（なければ Attach Point は固定）。</summary>
        public LimbMountController Mount => mount;
        public string ReferenceId => referenceId;
        public string InputId => inputId;

        /// <summary>基準と入力の両方を追跡できているか。</summary>
        public bool IsInputValid => inputValid;

        /// <summary>Re-centerして、基準位置で腕を解いた直後。</summary>
        public event Action Recentered;

        /// <summary>毎フレーム、追加肢を解いた直後。</summary>
        public event Action LimbSolved;

        /// <summary>Attach Point の付け替えが終わったか、体の寸法が変わって、基準位置や届く範囲が変わった直後（腕を解いた後）。</summary>
        public event Action MountChanged;

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
            mount = limb.Mount;
            body = mount != null ? mount.Registry.Body : FindFirstObjectByType<BodyFrameProvider>();
            if (mount != null)
            {
                mount.MountCompleted += OnMountCompleted;
                // 設定の Attach Point に付ける（なければ今の位置を設定に書く）
                if (mount.Registry.TryGet(config.attachPointId, out _)) mount.MountTo(config.attachPointId, immediate: true);
                config.attachPointId = mount.CurrentId;
                mount.UpdateMount();
            }
            if (body != null)
            {
                body.CalibrationChanged += OnCalibrationChanged;
                if (body.IsReady)
                {
                    heightText = (body.Calibration.Height * 100f).ToString("F0");
                    shoulderText = (body.Calibration.ShoulderWidth * 100f).ToString("F0");
                }
            }
            pipeline.Workspace = limb.CreateWorkspace();
            pipeline.SetBasePosition(limb.RestHandPosition);
            targetMarker = CreateMarker("ExtraLimb_IKTarget", new Color(0.3f, 0.9f, 1f), 0.03f);
        }

        void OnDestroy()
        {
            if (mount != null) mount.MountCompleted -= OnMountCompleted;
            if (body != null) body.CalibrationChanged -= OnCalibrationChanged;
        }

        void OnMountCompleted(AttachPointDefinition definition)
        {
            mountCompletedPending = true;
            config.attachPointId = definition.id;
        }

        void OnCalibrationChanged() => geometryChanged = true;

        /// <summary>
        /// 設定を差し替える（複製して持つ）。制御方式が変わったらターゲットを基準位置に戻す。
        /// Attach Point が変わったら付け替えを始める。
        /// </summary>
        public void Apply(LimbRuntimeConfig newConfig)
        {
            string previousAttachPoint = config.attachPointId;
            config = (newConfig ?? new LimbRuntimeConfig()).Clone();
            pipeline.Apply(config);
            if (mount != null && config.attachPointId != previousAttachPoint && !mount.MountTo(config.attachPointId))
                config.attachPointId = mount.CurrentId;
        }

        /// <summary>F1〜 に対応する Attach Point（0 始まり、AttachPointRegistry の順）へ付け替える。</summary>
        public void SelectAttachPoint(int index)
        {
            if (mount == null) return;
            var definition = mount.Registry.Get(index);
            if (definition == null || definition.id == mount.CurrentId) return;
            var next = config.Clone();
            next.attachPointId = definition.id;
            Apply(next);
            Debug.Log($"[ExtraLimb] 追加肢：生やす位置を「{definition.displayName}」（{definition.id}）に変えます");
        }

        /// <summary>参加者の身長・肩幅（cm）を入れて、アバターと Attach Point を拡縮する。</summary>
        public bool ApplyBodyDimensions(float heightCm, float shoulderWidthCm)
        {
            if (body == null) return false;
            bool ok = body.ApplyBodyDimensions(heightCm * 0.01f, shoulderWidthCm * 0.01f);
            bodyMessage = ok
                ? $"反映しました（アバターの拡縮 {body.Calibration.AvatarScale:F2}）"
                : $"範囲外です（身長 {BodyCalibration.MinHeight * 100f:F0}〜{BodyCalibration.MaxHeight * 100f:F0} cm、肩幅 {BodyCalibration.MinShoulderWidth * 100f:F0}〜{BodyCalibration.MaxShoulderWidth * 100f:F0} cm）";
            return ok;
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
            for (int i = 0; i < AttachPointKeys.Length; i++)
                if (keyboard[AttachPointKeys[i]].wasPressedThisFrame) SelectAttachPoint(i);
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

            // 付け根を置いてから（補間中ならその途中の位置）、基準位置と届く範囲をそれに合わせる
            if (mount != null) mount.UpdateMount();
            if (geometryChanged) pipeline.Workspace = limb.CreateWorkspace();
            bool notifyMount = mountCompletedPending || geometryChanged;
            if (notifyMount || (mount != null && mount.IsTransitioning)) pipeline.SetBasePosition(limb.RestHandPosition);
            mountCompletedPending = geometryChanged = false;

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
            if (notifyMount) MountChanged?.Invoke();
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
            GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, PanelHeight), GUI.skin.box);
            int attachPointCount = mount != null ? Mathf.Min(mount.Registry.Count, AttachPointKeys.Length) : 0;
            GUILayout.Label("<b>追加肢（Step 3-1）</b>　Space：Re-center　F：前を記録" + (attachPointCount > 0 ? $"　F1〜F{attachPointCount}：生やす位置" : ""), Rich);
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

            DrawAttachPointRow();
            DrawBodyDimensionsRow();

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

        /// <summary>パネルの高さ。仮のボタンのパネルはこの下に置く。</summary>
        public const float PanelHeight = 430f;

        void DrawAttachPointRow()
        {
            if (mount == null)
            {
                GUILayout.Label("生やす位置：固定（LimbMountController がありません）");
                return;
            }
            var current = mount.Current;
            GUILayout.Label($"生やす位置：{(current != null ? current.displayName : "—")}" + (mount.IsTransitioning ? "（付け替え中）" : ""));
            GUILayout.BeginHorizontal();
            var points = mount.Registry.Points;
            for (int i = 0; i < points.Count && i < AttachPointKeys.Length; i++)
            {
                if (points[i] == null) continue;
                string label = $"F{i + 1} {points[i].displayName}";
                if (points[i] == current) label = $"[{label}]";
                if (GUILayout.Button(label)) SelectAttachPoint(i);
            }
            GUILayout.EndHorizontal();
        }

        void DrawBodyDimensionsRow()
        {
            if (body == null || !body.IsReady) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label("身長", GUILayout.Width(32f));
            heightText = GUILayout.TextField(heightText, GUILayout.Width(44f));
            GUILayout.Label("cm　肩幅", GUILayout.Width(60f));
            shoulderText = GUILayout.TextField(shoulderText, GUILayout.Width(44f));
            GUILayout.Label("cm", GUILayout.Width(24f));
            if (GUILayout.Button("反映"))
            {
                if (float.TryParse(heightText, out float height) && float.TryParse(shoulderText, out float shoulder)) ApplyBodyDimensions(height, shoulder);
                else bodyMessage = "数字で入れてください";
            }
            if (GUILayout.Button("モデルの大きさ"))
            {
                body.ResetBodyDimensions();
                heightText = (body.Calibration.Height * 100f).ToString("F0");
                shoulderText = (body.Calibration.ShoulderWidth * 100f).ToString("F0");
                bodyMessage = "モデルの大きさに戻しました";
            }
            GUILayout.EndHorizontal();
            if (bodyMessage.Length > 0) GUILayout.Label(bodyMessage);
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
