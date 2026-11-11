using System;
using System.Collections.Generic;
using ExtraLimb.Body;
using ExtraLimb.Core;
using ExtraLimb.Limb;
using ExtraLimb.Tracking;
using UnityEngine;

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
    /// <para>操作は、実験者パネル（ExtraLimb.UI の ExperimenterPanel）とホットキー（HotkeyMap）から、このクラスの公開のメソッドを呼ぶ（Step 4-2）。
    /// 呼ぶ側は WithOrigin で出どころ（panel / key:…）を付ける。</para>
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
        [Tooltip("手首のIKターゲットの目印（右は水色、左は桃色の球）を出すか")]
        [SerializeField] bool showTargetMarker = true;

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
        string bodyMessage = "";

        // 今の変更がどこから来たか（ホットキーとパネルの処理の間だけ書き換える）
        string origin = ChangeOrigins.Api;
        int revision;

        static readonly string[] HandAxisNames = { "右", "上", "前" };

        /// <summary>動かしている追加肢（右が先頭）。</summary>
        public IReadOnlyList<LimbChannel> Channels => channels;

        /// <summary>右の追加肢（仮のボタンなどはこれを使う）。</summary>
        public LimbChannel Primary => channels.Count > 0 ? channels[0] : null;

        /// <summary>パネルとホットキーで設定する追加肢。</summary>
        public LimbChannel Selected => channels.Count > 0 ? channels[Mathf.Clamp(selected, 0, channels.Count - 1)] : null;

        /// <summary>左の追加肢も動かしているか。</summary>
        public bool TwoLimbs => left != null && left.Active;

        /// <summary>左の追加肢があるか（2本目を出せるか）。</summary>
        public bool HasLeftLimb => left != null;

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

        /// <summary>トラッカーの割り当てと「前の向き」の保存先（なければ null）。</summary>
        public TrackerBindingService Bindings => bindings;

        /// <summary>入力ソースの一覧（なければ null）。</summary>
        public TrackerRegistry Registry => registry;

        /// <summary>参加者の体の寸法を扱う部品（なければ null）。</summary>
        public BodyFrameProvider Body => body;

        /// <summary>入力の部位の候補（パネルのボタンとホットキー Ctrl+1〜 の順）。</summary>
        public IReadOnlyList<BodySite> InputSites => inputSites;

        /// <summary>最近振ったトラッカーのID（割り当て用。なければ null）。</summary>
        public string RecentlyMovedTracker => registry != null ? motion.RecentlyMoved(Time.unscaledTime, recentMotionSeconds) : null;

        /// <summary>割り当ての結果のメッセージ。</summary>
        public string AssignMessage => assignMessage;

        /// <summary>身長・肩幅の反映の結果のメッセージ。</summary>
        public string BodyMessage => bodyMessage;

        /// <summary>直近のフレームで、体を解いた直後に計算したか（false なら LateUpdate で計算）。</summary>
        public bool TickedAfterBody => tickedAfterBody;

        /// <summary>手首のIKターゲットの目印を出すか。</summary>
        public bool ShowTargetMarker
        {
            get => showTargetMarker;
            set => showTargetMarker = value;
        }

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
            if (body != null) body.CalibrationChanged += OnCalibrationChanged;
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

        /// <summary>右・左の追加肢を、設定する追加肢に選ぶ（その側の追加肢を動かしていなければ何もしない）。</summary>
        public void SelectSide(LimbSide side)
        {
            for (int i = 0; i < channels.Count; i++)
                if (channels[i].Side == side) Select(i);
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

        /// <summary>
        /// ほかの部品（実験者パネルなど）のできごとをイベントとして出す（記録に残る）。出どころは今の出どころ（WithOrigin で付ける）。
        /// </summary>
        public void Report(string type, string subject, string key = "", string oldValue = "", string newValue = "", string message = "") =>
            Raise(type, subject, key, oldValue, newValue, message);

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
            if (registry == null) return;
            motion.ThresholdMetersPerSecond = movingThresholdCmPerSecond * 0.01f;
            motion.Update(registry, Time.unscaledDeltaTime, Time.unscaledTime, source => source is IAssignableSite);
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
            // IKターゲットの目印は実験者のための可視化なので、モニタにだけ描く（Step 4-2b）
            go.layer = DisplayLayers.MonitorOnly;
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * size;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.color = color;
            go.GetComponent<Renderer>().material = material;
            return go.transform;
        }

        /// <summary>ソースの表示名（例：右足首（LHR-…・SteamVR）、部屋（SteamVR））。</summary>
        public string DescribeSource(string id)
        {
            if (id == null || registry == null || !registry.TryGet(id, out var source)) return "なし";
            if (source is FixedPoseSource) return $"部屋（{source.Space}）";
            return $"{BodySiteNames.Japanese(source.Site)}（{id}・{source.Space}）";
        }
    }
}
