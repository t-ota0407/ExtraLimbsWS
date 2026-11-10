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
    /// 追加肢1本を、入力（例：右足首）の基準（部屋か腰トラッカー）に対する変位で動かす。設定（LimbRuntimeConfig）はここが持つ。
    /// 自分のアバター（VRIK）が体を解き終えた直後に、変位→制御則→IK の順で計算するので、根元が1フレーム遅れない。
    /// アバターがないシーンでは LateUpdate で計算する。
    /// Attach Point を付け替えている間（LimbMountController の補間中）は、手首を新しい位置の既定位置へ連れていく（制御則の基準位置を毎フレーム置き直す）。
    /// 入力は部位（config.inputSite）からトラッカーを探し、基準は指定がなければ部屋（入力と同じ空間の固定の基準）を使う（SourceSelector）。
    /// 「前を記録」の結果は基準ごとに保存し（TrackerBindingService）、次の再生でも使う。
    /// トラッカーを振ってからパネルのボタンを押すと、そのトラッカーを今の入力の部位に割り当てられる。
    /// 制御は位置制御だけで、入力の動きはマッピングの型（MappingPreset）で手先の3軸に写す（計画書の決定事項#20・#21）。
    /// 入力の部位を変えると、その部位の既定の型とゲインになる。P で同じ部位の別の型に切り替え、パネルで軸ごとに反転できる。
    /// 左下のパネルに入力の6つの値（並進3つ・回転3つ）と Re-center からの範囲を出す（型を決めるため）。
    /// <para>操作：Space＝Re-center（今の姿勢を変位0にする）、F＝前を記録（入力の部位を前へ出した状態で押す）、F1〜＝生やす位置（AttachPointRegistry の順）、
    /// Ctrl+1〜4＝入力の部位（右足首・右膝・右肩・右肘）、P＝マッピングの型。エディタでは Game ビューの「Unity Shortcuts」を切っておく（Ctrl+数字が窓の切替に取られるため）。</para>
    /// </summary>
    [DefaultExecutionOrder(10010)]
    public class LimbSession : MonoBehaviour, ISourceSelection
    {
        [SerializeField] TrackerRegistryHost registryHost;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] SelfAvatarRig avatar;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] ExtraLimbRig limb;
        [SerializeField] LimbRuntimeConfig config = new LimbRuntimeConfig();

        [Header("入力の部位（Ctrl+1〜の順）")]
        [SerializeField] BodySite[] inputSites = { BodySite.RightAnkle, BodySite.RightKnee, BodySite.RightShoulder, BodySite.RightElbow };

        [Header("マッピングの型")]
        [Tooltip("空なら Resources の mappingPresetResourcePath から読む（ファイル名の順）")]
        [SerializeField] MappingPreset[] mappingPresets;
        [SerializeField] string mappingPresetResourcePath = "ExtraLimbMappings";

        [Header("入力の6つの値（左のパネル）")]
        [SerializeField] bool showChannelPanel = true;
        [SerializeField] bool channelPanelCollapsed;
        [SerializeField] Vector2 channelPanelPosition = new Vector2(10f, 150f);

        [Header("トラッカーの割り当て（振ってから押す）")]
        [Tooltip("この速さ（cm/s）を超えたトラッカーを「振った」とみなす")]
        [SerializeField] float movingThresholdCmPerSecond = 15f;
        [Tooltip("振ってからこの秒数の間、割り当てのボタンを出す")]
        [SerializeField] float recentMotionSeconds = 4f;

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
        TrackerBindingService bindings;
        readonly SourceMotionTracker motion = new SourceMotionTracker();
        string assignMessage = "";
        string referenceId, inputId;
        bool recenterRequested = true, forwardRequested, inputValid, wasInputValid, tickedAfterBody;
        bool mountCompletedPending, geometryChanged, loadForwardPending;
        int lastTickFrame = -1;
        string statusMessage = "";
        string forwardMessage = "";
        string heightText = "", shoulderText = "", bodyMessage = "";
        Transform targetMarker;
        readonly float[] channelMin = new float[6], channelMax = new float[6];
        bool hasChannelRange;

        static readonly Key[] AttachPointKeys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5 };
        static readonly Key[] InputKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
        static readonly string[] HandAxisNames = { "右", "上", "前" };

        public LimbRuntimeConfig Config => config;

        /// <summary>使えるマッピングの型（全部位）。</summary>
        public System.Collections.Generic.IReadOnlyList<MappingPreset> MappingPresetList => mappingPresets;

        /// <summary>今のマッピングの型。なければ null。</summary>
        public MappingPreset CurrentMappingPreset => MappingPresets.Find(mappingPresets, config.mappingPresetId);
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
            if (mappingPresets == null || mappingPresets.Length == 0)
            {
                mappingPresets = Resources.LoadAll<MappingPreset>(mappingPresetResourcePath);
                Array.Sort(mappingPresets, (a, b) => string.CompareOrdinal(a.name, b.name));
            }
            if (mappingPresets.Length == 0) Debug.LogWarning($"[ExtraLimb] マッピングの型が見つかりません（Resources/{mappingPresetResourcePath}）。並進そのままで動かします", this);
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
            if (bindings == null) bindings = TrackerBindingService.Find();
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
            // 設定の型がその部位のものでなければ、部位の既定の型にする
            if (MappingPresets.Find(MappingPresets.ForSite(mappingPresets, config.inputSite), config.mappingPresetId) == null)
                SelectMappingPreset(MappingPresets.Default(mappingPresets, config.inputSite));
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

        /// <summary>
        /// 入力の部位を変える（Ctrl+1〜）。トラッカーは部位の割り当てから探す。変えたら Re-center する。
        /// マッピングの型とゲインは、その部位の既定の型のものにする。
        /// </summary>
        public void SelectInputSite(BodySite site)
        {
            if (site == config.inputSite && string.IsNullOrEmpty(config.inputSourceId)) return;
            var next = config.Clone();
            next.inputSite = site;
            next.inputSourceId = "";
            var preset = MappingPresets.Default(mappingPresets, site);
            if (preset != null) SetPreset(next, preset);
            Apply(next);
            Debug.Log($"[ExtraLimb] 追加肢：入力を「{BodySiteNames.Japanese(site)}」にします（型：{(preset != null ? preset.displayName : "なし")}）");
        }

        /// <summary>マッピングの型を選ぶ。型の割り当てとゲインの初期値を設定に入れる（軸ごとの反転は消える）。</summary>
        public void SelectMappingPreset(MappingPreset preset)
        {
            if (preset == null) return;
            var next = config.Clone();
            SetPreset(next, preset);
            Apply(next);
            Debug.Log($"[ExtraLimb] 追加肢：マッピングの型を「{preset.displayName}」（{preset.id}）にします。{preset.mapping}、Gp {preset.gain:F1}");
        }

        /// <summary>同じ入力の部位の、次のマッピングの型にする（P）。</summary>
        public void CycleMappingPreset() => SelectMappingPreset(MappingPresets.Next(mappingPresets, config.inputSite, config.mappingPresetId));

        /// <summary>手先の軸（0=右、1=上、2=前）の向きを反転する。</summary>
        public void ToggleInverted(int axis)
        {
            if (axis < 0 || axis > 2 || !config.axisMapping[axis].IsUsed) return;
            var next = config.Clone();
            next.axisMapping = config.axisMapping.WithInverted(axis);
            Apply(next);
            Debug.Log($"[ExtraLimb] 追加肢：手先の{HandAxisNames[axis]}を{(next.axisMapping.IsInverted(axis) ? "反転" : "元の向き")}にします（{next.axisMapping}）");
        }

        static void SetPreset(LimbRuntimeConfig target, MappingPreset preset)
        {
            target.mappingPresetId = preset.id;
            target.axisMapping = preset.mapping;
            target.positionGain = preset.gain;
        }

        /// <summary>
        /// 最近振ったトラッカーを、今の入力の部位に割り当てる。その部位を使っていたトラッカーは未割り当てに戻る。
        /// 割り当てると入力が変わるので、次の計算で Re-center する。
        /// </summary>
        public bool AssignRecentlyMovedTracker()
        {
            string id = motion.RecentlyMoved(Time.unscaledTime, recentMotionSeconds);
            string site = BodySiteNames.Japanese(config.inputSite);
            if (bindings == null || id == null)
            {
                assignMessage = bindings == null ? "TrackerBindingService がないため割り当てられません" : $"{site}のトラッカーを振ってから押してください";
                return false;
            }
            string previous = bindings.Assign(id, config.inputSite);
            motion.ClearRecent();
            assignMessage = $"{id} を「{site}」にしました" + (previous != null ? $"（{previous} は未割り当てに戻しました）" : "");
            return true;
        }

        /// <summary>基準を順に切り替える：部屋 → 入力と同じ空間の腰（・胸）のトラッカー → 部屋。</summary>
        public void CycleReference()
        {
            if (registry == null || !registry.TryGet(inputId, out var input)) return;
            var options = new System.Collections.Generic.List<string> { "" };
            foreach (var source in registry.Sources)
                if (source.Space == input.Space && source.Id != input.Id && (source.Site == BodySite.Waist || source.Site == BodySite.Chest))
                    options.Add(source.Id);
            int index = options.IndexOf(config.referenceSourceId ?? "");
            var next = config.Clone();
            next.referenceSourceId = options[(index + 1) % options.Count];
            Apply(next);
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
            if (registry != null)
            {
                motion.ThresholdMetersPerSecond = movingThresholdCmPerSecond * 0.01f;
                motion.Update(registry, Time.unscaledDeltaTime, Time.unscaledTime, source => source is IAssignableSite);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || keyboard.altKey.isPressed) return;
            if (keyboard.ctrlKey.isPressed)
            {
                for (int i = 0; i < inputSites.Length && i < InputKeys.Length; i++)
                    if (keyboard[InputKeys[i]].wasPressedThisFrame) SelectInputSite(inputSites[i]);
                return;
            }
            if (keyboard.spaceKey.wasPressedThisFrame) RequestRecenter();
            if (keyboard.fKey.wasPressedThisFrame) RequestRecordForward();
            if (keyboard.pKey.wasPressedThisFrame) CycleMappingPreset();
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
                    if (loadForwardPending) LoadForward();
                }
                else if (!wasInputValid)
                {
                    pipeline.ResetFilter();
                }
                if (forwardRequested) RecordForward(referencePose, inputPose);
                pipeline.Step(referencePose, inputPose, Time.deltaTime);
                if (recentered) hasChannelRange = false;
                UpdateChannelRange(pipeline.LastChannels);
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
                if (bindings != null) bindings.SaveForward(referenceId, pipeline.BodyFrame.Forward);
                Debug.Log($"[ExtraLimb] 追加肢：{forwardMessage}。体の前＝基準の座標で {pipeline.BodyFrame.Forward.ToString("F2")}");
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
            string input = SourceSelector.SelectInput(registry, config.inputSourceId, config.inputSite);
            string reference = SourceSelector.SelectReference(registry, config.referenceSourceId, input);
            if (reference == referenceId && input == inputId) return;
            if (reference != referenceId) loadForwardPending = true;
            referenceId = reference;
            inputId = input;
            recenterRequested = true;
            Debug.Log($"[ExtraLimb] 追加肢：基準 {referenceId ?? "なし"}、入力 {inputId ?? "なし"}");
        }

        // 基準が変わったら、その基準で記録してある前の向きを使う（なければ未記録に戻す）
        void LoadForward()
        {
            loadForwardPending = false;
            pipeline.BodyFrame.ClearForward();
            if (bindings != null && bindings.TryGetForward(referenceId, out var forward) && pipeline.BodyFrame.TrySetForward(forward, 0.01f))
                forwardMessage = "前の向き：保存した値を使っています";
            else
                forwardMessage = "";
        }

        bool TryGetPoses(out Pose referencePose, out Pose inputPose)
        {
            referencePose = inputPose = default;
            if (!registry.TryGet(inputId, out var input))
            {
                statusMessage = $"「{BodySiteNames.Japanese(config.inputSite)}」のトラッカーがありません。そのトラッカーを振って、上のボタンで割り当ててください";
                return false;
            }
            if (!registry.TryGet(referenceId, out var reference))
            {
                statusMessage = $"基準がありません（{input.Space} の部屋が登録されていません）";
                return false;
            }
            if (reference.Space != input.Space)
            {
                statusMessage = $"基準と入力の空間が違います（{reference.Space} と {input.Space}）";
                return false;
            }
            if (!reference.TryGetPose(out referencePose))
            {
                statusMessage = "基準の追跡が切れています。手先は止めています";
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
            if (source is FixedPoseSource) return $"部屋（{source.Space}）";
            return $"{BodySiteNames.Japanese(source.Site)}（{id}・{source.Space}）";
        }

        void OnGUI()
        {
            if (!showGui) return;
            const float width = 420f;
            DrawChannelPanel();
            GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, PanelHeight), GUI.skin.box);
            int attachPointCount = mount != null ? Mathf.Min(mount.Registry.Count, AttachPointKeys.Length) : 0;
            GUILayout.Label("<b>追加肢（Step 3-3）</b>　Space：Re-center　F：前を記録" + (attachPointCount > 0 ? $"　F1〜F{attachPointCount}：生やす位置" : "") + $"　Ctrl+1〜{inputSites.Length}：入力　P：型", Rich);
            if (registry == null || limb == null || !limb.IsReady)
            {
                GUILayout.Label("TrackerRegistryHost か ExtraLimbRig がありません");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"入力：{Describe(inputId)}");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < inputSites.Length && i < InputKeys.Length; i++)
            {
                var site = inputSites[i];
                bool available = SourceSelector.SelectInput(registry, "", site) != null;
                string label = $"{i + 1} {BodySiteNames.Japanese(site)}" + (available ? "" : "×");
                if (site == config.inputSite && string.IsNullOrEmpty(config.inputSourceId)) label = $"[{label}]";
                if (GUILayout.Button(label)) SelectInputSite(site);
            }
            GUILayout.EndHorizontal();
            DrawAssignRow();
            DrawMappingRows();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"基準：{Describe(referenceId)}", GUILayout.Width(300f));
            if (GUILayout.Button("基準を切替")) CycleReference();
            GUILayout.EndHorizontal();

            DrawAttachPointRow();
            DrawBodyDimensionsRow();

            GUILayout.Label($"状態：{statusMessage}（{(tickedAfterBody ? "体を解いた直後に計算" : "LateUpdateで計算")}）");
            GUILayout.Label(pipeline.BodyFrame.HasForward
                ? "前の向き：記録済み"
                : "<color=#ffcc44>前の向き：未記録（基準の軸で代用）。入力の部位を前へ出して F</color>", Rich);
            if (forwardMessage.Length > 0) GUILayout.Label(forwardMessage);

            var u = pipeline.LastDisplacement * 100f;
            GUILayout.Label($"型を通した入力 u：右 {u.x,6:F1}　上 {u.y,6:F1}　前 {u.z,6:F1} cm" +
                            (pipeline.AtWorkspaceBoundary ? "　<color=#ff8866>手先が範囲の端</color>" : ""), Rich);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-center（Space）")) RequestRecenter();
            if (GUILayout.Button("前を記録（F）")) RequestRecordForward();
            GUILayout.EndHorizontal();
            showTargetMarker = GUILayout.Toggle(showTargetMarker, "手首のターゲットを表示（水色の球）");
            GUILayout.EndArea();
        }

        /// <summary>パネルの高さ。仮のボタンのパネルはこの下に置く。</summary>
        public const float PanelHeight = 520f;

        // マッピングの型、手先の各軸の動かし方、軸ごとの反転とゲイン
        void DrawMappingRows()
        {
            var presets = MappingPresets.ForSite(mappingPresets, config.inputSite);
            var preset = MappingPresets.Find(presets, config.mappingPresetId);
            GUILayout.BeginHorizontal();
            string count = presets.Count > 1 && preset != null ? $"（{presets.IndexOf(preset) + 1}/{presets.Count}）" : "";
            GUILayout.Label($"型：{(preset != null ? preset.displayName : "なし")}{count}", GUILayout.Width(300f));
            if (presets.Count > 1 && GUILayout.Button("次の型（P）")) CycleMappingPreset();
            GUILayout.EndHorizontal();
            var mapping = config.axisMapping;
            GUILayout.Label($"手先の右←{mapping.DescribeAxis(0)}　上←{mapping.DescribeAxis(1)}　前←{mapping.DescribeAxis(2)}");
            GUILayout.BeginHorizontal();
            GUILayout.Label("反転", GUILayout.Width(32f));
            for (int axis = 0; axis < 3; axis++)
            {
                bool inverted = mapping.IsInverted(axis);
                GUI.enabled = mapping[axis].IsUsed;
                bool next = GUILayout.Toggle(inverted, HandAxisNames[axis], GUILayout.Width(44f));
                GUI.enabled = true;
                if (next != inverted) ToggleInverted(axis);
            }
            GUILayout.Label($"ゲイン Gp：{config.positionGain:F1}", GUILayout.Width(110f));
            if (GUILayout.Button("−", GUILayout.Width(32f))) SetPositionGain(config.positionGain - 0.5f);
            if (GUILayout.Button("＋", GUILayout.Width(32f))) SetPositionGain(config.positionGain + 0.5f);
            GUILayout.EndHorizontal();
        }

        // 入力の6つの値の表示用の値（並進は cm、回転は度）
        static float DisplayValue(in InputChannels channels, InputChannel channel) =>
            channel.IsRotation() ? channels.Raw(channel) * Mathf.Rad2Deg : channels.Raw(channel) * 100f;

        void UpdateChannelRange(in InputChannels channels)
        {
            for (int i = 0; i < InputChannelInfo.All.Length; i++)
            {
                float value = DisplayValue(channels, InputChannelInfo.All[i]);
                channelMin[i] = hasChannelRange ? Mathf.Min(channelMin[i], value) : value;
                channelMax[i] = hasChannelRange ? Mathf.Max(channelMax[i], value) : value;
            }
            hasChannelRange = true;
        }

        // 左のパネル：入力の6つの値と、Re-center からの範囲（型を決めるため）
        void DrawChannelPanel()
        {
            if (!showChannelPanel || registry == null || limb == null || !limb.IsReady) return;
            const float width = 430f;
            float height = channelPanelCollapsed ? 34f : 270f;
            GUILayout.BeginArea(new Rect(channelPanelPosition.x, channelPanelPosition.y, width, height), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>入力の6つの値</b>（{BodySiteNames.Japanese(config.inputSite)}、体の軸、Re-center時を0）", Rich);
            if (GUILayout.Button(channelPanelCollapsed ? "開く" : "たたむ", GUILayout.Width(60f))) channelPanelCollapsed = !channelPanelCollapsed;
            GUILayout.EndHorizontal();
            if (channelPanelCollapsed)
            {
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(96f));
            GUILayout.Label("今", GUILayout.Width(56f));
            GUILayout.Label("最小", GUILayout.Width(56f));
            GUILayout.Label("最大", GUILayout.Width(56f));
            GUILayout.Label("", GUILayout.Width(26f));
            GUILayout.Label("手先");
            GUILayout.EndHorizontal();
            var channels = pipeline.LastChannels;
            var mapping = config.axisMapping;
            for (int i = 0; i < InputChannelInfo.All.Length; i++)
            {
                var channel = InputChannelInfo.All[i];
                string used = "";
                for (int axis = 0; axis < 3; axis++)
                    if (mapping[axis].IsUsed && mapping[axis].channel == channel)
                        used = $"→ {HandAxisNames[axis]}" + (mapping.IsInverted(axis) ? "（反転）" : "");
                string Format(float v) => used.Length > 0 ? $"<b>{v,6:+0.0;-0.0}</b>" : $"{v,6:+0.0;-0.0}";
                GUILayout.BeginHorizontal();
                GUILayout.Label(used.Length > 0 ? $"<b>{InputChannelInfo.Describe(channel)}</b>" : InputChannelInfo.Describe(channel), Rich, GUILayout.Width(96f));
                GUILayout.Label(Format(DisplayValue(channels, channel)), Rich, GUILayout.Width(56f));
                GUILayout.Label(hasChannelRange ? Format(channelMin[i]) : "", Rich, GUILayout.Width(56f));
                GUILayout.Label(hasChannelRange ? Format(channelMax[i]) : "", Rich, GUILayout.Width(56f));
                GUILayout.Label(channel.IsRotation() ? "°" : "cm", GUILayout.Width(26f));
                GUILayout.Label(used.Length > 0 ? $"<b>{used}</b>" : "", Rich);
                GUILayout.EndHorizontal();
            }
            var preset = CurrentMappingPreset;
            if (preset != null && preset.instruction.Length > 0) GUILayout.Label($"動かし方：{preset.instruction}");
            if (mapping.UsesRotation) GUILayout.Label($"回転は、てこ {mapping.leverLength * 100f:F0} cm で長さに直す（10° で {mapping.leverLength * 10f * Mathf.Deg2Rad * 100f:F1} cm）");
            if (GUILayout.Button("範囲をリセット", GUILayout.Width(120f))) hasChannelRange = false;
            GUILayout.EndArea();
        }

        // 振ったトラッカーを入力の部位に割り当てる行
        void DrawAssignRow()
        {
            if (bindings == null) return;
            string site = BodySiteNames.Japanese(config.inputSite);
            string recent = motion.RecentlyMoved(Time.unscaledTime, recentMotionSeconds);
            bool missing = inputId == null;
            ITrackedPoseSource tracker = null;
            if (recent != null && !registry.TryGet(recent, out tracker)) recent = null;
            if (recent != null && tracker.Site == config.inputSite)
            {
                GUILayout.Label($"振ったトラッカー {recent} は「{site}」です");
            }
            else if (recent != null)
            {
                if (GUILayout.Button($"振ったトラッカー {recent}（今：{BodySiteNames.Japanese(tracker.Site)}）を「{site}」にする")) AssignRecentlyMovedTracker();
            }
            else
            {
                GUILayout.Label(missing
                    ? $"<color=#ffcc44>{site}のトラッカーを振ると、ここに割り当てのボタンが出ます</color>"
                    : $"割り当て直すときは、{site}のトラッカーを振ってください", Rich);
            }
            if (assignMessage.Length > 0) GUILayout.Label(assignMessage);
        }

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
