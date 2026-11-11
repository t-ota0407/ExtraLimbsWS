using System;
using System.Collections.Generic;
using ExtraLimb.Body;
using ExtraLimb.Core;
using ExtraLimb.Limb;
using ExtraLimb.Tracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 追加肢（右、必要なら左も）を、入力（例：右足首）の基準（部屋か腰トラッカー）に対する変位で動かす。1本ぶんの計算は LimbChannel が持つ。
    /// 自分のアバター（VRIK）が体を解き終えた直後に、変位→制御則→IK の順で計算するので、根元が1フレーム遅れない。
    /// アバターがないシーンでは LateUpdate で計算する。
    /// <list type="bullet">
    /// <item>2本目（左）：フェーズA・Bは右の1本、C・Dで2本（計画書の決定事項#14）。左は右の4台のうち右とは別の部位で動かし、左トリガーで掴む。生やす位置は左右対称（F1〜F3 で両方が変わる）。</item>
    /// <item>パネルで「設定する追加肢」を選ぶ。入力の部位・型・反転・ゲイン・基準・前を記録・割り当ては、選んだ追加肢に効く。Re-center は両方に効く。</item>
    /// <item>入力は部位（config.inputSite）からトラッカーを探し、基準は指定がなければ部屋（SourceSelector）。「前を記録」の結果は基準ごとに保存する（TrackerBindingService）。</item>
    /// <item>制御は位置制御だけで、入力の動きはマッピングの型（MappingPreset）で手先の3軸に写す（決定事項#20・#21）。</item>
    /// <item>把持は GraspRig（右トリガーで右の追加肢、左トリガーで左の追加肢、グリップで本来の手、G キーは予備）。シーンになければ起動時に追加する。</item>
    /// <item>設定の変更はすべて Apply(channel, config) を通る（Step 4-1）。パネル・ホットキー・ほかのスクリプト・起動時のどこから変えても、
    /// 変わった項目ごとに EventRaised（config）を出し、設定の版（Revision）を進める。どこから変えたか（origin）も一緒に出す。
    /// Re-center・前を記録・割り当て・2本目の表示などのできごとも EventRaised で出す。記録は ExperimentRecorder が受け持つ。</item>
    /// </list>
    /// <para>操作：Space＝Re-center（両方）、F＝前を記録、F1〜＝生やす位置（両方）、Ctrl+1〜4＝入力の部位、P＝マッピングの型、
    /// T＝2本目を出す・隠す、←／→＝設定する追加肢（左／右）。
    /// エディタでは Game ビューの「Unity Shortcuts」を切っておく（Ctrl+数字が窓の切替に取られるため）。</para>
    /// </summary>
    [DefaultExecutionOrder(10010)]
    public class LimbSession : MonoBehaviour, ISourceSelection
    {
        [SerializeField] TrackerRegistryHost registryHost;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] SelfAvatarRig avatar;
        [Tooltip("右の追加肢。空ならシーンから探す")]
        [SerializeField] ExtraLimbRig limb;
        [SerializeField] LimbRuntimeConfig config = new LimbRuntimeConfig();

        [Header("2本目（左の追加肢）")]
        [Tooltip("左の追加肢。空ならシーンから探す")]
        [SerializeField] ExtraLimbRig leftLimb;
        [Tooltip("左の追加肢も動かすか（フェーズC・D）。パネルで切り替えられる")]
        [SerializeField] bool twoLimbs;
        [SerializeField] LimbRuntimeConfig leftConfig = new LimbRuntimeConfig
        {
            inputSite = BodySite.RightKnee,
            grasp = new GraspTriggerConfig { hand = ControllerHand.Left },
        };

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

        [Header("把持")]
        [Tooltip("GraspRig がこの GameObject になければ、起動時に追加する")]
        [SerializeField] bool createGraspRig = true;

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

        static readonly Color[] MarkerColors = { new Color(0.3f, 0.9f, 1f), new Color(1f, 0.45f, 0.85f) };

        TrackerRegistry registry;
        BodyFrameProvider body;
        TrackerBindingService bindings;
        GraspRig grasp;
        readonly List<LimbChannel> channels = new List<LimbChannel>();
        LimbChannel left;
        int selected;
        readonly SourceMotionTracker motion = new SourceMotionTracker();
        string assignMessage = "";
        bool tickedAfterBody;
        int lastTickFrame = -1;
        string heightText = "", shoulderText = "", bodyMessage = "";

        // 今の変更がどこから来たか（ホットキーとパネルの処理の間だけ書き換える）
        string origin = ChangeOrigins.Api;
        int revision;

        static readonly Key[] AttachPointKeys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5 };
        static readonly Key[] InputKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
        static readonly string[] HandAxisNames = { "右", "上", "前" };

        /// <summary>動かしている追加肢（右が先頭）。</summary>
        public IReadOnlyList<LimbChannel> Channels => channels;

        /// <summary>右の追加肢（仮のボタンなどはこれを使う）。</summary>
        public LimbChannel Primary => channels.Count > 0 ? channels[0] : null;

        /// <summary>パネルとホットキーで設定する追加肢。</summary>
        public LimbChannel Selected => channels.Count > 0 ? channels[Mathf.Clamp(selected, 0, channels.Count - 1)] : null;

        /// <summary>左の追加肢も動かしているか。</summary>
        public bool TwoLimbs => left != null && left.Active;

        /// <summary>右の追加肢の設定。</summary>
        public LimbRuntimeConfig Config => Primary != null ? Primary.Config : config;
        public LimbControlPipeline Pipeline => Primary?.Pipeline;
        public ExtraLimbRig Limb => Primary != null ? Primary.Limb : limb;
        public LimbMountController Mount => Primary?.Mount;
        public bool IsInputValid => Primary != null && Primary.IsInputValid;

        // モニタは、パネルで選んでいる追加肢と同じ入力・基準を表示する
        string ISourceSelection.InputId => Selected?.InputId;
        string ISourceSelection.ReferenceId => Selected?.ReferenceId;

        /// <summary>把持の入力と手。</summary>
        public GraspRig Grasp => grasp;

        internal TrackerBindingService Bindings => bindings;
        internal float MinForwardGestureMeters => minForwardGestureCm * 0.01f;

        /// <summary>使えるマッピングの型（全部位）。</summary>
        public IReadOnlyList<MappingPreset> MappingPresetList => mappingPresets;

        /// <summary>選んでいる追加肢のマッピングの型。なければ null。</summary>
        public MappingPreset CurrentMappingPreset => Selected != null ? MappingPresets.Find(mappingPresets, Selected.Config.mappingPresetId) : null;

        /// <summary>右の追加肢を Re-center して、基準位置で腕を解いた直後。</summary>
        public event Action Recentered;

        /// <summary>毎フレーム、右の追加肢を解いた直後。</summary>
        public event Action LimbSolved;

        /// <summary>右の追加肢の付け替えが終わったか、体の寸法が変わった直後（腕を解いた後）。</summary>
        public event Action MountChanged;

        /// <summary>設定の変更とできごと（イベントログの1行）。設定の変更は、変わった項目ごとに1つ出す。</summary>
        public event Action<SessionEvent> EventRaised;

        /// <summary>設定の版が進んだ直後。引数はきっかけ（イベントの種類）。記録は、ここで設定の全体を書く。</summary>
        public event Action<string> Revised;

        /// <summary>設定の版。設定・2本目の表示・身長と肩幅が変わるたびに1つ増える（フレームCSVの rev 列）。</summary>
        public int Revision => revision;

        /// <summary>パネル・ホットキーの処理の中なら panel / key:…、ほかのスクリプトから呼ばれたなら api。</summary>
        public string CurrentOrigin => origin;

        /// <summary>
        /// using の間だけ、変更の出どころを origin にする。ほかの部品のパネルのボタンから LimbSession を操作するときに使う
        /// （例：using (session.WithOrigin(ChangeOrigins.Panel)) session.RequestRecenter();）。
        /// </summary>
        public OriginScope WithOrigin(string newOrigin) => new OriginScope(this, newOrigin);

        /// <summary>WithOrigin の戻り値。Dispose で元の出どころに戻す。</summary>
        public readonly struct OriginScope : IDisposable
        {
            readonly LimbSession session;
            readonly string previous;

            internal OriginScope(LimbSession session, string origin)
            {
                this.session = session;
                previous = session != null ? session.origin : null;
                if (session != null) session.origin = origin ?? ChangeOrigins.Api;
            }

            public void Dispose()
            {
                if (session != null) session.origin = previous ?? ChangeOrigins.Api;
            }
        }

        void Awake()
        {
            if (mappingPresets == null || mappingPresets.Length == 0)
            {
                mappingPresets = Resources.LoadAll<MappingPreset>(mappingPresetResourcePath);
                Array.Sort(mappingPresets, (a, b) => string.CompareOrdinal(a.name, b.name));
            }
            if (mappingPresets.Length == 0) Debug.LogWarning($"[ExtraLimb] マッピングの型が見つかりません（Resources/{mappingPresetResourcePath}）。並進そのままで動かします", this);
            grasp = GetComponent<GraspRig>();
            if (grasp == null && createGraspRig) grasp = gameObject.AddComponent<GraspRig>();
        }

        void OnEnable()
        {
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            registry = registryHost != null ? registryHost.Registry : null;
            if (registry == null) Debug.LogError("[ExtraLimb] TrackerRegistryHost がシーンにないため、追加肢を動かせません", this);
            if (avatar == null) avatar = FindFirstObjectByType<SelfAvatarRig>();
            if (limb == null || leftLimb == null)
            {
                foreach (var candidate in FindObjectsByType<ExtraLimbRig>(FindObjectsSortMode.None))
                {
                    if (candidate.Side == LimbSide.Right && limb == null) limb = candidate;
                    else if (candidate.Side == LimbSide.Left && leftLimb == null) leftLimb = candidate;
                }
            }
            if (limb == null) Debug.LogError("[ExtraLimb] 右の ExtraLimbRig がシーンにないため、追加肢を動かせません", this);
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
            origin = ChangeOrigins.System;
            try
            {
                StartChannels();
            }
            finally
            {
                origin = ChangeOrigins.Api;
            }
        }

        void StartChannels()
        {
            if (limb == null || !limb.IsReady) return;
            var right = AddChannel(limb, config);
            right.Recentered += () => Recentered?.Invoke();
            right.LimbSolved += () => LimbSolved?.Invoke();
            right.MountChanged += () => MountChanged?.Invoke();
            if (leftLimb != null && leftLimb.IsReady)
            {
                // 生やす位置は左右対称にそろえる
                var leftStart = leftConfig.Clone();
                leftStart.attachPointId = right.Config.attachPointId;
                left = AddChannel(leftLimb, leftStart);
                left.SetActive(false);
                if (twoLimbs) SetTwoLimbs(true);
            }

            body = right.Mount != null ? right.Mount.Registry.Body : FindFirstObjectByType<BodyFrameProvider>();
            if (body != null)
            {
                body.CalibrationChanged += OnCalibrationChanged;
                if (body.IsReady)
                {
                    heightText = (body.Calibration.Height * 100f).ToString("F0");
                    shoulderText = (body.Calibration.ShoulderWidth * 100f).ToString("F0");
                }
            }
        }

        LimbChannel AddChannel(ExtraLimbRig rig, LimbRuntimeConfig channelConfig)
        {
            var channel = new LimbChannel(this, rig, channelConfig);
            channels.Add(channel);
            channel.Start(MarkerColors[(channels.Count - 1) % MarkerColors.Length]);
            // 設定の型がその部位のものでなければ、部位の既定の型にする
            if (MappingPresets.Find(MappingPresets.ForSite(mappingPresets, channel.Config.inputSite), channel.Config.mappingPresetId) == null)
                SelectMappingPreset(channel, MappingPresets.Default(mappingPresets, channel.Config.inputSite));
            return channel;
        }

        void OnDestroy()
        {
            foreach (var channel in channels) channel.Dispose();
            if (body != null) body.CalibrationChanged -= OnCalibrationChanged;
        }

        void OnCalibrationChanged()
        {
            foreach (var channel in channels) channel.OnGeometryChanged();
        }

        // ---- 追加肢の選択と2本目 ----

        /// <summary>パネルとホットキーで設定する追加肢を選ぶ（0=右、1=左）。</summary>
        public void Select(int index)
        {
            if (index < 0 || index >= channels.Count || !channels[index].Active) return;
            var previous = Selected;
            selected = index;
            assignMessage = "";
            if (previous != Selected)
                Raise(SessionEventTypes.Select, Selected.Subject, "selected", previous?.Subject, Selected.Subject, $"{Selected.Label}の追加肢を設定します");
        }

        /// <summary>左の追加肢を動かすか（フェーズC・D）。止めると隠し、選択を右に戻す。</summary>
        public void SetTwoLimbs(bool on)
        {
            if (left == null || TwoLimbs == on) return;
            left.SetActive(on);
            Raise(SessionEventTypes.TwoLimbs, left.Subject, "active", Bool(!on), Bool(on), on ? "左の追加肢を出しました" : "左の追加肢を隠しました");
            if (on)
            {
                // 生やす位置を右にそろえる
                var next = left.Config.Clone();
                next.attachPointId = Primary.Config.attachPointId;
                Apply(left, next);
            }
            else if (Selected == left)
            {
                Select(0);
            }
            Revise(SessionEventTypes.TwoLimbs);
            Debug.Log($"[ExtraLimb] 左の追加肢を{(on ? "動かします" : "隠します")}");
        }

        // ---- 設定 ----

        /// <summary>選んでいる追加肢の設定を差し替える。</summary>
        public void Apply(LimbRuntimeConfig newConfig) => Apply(Selected, newConfig);

        /// <summary>
        /// 追加肢1本の設定を差し替える（複製して持つ）。設定の変更はすべてここを通る（計画書の設計方針、Step 4-1）。
        /// 変わった項目ごとにイベント（config）を出し、何か変わっていれば設定の版を1つ進める。
        /// Attach Point が見つからないなどで設定どおりにならなかった項目は、実際の値を記録する。
        /// </summary>
        public void Apply(LimbChannel channel, LimbRuntimeConfig newConfig)
        {
            if (channel == null || newConfig == null) return;
            var before = channel.Config.Clone();
            channel.Apply(newConfig);
            var changes = ConfigFields.Diff(before, channel.Config);
            if (changes.Count == 0) return;
            foreach (var change in changes)
                Raise(SessionEventTypes.Config, channel.Subject, change.path, change.oldValue, change.newValue,
                    $"{channel.Label}の追加肢：{change.path} を {change.oldValue} から {change.newValue} に");
            Revise(SessionEventTypes.Config);
        }

        /// <summary>F1〜 に対応する Attach Point（0 始まり、AttachPointRegistry の順）へ付け替える。左右とも同じ位置にする。</summary>
        public void SelectAttachPoint(int index)
        {
            var mount = Primary?.Mount;
            if (mount == null) return;
            var definition = mount.Registry.Get(index);
            if (definition == null) return;
            foreach (var channel in channels)
            {
                if (channel.Config.attachPointId == definition.id) continue;
                var next = channel.Config.Clone();
                next.attachPointId = definition.id;
                Apply(channel, next);
            }
            Debug.Log($"[ExtraLimb] 追加肢：生やす位置を「{definition.displayName}」（{definition.id}）に変えます");
        }

        /// <summary>
        /// 選んでいる追加肢の入力の部位を変える（Ctrl+1〜）。トラッカーは部位の割り当てから探す。変えたら Re-center する。
        /// マッピングの型とゲインは、その部位の既定の型のものにする。
        /// </summary>
        public void SelectInputSite(BodySite site)
        {
            var channel = Selected;
            if (channel == null || (site == channel.Config.inputSite && string.IsNullOrEmpty(channel.Config.inputSourceId))) return;
            var next = channel.Config.Clone();
            next.inputSite = site;
            next.inputSourceId = "";
            var preset = MappingPresets.Default(mappingPresets, site);
            if (preset != null) SetPreset(next, preset);
            Apply(channel, next);
            Debug.Log($"[ExtraLimb] {channel.Label}の追加肢：入力を「{BodySiteNames.Japanese(site)}」にします（型：{(preset != null ? preset.displayName : "なし")}）");
        }

        /// <summary>選んでいる追加肢のマッピングの型を選ぶ。型の割り当てとゲインの初期値を設定に入れる（軸ごとの反転は消える）。</summary>
        public void SelectMappingPreset(MappingPreset preset) => SelectMappingPreset(Selected, preset);

        void SelectMappingPreset(LimbChannel channel, MappingPreset preset)
        {
            if (channel == null || preset == null) return;
            var next = channel.Config.Clone();
            SetPreset(next, preset);
            Apply(channel, next);
            Debug.Log($"[ExtraLimb] {channel.Label}の追加肢：マッピングの型を「{preset.displayName}」（{preset.id}）にします。{preset.mapping}、Gp {preset.gain:F1}");
        }

        /// <summary>選んでいる追加肢を、同じ入力の部位の次のマッピングの型にする（P）。</summary>
        public void CycleMappingPreset()
        {
            var channel = Selected;
            if (channel != null) SelectMappingPreset(channel, MappingPresets.Next(mappingPresets, channel.Config.inputSite, channel.Config.mappingPresetId));
        }

        /// <summary>選んでいる追加肢の、手先の軸（0=右、1=上、2=前）の向きを反転する。</summary>
        public void ToggleInverted(int axis)
        {
            var channel = Selected;
            if (channel == null || axis < 0 || axis > 2 || !channel.Config.axisMapping[axis].IsUsed) return;
            var next = channel.Config.Clone();
            next.axisMapping = channel.Config.axisMapping.WithInverted(axis);
            Apply(channel, next);
            Debug.Log($"[ExtraLimb] {channel.Label}の追加肢：手先の{HandAxisNames[axis]}を{(next.axisMapping.IsInverted(axis) ? "反転" : "元の向き")}にします（{next.axisMapping}）");
        }

        static void SetPreset(LimbRuntimeConfig target, MappingPreset preset)
        {
            target.mappingPresetId = preset.id;
            target.axisMapping = preset.mapping;
            target.positionGain = preset.gain;
        }

        /// <summary>選んでいる追加肢の位置ゲイン G_p を変える（0.5〜5）。</summary>
        public void SetPositionGain(float gain)
        {
            var channel = Selected;
            if (channel == null) return;
            var next = channel.Config.Clone();
            next.positionGain = Mathf.Clamp(gain, 0.5f, 5f);
            Apply(channel, next);
        }

        /// <summary>
        /// 最近振ったトラッカーを、選んでいる追加肢の入力の部位に割り当てる。その部位を使っていたトラッカーは未割り当てに戻る。
        /// 割り当てると入力が変わるので、次の計算で Re-center する。
        /// </summary>
        public bool AssignRecentlyMovedTracker()
        {
            var channel = Selected;
            if (channel == null) return false;
            string id = motion.RecentlyMoved(Time.unscaledTime, recentMotionSeconds);
            string site = BodySiteNames.Japanese(channel.Config.inputSite);
            if (bindings == null || id == null)
            {
                assignMessage = bindings == null ? "TrackerBindingService がないため割り当てられません" : $"{site}のトラッカーを振ってから押してください";
                return false;
            }
            string previous = bindings.Assign(id, channel.Config.inputSite);
            motion.ClearRecent();
            assignMessage = $"{id} を「{site}」にしました" + (previous != null ? $"（{previous} は未割り当てに戻しました）" : "");
            Raise(SessionEventTypes.TrackerAssign, channel.Subject, channel.Config.inputSite.ToString(), previous, id, assignMessage);
            return true;
        }

        /// <summary>選んでいる追加肢の基準を順に切り替える：部屋 → 入力と同じ空間の腰（・胸）のトラッカー → 部屋。</summary>
        public void CycleReference()
        {
            var channel = Selected;
            if (channel == null || registry == null || !registry.TryGet(channel.InputId, out var input)) return;
            var options = new List<string> { "" };
            foreach (var source in registry.Sources)
                if (source.Space == input.Space && source.Id != input.Id && (source.Site == BodySite.Waist || source.Site == BodySite.Chest))
                    options.Add(source.Id);
            int index = options.IndexOf(channel.Config.referenceSourceId ?? "");
            var next = channel.Config.Clone();
            next.referenceSourceId = options[(index + 1) % options.Count];
            Apply(channel, next);
        }

        /// <summary>参加者の身長・肩幅（cm）を入れて、アバターと Attach Point を拡縮する。</summary>
        public bool ApplyBodyDimensions(float heightCm, float shoulderWidthCm)
        {
            if (body == null) return false;
            float oldHeight = body.Calibration.Height * 100f, oldShoulder = body.Calibration.ShoulderWidth * 100f;
            bool ok = body.ApplyBodyDimensions(heightCm * 0.01f, shoulderWidthCm * 0.01f);
            bodyMessage = ok
                ? $"反映しました（アバターの拡縮 {body.Calibration.AvatarScale:F2}）"
                : $"範囲外です（身長 {BodyCalibration.MinHeight * 100f:F0}〜{BodyCalibration.MaxHeight * 100f:F0} cm、肩幅 {BodyCalibration.MinShoulderWidth * 100f:F0}〜{BodyCalibration.MaxShoulderWidth * 100f:F0} cm）";
            if (ok) RaiseBodyChange(oldHeight, oldShoulder);
            return ok;
        }

        /// <summary>身長・肩幅をモデルの大きさに戻す。</summary>
        public void ResetBodyDimensions()
        {
            if (body == null) return;
            float oldHeight = body.Calibration.Height * 100f, oldShoulder = body.Calibration.ShoulderWidth * 100f;
            body.ResetBodyDimensions();
            heightText = (body.Calibration.Height * 100f).ToString("F0");
            shoulderText = (body.Calibration.ShoulderWidth * 100f).ToString("F0");
            bodyMessage = "モデルの大きさに戻しました";
            RaiseBodyChange(oldHeight, oldShoulder);
        }

        void RaiseBodyChange(float oldHeightCm, float oldShoulderCm)
        {
            float height = body.Calibration.Height * 100f, shoulder = body.Calibration.ShoulderWidth * 100f;
            bool changed = false;
            if (!Mathf.Approximately(oldHeightCm, height))
            {
                Raise(SessionEventTypes.Body, "session", "height_cm", Number(oldHeightCm), Number(height), $"身長を {height:F1} cm に（アバターの拡縮 {body.Calibration.AvatarScale:F3}）");
                changed = true;
            }
            if (!Mathf.Approximately(oldShoulderCm, shoulder))
            {
                Raise(SessionEventTypes.Body, "session", "shoulder_width_cm", Number(oldShoulderCm), Number(shoulder), $"肩幅を {shoulder:F1} cm に");
                changed = true;
            }
            if (changed) Revise(SessionEventTypes.Body);
        }

        /// <summary>次の計算で、動かしているすべての追加肢の今の姿勢を変位0にする。</summary>
        public void RequestRecenter()
        {
            foreach (var channel in channels) channel.RequestRecenter();
            Raise(SessionEventTypes.RecenterRequest, TwoLimbs ? "both" : Primary?.Subject, message: "Re-center を頼みました");
        }

        /// <summary>次の計算で、選んでいる追加肢の入力の変位の水平成分を「前」として記録する。</summary>
        public void RequestRecordForward()
        {
            var channel = Selected;
            if (channel == null) return;
            channel.RequestRecordForward();
            Raise(SessionEventTypes.ForwardRequest, channel.Subject, message: "前の記録を頼みました");
        }

        // 前を記録したら保存し、同じ基準を使うほかの追加肢にも同じ前の向きを使う
        internal void OnForwardRecorded(LimbChannel source)
        {
            if (bindings != null) bindings.SaveForward(source.ReferenceId, source.Pipeline.BodyFrame.Forward);
            foreach (var channel in channels)
                if (channel != source && channel.ReferenceId == source.ReferenceId) channel.LoadForward();
        }

        // ---- イベント ----

        /// <summary>イベントを出す。origin を省くと、今の変更の出どころ（panel / key:… / api / system）。</summary>
        internal void Raise(string type, string subject, string key = "", string oldValue = "", string newValue = "", string message = "", string originOverride = null)
        {
            EventRaised?.Invoke(new SessionEvent(type, subject, originOverride ?? origin, key, oldValue, newValue, message));
        }

        void Revise(string reason)
        {
            revision++;
            Revised?.Invoke(reason);
        }

        static string Bool(bool value) => value ? "true" : "false";
        static string Number(float value) => ConfigFields.FormatValue((float)Math.Round(value, 2));

        /// <summary>今の設定の全体（記録用）。</summary>
        public SessionSnapshot CreateSnapshot(string reason = "")
        {
            var snapshot = new SessionSnapshot
            {
                revision = revision,
                reason = reason ?? "",
                twoLimbs = TwoLimbs,
                selected = Selected?.Subject ?? "",
                limbs = new LimbSnapshot[channels.Count],
            };
            if (body != null && body.IsReady)
            {
                snapshot.heightCm = body.Calibration.Height * 100f;
                snapshot.shoulderWidthCm = body.Calibration.ShoulderWidth * 100f;
                snapshot.avatarScale = body.Calibration.AvatarScale;
            }
            for (int i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                snapshot.limbs[i] = new LimbSnapshot
                {
                    limb = channel.Subject,
                    active = channel.Active,
                    inputId = channel.InputId ?? "",
                    referenceId = channel.ReferenceId ?? "",
                    config = channel.Config.Clone(),
                };
            }
            return snapshot;
        }

        // ---- 毎フレーム ----

        void Update()
        {
            if (registry != null)
            {
                motion.ThresholdMetersPerSecond = movingThresholdCmPerSecond * 0.01f;
                motion.Update(registry, Time.unscaledDeltaTime, Time.unscaledTime, source => source is IAssignableSite);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || keyboard.altKey.isPressed) return;
            try
            {
                HandleHotkeys(keyboard);
            }
            finally
            {
                origin = ChangeOrigins.Api;
            }
        }

        // 押されたキーなら、そのキーを変更の出どころにする
        bool Pressed(Keyboard keyboard, Key key, string label)
        {
            if (!keyboard[key].wasPressedThisFrame) return false;
            origin = ChangeOrigins.Key(label);
            return true;
        }

        void HandleHotkeys(Keyboard keyboard)
        {
            if (keyboard.ctrlKey.isPressed)
            {
                for (int i = 0; i < inputSites.Length && i < InputKeys.Length; i++)
                    if (Pressed(keyboard, InputKeys[i], $"Ctrl+{i + 1}")) SelectInputSite(inputSites[i]);
                return;
            }
            if (Pressed(keyboard, Key.Space, "Space")) RequestRecenter();
            if (Pressed(keyboard, Key.F, "F")) RequestRecordForward();
            if (Pressed(keyboard, Key.P, "P")) CycleMappingPreset();
            if (Pressed(keyboard, Key.T, "T")) SetTwoLimbs(!TwoLimbs);
            if (Pressed(keyboard, Key.RightArrow, "Right")) Select(0);
            if (Pressed(keyboard, Key.LeftArrow, "Left") && left != null) Select(channels.IndexOf(left));
            for (int i = 0; i < AttachPointKeys.Length; i++)
                if (Pressed(keyboard, AttachPointKeys[i], $"F{i + 1}")) SelectAttachPoint(i);
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
            if (registry == null) return;
            foreach (var channel in channels) channel.Tick(registry, showTargetMarker);
        }

        // ---- 表示 ----

        internal Transform CreateMarker(string markerName, Color color, float size)
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

        /// <summary>パネルの高さ。仮のボタンのパネルはこの下に置く。</summary>
        public const float PanelHeight = 540f;

        void OnGUI()
        {
            if (!showGui) return;
            // パネルのボタンからの変更は panel として記録する
            origin = ChangeOrigins.Panel;
            try
            {
                DrawPanels();
            }
            finally
            {
                origin = ChangeOrigins.Api;
            }
        }

        void DrawPanels()
        {
            const float width = 420f;
            DrawChannelPanel();
            GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, PanelHeight), GUI.skin.box);
            var channel = Selected;
            int attachPointCount = channel?.Mount != null ? Mathf.Min(channel.Mount.Registry.Count, AttachPointKeys.Length) : 0;
            GUILayout.Label("<b>追加肢（Step 4-1）</b>　Space：Re-center　F：前を記録" + (attachPointCount > 0 ? $"　F1〜F{attachPointCount}：生やす位置" : "") + $"　Ctrl+1〜{inputSites.Length}：入力　P：型　G：掴む（予備）" + (left != null ? "　T：2本目　←→：設定する追加肢" : ""), Rich);
            if (registry == null || channel == null)
            {
                GUILayout.Label("TrackerRegistryHost か ExtraLimbRig がありません");
                GUILayout.EndArea();
                return;
            }

            DrawLimbSelectRow();
            var config = channel.Config;
            GUILayout.Label($"入力：{Describe(channel.InputId)}");
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
            DrawAssignRow(channel);
            DrawMappingRows(channel);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"基準：{Describe(channel.ReferenceId)}", GUILayout.Width(300f));
            if (GUILayout.Button("基準を切替")) CycleReference();
            GUILayout.EndHorizontal();

            DrawAttachPointRow(channel);
            DrawBodyDimensionsRow();

            GUILayout.Label($"状態：{channel.StatusMessage}（{(tickedAfterBody ? "体を解いた直後に計算" : "LateUpdateで計算")}）");
            GUILayout.Label(channel.Pipeline.BodyFrame.HasForward
                ? "前の向き：記録済み"
                : "<color=#ffcc44>前の向き：未記録（基準の軸で代用）。入力の部位を前へ出して F</color>", Rich);
            if (channel.ForwardMessage.Length > 0) GUILayout.Label(channel.ForwardMessage);

            var u = channel.Pipeline.LastDisplacement * 100f;
            GUILayout.Label($"型を通した入力 u：右 {u.x,6:F1}　上 {u.y,6:F1}　前 {u.z,6:F1} cm" +
                            (channel.Pipeline.AtWorkspaceBoundary ? "　<color=#ff8866>手先が範囲の端</color>" : ""), Rich);
            DrawGraspRow();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Re-center（Space）")) RequestRecenter();
            if (GUILayout.Button("前を記録（F）")) RequestRecordForward();
            GUILayout.EndHorizontal();
            showTargetMarker = GUILayout.Toggle(showTargetMarker, "手首のターゲットを表示（右は水色、左は桃色の球）");
            GUILayout.EndArea();
        }

        // 設定する追加肢の選択と、2本目の表示
        void DrawLimbSelectRow()
        {
            if (left == null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label("設定する追加肢（←→）", GUILayout.Width(130f));
            for (int i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                GUI.enabled = channel.Active;
                string label = $"{channel.Label}の追加肢";
                if (channel == Selected) label = $"[{label}]";
                if (GUILayout.Button(label)) Select(i);
                GUI.enabled = true;
            }
            if (GUILayout.Button(TwoLimbs ? "2本目を隠す（T）" : "2本目を出す（T）")) SetTwoLimbs(!TwoLimbs);
            GUILayout.EndHorizontal();
        }

        // 把持：トリガーの押し込み量と、追加肢・本来の手が掴んでいる物
        void DrawGraspRow()
        {
            if (grasp == null) return;
            var parts = new List<string>();
            foreach (var channel in channels)
            {
                if (!channel.Active) continue;
                var grasper = grasp.GrasperFor(channel);
                if (grasper == null) continue;
                string side = channel.Config.grasp.hand == ControllerHand.Left ? "左" : "右";
                string pressed = grasper.Pressed ? "<b>（掴む）</b>" : "";
                string held = grasper.Held != null ? grasper.Held.name : "なし";
                parts.Add($"{channel.Label}：{side}トリガー {grasp.TriggerValue(channel):F2}{pressed} {held}");
            }
            if (grasp.LeftHand != null && grasp.LeftHand.Held != null) parts.Add($"左手：{grasp.LeftHand.Held.name}");
            if (grasp.RightHand != null && grasp.RightHand.Held != null) parts.Add($"右手：{grasp.RightHand.Held.name}");
            GUILayout.Label("把持　" + string.Join("　", parts) + (grasp.KeyPressed ? "　G" : ""), Rich);
        }

        // マッピングの型、手先の各軸の動かし方、軸ごとの反転とゲイン
        void DrawMappingRows(LimbChannel channel)
        {
            var config = channel.Config;
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

        // 左のパネル：選んでいる追加肢の入力の6つの値と、Re-center からの範囲（型を決めるため）
        void DrawChannelPanel()
        {
            var channel = Selected;
            if (!showChannelPanel || registry == null || channel == null) return;
            const float width = 430f;
            float height = channelPanelCollapsed ? 34f : 270f;
            GUILayout.BeginArea(new Rect(channelPanelPosition.x, channelPanelPosition.y, width, height), GUI.skin.box);
            GUILayout.BeginHorizontal();
            string limbLabel = channels.Count > 1 ? $"{channel.Label}の追加肢、" : "";
            GUILayout.Label($"<b>入力の6つの値</b>（{limbLabel}{BodySiteNames.Japanese(channel.Config.inputSite)}、体の軸、Re-center時を0）", Rich);
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
            var values = channel.Pipeline.LastChannels;
            var mapping = channel.Config.axisMapping;
            bool hasRange = channel.HasChannelRange;
            for (int i = 0; i < InputChannelInfo.All.Length; i++)
            {
                var inputChannel = InputChannelInfo.All[i];
                string used = "";
                for (int axis = 0; axis < 3; axis++)
                    if (mapping[axis].IsUsed && mapping[axis].channel == inputChannel)
                        used = $"→ {HandAxisNames[axis]}" + (mapping.IsInverted(axis) ? "（反転）" : "");
                string Format(float v) => used.Length > 0 ? $"<b>{v,6:+0.0;-0.0}</b>" : $"{v,6:+0.0;-0.0}";
                GUILayout.BeginHorizontal();
                GUILayout.Label(used.Length > 0 ? $"<b>{InputChannelInfo.Describe(inputChannel)}</b>" : InputChannelInfo.Describe(inputChannel), Rich, GUILayout.Width(96f));
                GUILayout.Label(Format(LimbChannel.DisplayValue(values, inputChannel)), Rich, GUILayout.Width(56f));
                GUILayout.Label(hasRange ? Format(channel.ChannelMin(i)) : "", Rich, GUILayout.Width(56f));
                GUILayout.Label(hasRange ? Format(channel.ChannelMax(i)) : "", Rich, GUILayout.Width(56f));
                GUILayout.Label(inputChannel.IsRotation() ? "°" : "cm", GUILayout.Width(26f));
                GUILayout.Label(used.Length > 0 ? $"<b>{used}</b>" : "", Rich);
                GUILayout.EndHorizontal();
            }
            var preset = CurrentMappingPreset;
            if (preset != null && preset.instruction.Length > 0) GUILayout.Label($"動かし方：{preset.instruction}");
            if (mapping.UsesRotation) GUILayout.Label($"回転は、てこ {mapping.leverLength * 100f:F0} cm で長さに直す（10° で {mapping.leverLength * 10f * Mathf.Deg2Rad * 100f:F1} cm）");
            if (GUILayout.Button("範囲をリセット", GUILayout.Width(120f))) channel.ResetChannelRange();
            GUILayout.EndArea();
        }

        // 振ったトラッカーを、選んでいる追加肢の入力の部位に割り当てる行
        void DrawAssignRow(LimbChannel channel)
        {
            if (bindings == null) return;
            string site = BodySiteNames.Japanese(channel.Config.inputSite);
            string recent = motion.RecentlyMoved(Time.unscaledTime, recentMotionSeconds);
            bool missing = channel.InputId == null;
            ITrackedPoseSource tracker = null;
            if (recent != null && !registry.TryGet(recent, out tracker)) recent = null;
            if (recent != null && tracker.Site == channel.Config.inputSite)
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

        void DrawAttachPointRow(LimbChannel channel)
        {
            var mount = channel.Mount;
            if (mount == null)
            {
                GUILayout.Label("生やす位置：固定（LimbMountController がありません）");
                return;
            }
            var current = mount.Current;
            GUILayout.Label($"生やす位置：{(current != null ? current.displayName : "—")}" + (TwoLimbs ? "（左右とも）" : "") + (mount.IsTransitioning ? "（付け替え中）" : ""));
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
            if (GUILayout.Button("モデルの大きさ")) ResetBodyDimensions();
            GUILayout.EndHorizontal();
            if (bodyMessage.Length > 0) GUILayout.Label(bodyMessage);
        }

        static GUIStyle rich;
        static GUIStyle Rich => rich ??= new GUIStyle(GUI.skin.label) { richText = true };
    }
}
