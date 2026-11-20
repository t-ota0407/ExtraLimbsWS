using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ExtraLimb.Core;
using ExtraLimb.Limb;
using ExtraLimb.Tracking;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>条件の進行の段階（Step 4-3）。</summary>
    public enum TrialPhase
    {
        /// <summary>条件を読み込んでいない（提示順がないか、1つ目を読み込む前）。</summary>
        Idle,

        /// <summary>条件を読み込んだ。Re-center と動かし方の説明をして、開始を待つ。</summary>
        Ready,

        /// <summary>前半の前の案内とカウントダウン。</summary>
        Lead,

        /// <summary>前半（追加肢だけ）。</summary>
        Segment1,

        /// <summary>後半の前の案内とカウントダウン。</summary>
        Interlude,

        /// <summary>後半（本来の腕も使う）。</summary>
        Segment2,

        /// <summary>口頭評価の入力を待つ。</summary>
        Rating,

        /// <summary>条件を終えた（次へを待つ）。</summary>
        Done,

        /// <summary>入力部位のブロックの間の休憩。</summary>
        Break,

        /// <summary>すべての条件を終えた。</summary>
        Finished,

        /// <summary>開始（または練習）の前に、参加者が楽な姿勢で止まるのを待つ。止まったら Re-center して Lead へ（2026-11-15）。</summary>
        Settle,
    }

    /// <summary>提示順の中の各条件の状態。</summary>
    public enum ConditionStatus
    {
        NotRun,
        Aborted,
        Done,
    }

    /// <summary>
    /// 条件の進行（Step 4-3、計画書の1.3〜1.5）。参加者IDから提示順を作り、条件を1つずつ読み込み、
    /// 前半1分（追加肢だけ）・後半1分（本来の腕も使う）を時間どおりに進め、口頭評価3項目を受け取る。
    /// <list type="bullet">
    /// <item>提示順：ConditionSequence（入力部位ごとのブロック。ブロックの順はラテン方格、ブロックの中はランダム）。sequence.json に保存する。</item>
    /// <item>条件の読み込み：右の追加肢の生やす位置・入力部位・マッピングの型（ゲインは型の初期値）を LimbSession.Apply で変える。2本目は隠す。</item>
    /// <item>課題の時間：TrialTimeline。HMD の案内とカウントダウンは IParticipantNotice（HmdNotice）に出す。前半・後半の始めと終わりは IConditionTask（Step 5-1）に知らせる。</item>
    /// <item>記録：できごとは LimbSession のイベント（events.csv）、条件ごとの1行は conditions.csv。</item>
    /// </list>
    /// 操作は実験者パネルとホットキー（PageDown / PageUp、Enter、Esc、R、数字キー）から呼ぶ。
    /// <para>
    /// モード（WorkshopMode、2026-11-12）：デモでは提示順を使わず、今の設定のままの練習（リーチング課題）だけを行う。
    /// 実験では、生やす位置 × 入力部位の12条件（入力部位ごとの型は ExperimentDesign で1つ）を提示順どおりに進める。
    /// 実験モードに入ると記録を始め、読み込んである条件の設定に戻す。
    /// </para>
    /// <para>
    /// 追加肢は左右2本（2026-11-13）。条件の入力部位（右の部位で持つ）で右の追加肢を、左の同じ部位で左の追加肢を動かし、
    /// 左の設定は右を鏡に写す（LimbSession.MirrorLeftFromRight）。トラッカーは4台で、前半2ブロックの部位（左右）に付けておき、
    /// 3つ目のブロックの前の休憩で後半2ブロックの部位に付け替える（TrackerSwaps・SwapTrackers）。
    /// </para>
    /// <para>
    /// 間違いを防ぐ仕組み（2026-11-15）：
    /// トラッカーは、付けた後と付け替えた後に「トラッカーの確認」（TrackerCheck。部位を1つずつ動かしてもらい、動いたトラッカーを割り当てる）をする。
    /// 開始（Enter）は、体の寸法・そのブロックの部位のトラッカーの確認・入力の追跡がそろっていなければ断り、理由を出す（CanStart）。
    /// 開始すると、参加者が楽な姿勢で止まるのを待って Re-center してから案内とカウントダウンに進む（Settle）。
    /// ブロックの最初の開始では、休憩で座り直したのに合わせてリーチング課題の円を置き直す（IConditionTask.Relayout）。
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(10030)]
    public class ConditionRunner : MonoBehaviour
    {
        public const string ConditionsFileName = "conditions.csv";
        public const string SequenceFileName = "sequence";

        /// <summary>conditions.csv の列。時刻は events.csv の time と同じ（記録の開始からの秒）。</summary>
        public static readonly string[] ConditionColumns =
        {
            "participant", "latin_row", "order", "code", "block", "block_site", "index_in_block",
            "attach_point", "input_site", "mapping", "gain", "matches_condition", "attempt", "status",
            "loaded_time", "start_time", "seg1_start", "seg1_end", "seg2_start", "seg2_end",
            "seg1_hits", "seg2_hits", "rating_ease", "rating_body", "rating_fatigue", "recenter_requests", "note", "limbs",
            "limbs_driven",
        };

        [Tooltip("空なら同じ GameObject かシーンから探す")]
        [SerializeField] LimbSession session;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] ExperimentRecorder recorder;
        [Tooltip("参加者への通知（IParticipantNotice）。空ならシーンから探す（HmdNotice）")]
        [SerializeField] MonoBehaviour noticeTarget;
        [Tooltip("課題（IConditionTask）。空ならシーンから探す（Step 5-1 のリーチング課題）")]
        [SerializeField] MonoBehaviour taskTarget;
        [SerializeField] TrialTiming timing = new TrialTiming();
        [Tooltip("提示順を作ったとき、記録を参加者IDの名前のフォルダで始め直す")]
        [SerializeField] bool restartRecordingWithParticipant = true;
        [Tooltip("再生を始めたときのモード。デモなら記録を自動で始めない（実験モードに切り替えたときに始める）")]
        [SerializeField] WorkshopMode startMode = WorkshopMode.Demo;
        [Tooltip("実験で入力部位ごとに使う型を保存するファイル（Application.persistentDataPath/ExtraLimb/ の下）")]
        [SerializeField] string designFileName = "experiment_design.json";

        [Header("HMD に出す文")]
        [SerializeField] string conditionChangedText = "条件が変わります";
        [SerializeField] string segment1Text = "追加肢だけで触れます";
        [SerializeField] string segment2Text = "次は両手も使えます";
        [SerializeField] string startText = "はじめ";
        [SerializeField] string endText = "おわり";
        [SerializeField] string abortText = "中断します";
        [SerializeField] string breakText = "休憩です";
        [SerializeField] string finishedText = "すべて終わりました";

        // 間違いを防ぐ仕組み（2026-11-15）。シーンに保存しないよう定数にする
        const float SettleMaxSpeed = 0.03f;
        const float SettleMaxAngularSpeed = 15f;
        const float SettleHoldSeconds = 0.5f;
        const float TrackerCheckPauseSeconds = 0.8f;
        const string SettleText = "楽な姿勢で止まってください";
        const string SettleFeetText = "楽な姿勢で、両足をそろえて止まってください";
        const string TrackerCheckDoneText = "確認できました";
        // 左の部位を確かめる番で、残りの左の部位に割り振れるトラッカーが足りない状態がこれ（秒）続いたら、左はトラッカーなしとして飛ばす（2026-11-20）
        const float LeftShortageSeconds = 1f;

        readonly TrackerCheck trackerCheck = new TrackerCheck();
        readonly StillnessDetector stillness = new StillnessDetector
            { MaxSpeed = SettleMaxSpeed, MaxAngularSpeed = SettleMaxAngularSpeed, HoldSeconds = SettleHoldSeconds };
        readonly HashSet<BodySite> verifiedSites = new HashSet<BodySite>();
        // トラッカーの確認で「トラッカーなし」とした左の部位（右の追加肢だけで行う。左の追加肢は止める。2026-11-20）
        readonly HashSet<BodySite> absentSites = new HashSet<BodySite>();
        float leftShortageSince = -1f;
        readonly List<(string, Vector3)> checkPositions = new List<(string, Vector3)>();
        readonly List<(string, bool, Pose)> settlePoses = new List<(string, bool, Pose)>();
        bool settlePractice, settledByHand;
        float trackerCheckResumeAt;
        int ringBlock = -1;

        readonly TrialTimeline timeline = new TrialTimeline();
        readonly List<TrialCue> cueBuffer = new List<TrialCue>();
        readonly ConditionRating rating = new ConditionRating();
        readonly CsvRow row = new CsvRow();
        readonly int?[] hits = new int?[2];

        ConditionPlan plan;
        ConditionStatus[] statuses = Array.Empty<ConditionStatus>();
        int[] attempts = Array.Empty<int>();
        int currentIndex = -1;
        TrialPhase phase = TrialPhase.Idle;
        TrialPhase phaseBeforePractice;
        bool practice;
        int practiceCount;
        bool matchesAtStart;
        int drivenAtStart;
        int recenterRequests;
        float breakStartedAt;
        double loadedAt = double.NaN, startedAt = double.NaN;
        double seg1Start = double.NaN, seg1End = double.NaN, seg2Start = double.NaN, seg2End = double.NaN;
        string message = "";
        IParticipantNotice notice;
        IConditionTask task;
        WorkshopMode mode;
        bool modeInitialized;
        ExperimentDesign design;

        /// <summary>今の提示順（なければ null）。</summary>
        public ConditionPlan Plan => plan;

        public bool HasPlan => plan != null && plan.Count > 0;

        /// <summary>読み込んでいる条件の番号（0 始まり。読み込んでいなければ -1）。</summary>
        public int CurrentIndex => currentIndex;

        /// <summary>読み込んでいる条件（なければ null）。</summary>
        public ConditionSpec Current => plan != null && currentIndex >= 0 && currentIndex < plan.Count ? plan.conditions[currentIndex] : null;

        public TrialPhase Phase => phase;

        /// <summary>課題の途中か（開始の前の待ち・案内・カウントダウン・前半・後半）。</summary>
        public bool IsRunning => phase == TrialPhase.Settle || phase == TrialPhase.Lead || phase == TrialPhase.Segment1 || phase == TrialPhase.Interlude
                                 || phase == TrialPhase.Segment2;

        /// <summary>練習（今の設定で、提示順に数えない）の途中か（開始の前の待ちを含む）。</summary>
        public bool IsPractice => practice || (phase == TrialPhase.Settle && settlePractice);

        /// <summary>トラッカーの確認（2026-11-15）。</summary>
        public TrackerCheck TrackerCheck => trackerCheck;

        /// <summary>開始の前に、参加者が止まり続けている秒。</summary>
        public float SettleStillSeconds => stillness.StillSeconds;

        /// <summary>開始の前の待ちで、止まったとみなすまでの秒。</summary>
        public float SettleHoldTime => SettleHoldSeconds;

        /// <summary>この提示順で、ブロックの始めに円を置き直した最後のブロック（まだなら -1）。</summary>
        public int RingBlock => ringBlock;

        /// <summary>今の区間（0＝区間の外、1＝前半、2＝後半）。</summary>
        public int Segment => timeline.Segment;

        /// <summary>本来の手で触れても数えるか（後半だけ）。</summary>
        public bool NaturalHandsCount => Segment == 2;

        /// <summary>今の区間の残りの秒。</summary>
        public double SegmentRemaining => timeline.SegmentRemaining(Now);

        /// <summary>次の区間（前半・後半）が始まるまでの秒（区間の前の案内・カウントダウンの間だけ。ほかは 0）。</summary>
        public double SecondsToSegmentStart
        {
            get
            {
                double now = Now;
                double start = phase == TrialPhase.Lead ? timeline.TimeOf(TrialCue.Segment1Start)
                    : phase == TrialPhase.Interlude ? timeline.TimeOf(TrialCue.Segment2Start) : double.NaN;
                return double.IsNaN(start) ? 0 : Math.Max(0, start - now);
            }
        }

        /// <summary>次の区切り（区間の始まりなど）までの秒。</summary>
        public double UntilNextCue => timeline.UntilNext(Now);

        public ConditionRating Rating => rating;

        public TrialTiming Timing => timing;

        /// <summary>最後のメッセージ（パネルに出す）。</summary>
        public string Message => message;

        /// <summary>条件を読み込んでから、実験者が Re-center した回数。</summary>
        public int RecenterRequestsSinceLoad => recenterRequests;

        /// <summary>休憩に入ってからの秒。</summary>
        public float BreakElapsed => phase == TrialPhase.Break ? Time.unscaledTime - breakStartedAt : 0f;

        /// <summary>前半・後半の到達数（課題がないか、まだなら null）。</summary>
        public int? HitsInSegment(int segment) => segment == 1 || segment == 2 ? hits[segment - 1] : null;

        public ConditionStatus StatusOf(int index) => index >= 0 && index < statuses.Length ? statuses[index] : ConditionStatus.NotRun;

        public int AttemptsOf(int index) => index >= 0 && index < attempts.Length ? attempts[index] : 0;

        /// <summary>練習をした回数（提示順を作り直すと0に戻る）。</summary>
        public int PracticeCount => practiceCount;

        /// <summary>終えた条件の数。</summary>
        public int DoneCount
        {
            get
            {
                int count = 0;
                foreach (var status in statuses)
                    if (status == ConditionStatus.Done) count++;
                return count;
            }
        }

        /// <summary>今の右の追加肢の設定が、読み込んだ条件と同じか（2本目を出していても違うとみなす）。</summary>
        public bool MatchesCurrent => Matches(Current);

        /// <summary>今のモード（デモ／実験）。</summary>
        public WorkshopMode Mode => mode;

        public bool IsExperimentMode => mode == WorkshopMode.Experiment;

        /// <summary>条件で決まる設定（生やす位置・入力部位・型・反転・ゲイン・2本目）を、パネルとホットキーから変えられないか（実験モード）。</summary>
        public bool ConditionSettingsLocked => IsExperimentMode;

        /// <summary>実験で入力部位ごとに使う型。</summary>
        public ExperimentDesign Design => design ??= new ExperimentDesign();

        /// <summary>実験で入力部位ごとに使う型を保存するファイル。</summary>
        public string DesignFilePath => Path.Combine(Application.persistentDataPath, "ExtraLimb", designFileName);

        /// <summary>モードを切り替えた直後。</summary>
        public event Action<WorkshopMode> ModeChanged;

        /// <summary>段階が変わった直後。</summary>
        public event Action<TrialPhase> PhaseChanged;

        /// <summary>条件を読み込んだ直後。</summary>
        public event Action<ConditionSpec> ConditionLoaded;

        /// <summary>前半（1）・後半（2）が始まった。2つ目の引数は、本来の手で触れても数えるか。</summary>
        public event Action<int, bool> SegmentStarted;

        /// <summary>前半（1）・後半（2）が終わった。</summary>
        public event Action<int> SegmentEnded;

        /// <summary>段階の表示名。</summary>
        public static string PhaseLabel(TrialPhase phase)
        {
            switch (phase)
            {
                case TrialPhase.Ready: return "準備（動かし方を伝える → 開始）";
                case TrialPhase.Settle: return "開始の前（参加者が止まるのを待って Re-center）";
                case TrialPhase.Lead: return "前半の前の案内とカウントダウン";
                case TrialPhase.Segment1: return "前半（追加肢だけ）";
                case TrialPhase.Interlude: return "後半の前の案内とカウントダウン";
                case TrialPhase.Segment2: return "後半（本来の腕も使う）";
                case TrialPhase.Rating: return "口頭評価の入力";
                case TrialPhase.Done: return "終了（次へ）";
                case TrialPhase.Break: return "休憩";
                case TrialPhase.Finished: return "すべて終了";
                default: return "条件を読み込んでいません";
            }
        }

        void OnEnable()
        {
            if (session == null) session = GetComponent<LimbSession>();
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            if (recorder == null) recorder = FindFirstObjectByType<ExperimentRecorder>();
            if (session != null) session.EventRaised += OnSessionEvent;
            else Debug.LogWarning("[ExtraLimb] 条件の進行：LimbSession がシーンにありません", this);
            if (!modeInitialized)
            {
                modeInitialized = true;
                mode = startMode;
                LoadDesign();
                // 記録は実験モードで始める（ExperimentRecorder.Start より前に決める）
                if (recorder != null) recorder.AutoStart = mode == WorkshopMode.Experiment;
            }
        }

        void OnDisable()
        {
            if (session != null) session.EventRaised -= OnSessionEvent;
            if (timeline.Running) timeline.Stop();
        }

        void Start()
        {
            ResolveNotice();
            ResolveTask();
            // 追加肢は左右2本（LimbSession.Start で追加肢ができた後）
            if (session != null && session.HasLeftLimb) session.SetTwoLimbs(true);
        }

        void ResolveNotice()
        {
            if (notice != null) return;
            notice = noticeTarget as IParticipantNotice;
            if (notice == null) notice = FindInScene<IParticipantNotice>();
        }

        void ResolveTask()
        {
            if (task != null && !(task is UnityEngine.Object o && o == null)) return;
            task = taskTarget as IConditionTask;
            if (task == null) task = FindInScene<IConditionTask>();
        }

        static T FindInScene<T>() where T : class
        {
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour is T found && behaviour.isActiveAndEnabled) return found;
            return null;
        }

        void OnSessionEvent(SessionEvent e)
        {
            if (e.type != SessionEventTypes.RecenterRequest) return;
            recenterRequests++;
            // 開始の前の待ちの間に実験者が Re-center したら、今の姿勢のまま始める
            if (phase == TrialPhase.Settle && e.origin != ChangeOrigins.TrialStart && e.origin != ChangeOrigins.System) settledByHand = true;
        }

        // ---- モード ----

        /// <summary>
        /// デモ／実験を切り替える。課題の途中と口頭評価の入力中は切り替えない。
        /// 実験モードに入ると、記録していなければ記録を始め、2本目を隠し、準備中の条件があればその設定に戻す。
        /// </summary>
        public bool SetMode(WorkshopMode next)
        {
            if (next == mode) return true;
            if (IsRunning) return Fail("課題の途中です。中止（Esc）してから切り替えてください");
            if (phase == TrialPhase.Rating) return Fail("口頭評価を入れてから切り替えてください");
            var previous = mode;
            mode = next;
            if (next == WorkshopMode.Experiment && recorder != null && !recorder.IsRecording)
                recorder.StartRecording(session != null ? session.CurrentOrigin : ChangeOrigins.Api, plan?.participantId);
            Report(SessionEventTypes.Mode, "session", "mode", ModeKey(previous), ModeKey(next),
                next == WorkshopMode.Experiment ? "実験モードにしました" : "デモモードにしました");
            if (next == WorkshopMode.Experiment) RestoreConditionSettings();
            Say("");
            Debug.Log($"[ExtraLimb] {(next == WorkshopMode.Experiment ? "実験" : "デモ")}モードにしました");
            ModeChanged?.Invoke(next);
            return true;
        }

        // 実験モードに入ったとき：左右2本にする。準備中の条件があればその設定に、なければ左を右の鏡写しに戻す（デモで変えたものを戻す）
        void RestoreConditionSettings()
        {
            if (session == null) return;
            if (Current != null && phase == TrialPhase.Ready && !Matches(Current))
            {
                ApplySpec(Current);
                return;
            }
            if (session.HasLeftLimb)
            {
                session.SetTwoLimbs(true);
                session.MirrorLeftFromRight();
            }
        }

        /// <summary>実験モードで、条件で決まる設定を変えようとしたときに知らせる。</summary>
        public void NotifyLocked() => Say("実験モードでは条件の設定は変えられません（変えるときはデモモードへ）");

        static string ModeKey(WorkshopMode value) => value == WorkshopMode.Experiment ? "experiment" : "demo";

        bool RequireExperiment() => IsExperimentMode || Fail("デモモードでは使えません（実験モードに切り替えてください）");

        // ---- 実験で使う型 ----

        /// <summary>実験で入力部位 site に使う型（選んでいなければその部位の既定の型）。</summary>
        public MappingPreset ExperimentMapping(BodySite site) => session != null ? Design.Resolve(session.MappingPresetList, site) : null;

        /// <summary>実験で入力部位 site に使う型を選び、保存する（デモモードだけ）。作ってある提示順には、作り直すまで反映しない。</summary>
        public bool SetExperimentMapping(BodySite site, MappingPreset preset)
        {
            if (preset == null || session == null) return false;
            if (ConditionSettingsLocked)
            {
                NotifyLocked();
                return false;
            }
            var old = ExperimentMapping(site);
            if (old == preset) return false;
            Design.Set(site, preset.id);
            SaveDesign();
            string siteName = BodySiteNames.Kind(site);
            Report(SessionEventTypes.ExperimentMapping, "session", site.ToString(), old != null ? old.id : "", preset.id,
                $"実験で{siteName}に使う型を「{MappingPresets.ShortName(preset)}」にしました");
            bool planDiffers = false;
            if (plan != null)
                foreach (var condition in plan.conditions)
                    if (condition.inputSite == site && condition.mappingPresetId != preset.id) planDiffers = true;
            Say(planDiffers ? $"{siteName}の型を変えました。今の提示順に反映するには、実験モードで提示順を作り直してください" : "");
            return true;
        }

        void LoadDesign()
        {
            design = new ExperimentDesign();
            try
            {
                if (File.Exists(DesignFilePath)) design = ExperimentDesign.FromJson(File.ReadAllText(DesignFilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExtraLimb] 実験で使う型を読めませんでした（既定の型を使います）：{DesignFilePath}\n{e.Message}", this);
            }
        }

        void SaveDesign()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DesignFilePath));
                File.WriteAllText(DesignFilePath, Design.ToJson());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExtraLimb] 実験で使う型を保存できませんでした：{DesignFilePath}\n{e.Message}", this);
            }
        }

        // ---- 提示順 ----

        /// <summary>
        /// 参加者IDから提示順を作る。row（ラテン方格の行、0 始まり）を省くと、IDの数字から決める。
        /// 記録を参加者IDの名前のフォルダで始め直し、提示順を sequence.json に保存する。1つ目の条件は「次へ」で読み込む。
        /// </summary>
        public bool CreatePlan(string participantId, int? row = null)
        {
            if (!IsExperimentMode) return Fail("提示順は実験モードで作ります");
            if (IsRunning)
            {
                Say("課題の途中です。中止（Esc）してから作り直してください");
                return false;
            }
            participantId = (participantId ?? "").Trim();
            if (participantId.Length == 0)
            {
                Say("参加者IDを入れてください");
                return false;
            }
            var mount = session != null ? session.Primary?.Mount : null;
            if (mount == null)
            {
                Say("生やす位置が設定されていないため、提示順を作れません");
                return false;
            }

            var attachPoints = new List<ConditionSequence.Option>();
            foreach (var point in mount.Registry.Points)
                if (point != null) attachPoints.Add(new ConditionSequence.Option(point.id, point.displayName));
            var presets = session.MappingPresetList;
            // 入力部位ごとの型は1つ（実験で使う型）。生やす位置 × 入力部位の条件になる
            var newPlan = ConditionSequence.Build(participantId, attachPoints, session.InputSites, site =>
            {
                var options = new List<ConditionSequence.Option>();
                var preset = Design.Resolve(presets, site);
                if (preset != null) options.Add(new ConditionSequence.Option(preset.id, MappingPresets.ShortName(preset)));
                return options;
            }, row);
            if (newPlan.Count == 0)
            {
                Say("条件がありません（生やす位置・入力部位・マッピングの型を確かめてください）");
                return false;
            }

            // 参加者が変わったら、トラッカーの確認と円はやり直す
            if (plan == null || plan.participantId != newPlan.participantId)
            {
                verifiedSites.Clear();
                absentSites.Clear();
                session?.SetHeldSites(absentSites);
            }
            ringBlock = -1;
            if (trackerCheck.IsActive) CancelTrackerCheck();
            plan = newPlan;
            statuses = new ConditionStatus[plan.Count];
            attempts = new int[plan.Count];
            currentIndex = -1;
            practiceCount = 0;
            rating.Clear();
            ResetTimes();
            SetPhase(TrialPhase.Idle);

            string origin = session.CurrentOrigin;
            if (recorder != null)
            {
                recorder.SessionLabel = participantId;
                if (restartRecordingWithParticipant && recorder.CurrentLabel != ExperimentLogger.Sanitize(participantId))
                {
                    if (recorder.IsRecording) recorder.StopRecording(origin);
                    recorder.StartRecording(origin, participantId);
                }
            }
            string order = $"{plan.latinRow + 1}/{plan.latinRowCount}：{plan.BlockOrderText}";
            Report(SessionEventTypes.Sequence, "session", participantId, "", order,
                $"提示順を作りました：{participantId}、ブロックの順 {order}、全{plan.Count}条件、型 {DesignText()}");
            recorder?.SaveJson(SequenceFileName, JsonUtility.ToJson(plan, true));
            Say("");
            Debug.Log($"[ExtraLimb] 提示順：{participantId}、ブロックの順 {order}、全{plan.Count}条件");
            return true;
        }

        /// <summary>実験で使う型の一覧（右足首＝並進、…）。</summary>
        public string DesignText()
        {
            if (session == null) return "";
            var parts = new List<string>();
            foreach (var site in session.InputSites)
            {
                var preset = ExperimentMapping(site);
                parts.Add($"{BodySiteNames.Kind(site)}＝{(preset != null ? MappingPresets.ShortName(preset) : "なし")}");
            }
            return string.Join("、", parts);
        }

        // ---- 条件の読み込み ----

        /// <summary>提示順の index 番目（0 始まり）の条件を読み込む。課題の途中や評価の入力中は読み込まない。実験モードだけ。</summary>
        public bool Load(int index) => RequireExperiment() && Load(index, true);

        bool Load(int index, bool announce)
        {
            if (plan == null || index < 0 || index >= plan.Count) return false;
            if (IsRunning)
            {
                Say("課題の途中です。中止（Esc）してから");
                return false;
            }
            if (phase == TrialPhase.Rating && index != currentIndex)
            {
                Say("口頭評価を入れてから次へ進んでください（やめるときは Esc）");
                return false;
            }
            currentIndex = index;
            var spec = plan.conditions[index];
            ApplySpec(spec);
            rating.Clear();
            ResetTimes();
            loadedAt = Elapsed();
            recenterRequests = 0;
            Report(SessionEventTypes.Condition, "condition", Number(index), "", spec.Label,
                $"条件 {index + 1}/{plan.Count}（ブロック {spec.Code}）を読み込みました：{spec.Label}");
            ResolveNotice();
            if (announce) notice?.Show(conditionChangedText, 0f);
            SetPhase(TrialPhase.Ready);
            ConditionLoaded?.Invoke(spec);
            Say("");
            return true;
        }

        void ApplySpec(ConditionSpec spec)
        {
            if (session == null || spec == null) return;
            session.SelectSide(LimbSide.Right);
            var channel = session.Primary;
            if (channel == null) return;
            var next = channel.Config.Clone();
            next.attachPointId = spec.attachPointId;
            next.inputSite = spec.inputSite;
            next.inputSourceId = "";
            var preset = MappingPresets.Find(session.MappingPresetList, spec.mappingPresetId);
            if (preset != null)
            {
                next.mappingPresetId = preset.id;
                next.axisMapping = preset.mapping;
                next.positionGain = preset.gain;
            }
            else
            {
                Debug.LogWarning($"[ExtraLimb] 条件の進行：マッピングの型「{spec.mappingPresetId}」が見つかりません", this);
            }
            session.Apply(channel, next);
            // 左の追加肢：右を鏡に写す（左の同じ部位で動かす）
            if (session.HasLeftLimb)
            {
                session.SetTwoLimbs(true);
                session.MirrorLeftFromRight();
            }
        }

        bool Matches(ConditionSpec spec)
        {
            var channel = session != null ? session.Primary : null;
            if (spec == null || channel == null) return false;
            var config = channel.Config;
            bool right = config.attachPointId == spec.attachPointId && config.inputSite == spec.inputSite
                         && string.IsNullOrEmpty(config.inputSourceId) && config.mappingPresetId == spec.mappingPresetId;
            return right && (!session.HasLeftLimb || (session.TwoLimbs && session.LeftMirrorsRight()));
        }

        // ---- トラッカーの付け替え（4台を左右2部位ずつ） ----

        /// <summary>ブロック b（0 始まり）の入力部位（右の部位）。提示順がなければ Unknown。</summary>
        public BodySite BlockSite(int block)
        {
            if (plan == null) return BodySite.Unknown;
            foreach (var condition in plan.conditions)
                if (condition.block == block) return condition.inputSite;
            return BodySite.Unknown;
        }

        /// <summary>最初に付けるトラッカーの部位（前半2ブロックの部位の左右）。</summary>
        public IReadOnlyList<BodySite> FirstTrackerSites => TrackerSites(0, 1);

        /// <summary>3つ目のブロックの前に付け替える組（前半の部位 → 後半の部位。左右それぞれ）。4ブロックでなければ空。</summary>
        public IReadOnlyList<(BodySite from, BodySite to)> TrackerSwaps
        {
            get
            {
                var swaps = new List<(BodySite, BodySite)>();
                if (plan == null || plan.BlockCount != 4) return swaps;
                for (int i = 0; i < 2; i++)
                {
                    var from = BlockSite(i);
                    var to = BlockSite(i + 2);
                    swaps.Add((from, to));
                    swaps.Add((BodySites.Mirror(from), BodySites.Mirror(to)));
                }
                return swaps;
            }
        }

        List<BodySite> TrackerSites(int firstBlock, int secondBlock)
        {
            var sites = new List<BodySite>();
            if (plan == null) return sites;
            foreach (int block in new[] { firstBlock, secondBlock })
            {
                var site = BlockSite(block);
                if (site == BodySite.Unknown) continue;
                sites.Add(site);
                sites.Add(BodySites.Mirror(site));
            }
            return sites;
        }

        /// <summary>ブロック block（0 始まり）でトラッカーを付けておく部位（前半か後半の2ブロックの部位の左右。右、左の順）。</summary>
        public IReadOnlyList<BodySite> TrackerSitesForBlock(int block) => block < 2 ? TrackerSites(0, 1) : TrackerSites(2, 3);

        // 次に行う条件（準備中・課題中なら今の条件）。なければ -1
        int UpcomingIndex => plan == null ? -1
            : phase == TrialPhase.Ready || IsRunning ? currentIndex
            : currentIndex + 1 < plan.Count ? currentIndex + 1 : -1;

        /// <summary>次に行う条件のブロックでトラッカーを付けておく部位（提示順がなければ空）。</summary>
        public IReadOnlyList<BodySite> RequiredTrackerSites
        {
            get
            {
                int upcoming = UpcomingIndex;
                return upcoming >= 0 ? TrackerSitesForBlock(plan.conditions[upcoming].block) : Array.Empty<BodySite>();
            }
        }

        /// <summary>その部位のトラッカーを、この参加者で確認したか。</summary>
        public bool IsTrackerSiteVerified(BodySite site) => verifiedSites.Contains(site);

        /// <summary>その部位を、トラッカーの確認で「トラッカーなし」にしたか（左だけ。右の追加肢だけで行う）。</summary>
        public bool IsTrackerSiteAbsent(BodySite site) => absentSites.Contains(site);

        // 確認が済んだ部位：トラッカーを割り当てたか、トラッカーなしにした
        bool IsTrackerSiteDone(BodySite site) => verifiedSites.Contains(site) || absentSites.Contains(site);

        /// <summary>
        /// 左の追加肢を止めて右だけで行っているときの説明（左のトラッカーがない・追跡できない、または確認で「なし」にした）。なければ空。
        /// 左のトラッカーがなくても始められる（右の追加肢だけで行う）。
        /// </summary>
        public string LeftLimbNote
        {
            get
            {
                if (session == null) return "";
                foreach (var channel in session.Channels)
                    if (channel.Side == LimbSide.Left && channel.Active && !channel.IsInputValid)
                        return $"右の追加肢だけで行います。左の追加肢は止めています：{channel.StatusMessage}";
                return "";
            }
        }

        /// <summary>
        /// トラッカーの確認が要るか：実験モードで、次に行う条件のブロックの部位に、まだ確認していない部位がある（最初の装着と、後半の前の付け替え）。
        /// 課題の途中・評価の入力中・確認の途中は false。
        /// </summary>
        public bool NeedsTrackerCheck
        {
            get
            {
                if (!IsExperimentMode || plan == null || IsRunning || phase == TrialPhase.Rating || trackerCheck.IsActive) return false;
                foreach (var site in RequiredTrackerSites)
                    if (!IsTrackerSiteDone(site)) return true;
                return false;
            }
        }

        // ---- トラッカーの確認（2026-11-15） ----

        /// <summary>
        /// トラッカーの確認を始める。実験モードで提示順があれば次に行うブロックの部位（2部位の左右）を、なければ動かしている追加肢の入力の部位を、
        /// 1つずつ確かめる。部位を挙げたら、参加者にその部位を動かしてもらうか、実験者がその部位のトラッカーを揺らす。
        /// </summary>
        public bool StartTrackerCheck()
        {
            if (IsRunning) return Fail("課題の途中です。中止（Esc）してから");
            if (phase == TrialPhase.Rating) return Fail("口頭評価を入れてから");
            var sites = RightFirst(IsExperimentMode && plan != null ? RequiredTrackerSites : DemoCheckSites());
            if (sites.Count == 0) return Fail("確かめる部位がありません");
            trackerCheck.Begin(sites);
            trackerCheckResumeAt = Time.unscaledTime;
            leftShortageSince = -1f;
            var names = new List<string>();
            foreach (var site in sites) names.Add(BodySiteNames.Japanese(site));
            Report(SessionEventTypes.TrackerCheck, "session", "start", "", string.Join(" ", names), $"トラッカーの確認を始めました：{string.Join("・", names)}");
            ResolveNotice();
            ShowCheckPrompt();
            Say("");
            return true;
        }

        // 右の部位を先に、左の部位を後に確かめる（左のトラッカーがないときは、右を確かめ終えた後で左をまとめて飛ばせる）
        static List<BodySite> RightFirst(IReadOnlyList<BodySite> sites)
        {
            var ordered = new List<BodySite>();
            foreach (var site in sites)
                if (!BodySites.IsLeft(site)) ordered.Add(site);
            foreach (var site in sites)
                if (BodySites.IsLeft(site)) ordered.Add(site);
            return ordered;
        }

        /// <summary>トラッカーの確認で、今は左の部位を確かめているか（「左はなし」で飛ばせる）。</summary>
        public bool CanSkipLeftTrackers => trackerCheck.IsActive && BodySites.IsLeft(trackerCheck.CurrentSite);

        /// <summary>
        /// 左のトラッカーを付けずに行う：確認の残りの左の部位を「トラッカーなし」として飛ばす。左の追加肢は止め、右の追加肢だけで行う。
        /// 左の部位らしい位置（右の同じ部位と高さが近い所）にトラッカーがなければ確認が自動で飛ばすので、飛ばないときにこのボタンを使う。
        /// </summary>
        public bool SkipLeftTrackers() => SkipLeftTrackers(false);

        bool SkipLeftTrackers(bool automatic)
        {
            if (!CanSkipLeftTrackers) return Fail("左の部位を確かめているときに使えます");
            var names = new List<string>();
            while (trackerCheck.IsActive && BodySites.IsLeft(trackerCheck.CurrentSite))
                names.Add(BodySiteNames.Japanese(trackerCheck.Skip()));
            string text = string.Join("・", names);
            Report(SessionEventTypes.TrackerCheck, "session", "skip_left", automatic ? "auto" : "manual", text,
                automatic ? $"トラッカーの確認：左の部位らしい位置にトラッカーがないため、左（{text}）はトラッカーなしにしました"
                          : $"トラッカーの確認：左（{text}）はトラッカーなしにしました");
            trackerCheckResumeAt = Time.unscaledTime + TrackerCheckPauseSeconds;
            if (trackerCheck.IsComplete) FinishTrackerCheck();
            else ShowCheckPrompt();
            return true;
        }

        /// <summary>トラッカーの確認をやめる（割り当ては変えない）。</summary>
        public void CancelTrackerCheck()
        {
            if (!trackerCheck.IsActive) return;
            trackerCheck.Cancel();
            notice?.Clear();
            Report(SessionEventTypes.TrackerCheck, "session", "cancel", "", "", "トラッカーの確認をやめました");
            Say("トラッカーの確認をやめました（割り当ては変えていません）");
        }

        /// <summary>参加者に見せる、部位を動かしてもらう文。</summary>
        public static string MovePrompt(BodySite site) => $"{BodySiteNames.Japanese(site)}を少し動かしてください";

        /// <summary>実験者への案内。</summary>
        public static string CheckInstruction(BodySite site)
        {
            string name = BodySiteNames.Japanese(site);
            return $"{name}：参加者に{name}を動かしてもらうか、{name}のトラッカーを軽く揺らしてください";
        }

        List<BodySite> DemoCheckSites()
        {
            var sites = new List<BodySite>();
            if (session == null) return sites;
            foreach (var channel in session.Channels)
            {
                if (!channel.Active) continue;
                var site = BodySites.OnSide(channel.Config.inputSite, channel.Side == LimbSide.Left);
                if (site != BodySite.Unknown && !sites.Contains(site)) sites.Add(site);
            }
            return sites;
        }

        void ShowCheckPrompt()
        {
            if (trackerCheck.IsActive) notice?.Show(MovePrompt(trackerCheck.CurrentSite), 600f);
        }

        void UpdateTrackerCheck()
        {
            var registry = session != null ? session.Registry : null;
            if (registry == null) return;
            checkPositions.Clear();
            foreach (var source in registry.Sources)
                if (source is IAssignableSite && source.TryGetPose(out var pose)) checkPositions.Add((source.Id, pose.position));
            // 部位と部位の間は一息おく（前の部位を動かし終えるまでの動きを数えない）
            if (Time.unscaledTime < trackerCheckResumeAt)
            {
                trackerCheck.Observe(checkPositions);
                return;
            }
            if (UpdateLeftShortage()) return;
            var done = trackerCheck.Update(checkPositions);
            if (done == BodySite.Unknown) return;
            trackerCheck.TryGetFound(done, out string id);
            Report(SessionEventTypes.TrackerCheck, "session", done.ToString(), "", id, $"トラッカーの確認：{BodySiteNames.Japanese(done)}＝{id}");
            trackerCheckResumeAt = Time.unscaledTime + TrackerCheckPauseSeconds;
            if (trackerCheck.IsComplete) FinishTrackerCheck();
            else ShowCheckPrompt();
        }

        // 左の部位の番で、残りの左の部位に割り振れるトラッカー（確認を始めてから見えた、まだ決まっていないもの。
        // 右の同じ部位と高さが近く、ほどよく離れたもの：TrackerCheck.CanCoverRemaining）が足りない状態が続いたら、左をまとめて飛ばす
        bool UpdateLeftShortage()
        {
            if (!CanSkipLeftTrackers)
            {
                leftShortageSince = -1f;
                return false;
            }
            if (trackerCheck.CanCoverRemaining(Vector3.up))
            {
                leftShortageSince = -1f;
                return false;
            }
            if (leftShortageSince < 0f) leftShortageSince = Time.unscaledTime;
            if (Time.unscaledTime - leftShortageSince < LeftShortageSeconds) return false;
            leftShortageSince = -1f;
            return SkipLeftTrackers(true);
        }

        // 確かめた部位にトラッカーを割り当てる。前の部位（付け替える前の部位など）の割り当ては外れる。
        // トラッカーなしにした左の部位は、左の追加肢を止める（古い割り当てのトラッカーが残っていても使わない）
        void FinishTrackerCheck()
        {
            var bindings = TrackerBindingService.Find();
            var parts = new List<string>();
            foreach (var pair in trackerCheck.Found)
            {
                bindings?.Assign(pair.Value, pair.Key);
                verifiedSites.Add(pair.Key);
                parts.Add($"{BodySiteNames.Japanese(pair.Key)}＝{pair.Value}");
            }
            if (bindings != null) verifiedSites.RemoveWhere(site => !bindings.HasAssigned(site));
            foreach (var site in trackerCheck.Found.Keys) absentSites.Remove(site);
            foreach (var site in trackerCheck.Skipped)
            {
                absentSites.Add(site);
                verifiedSites.Remove(site);
                parts.Add($"{BodySiteNames.Japanese(site)}＝なし");
            }
            session?.SetHeldSites(absentSites);
            session?.ResetForwardBaseline();
            notice?.Show(TrackerCheckDoneText, 2f);
            string text = string.Join("、", parts);
            Report(SessionEventTypes.TrackerCheck, "session", "done", "", text, $"トラッカーの確認を終え、割り当てました：{text}");
            Say(bindings != null ? "" : "トラッカーの確認はできましたが、割り当てを保存する部品（TrackerBindingService）がありません");
        }

        /// <summary>トラッカーの割り当てを付け替える（前半の部位 → 後半の部位）。トラッカーは体の上で同じように付け替える。</summary>
        public bool SwapTrackers()
        {
            var bindings = TrackerBindingService.Find();
            if (bindings == null) return Fail("トラッカーの割り当てがありません（TrackerBindingService がシーンにありません）");
            if (IsRunning) return Fail("課題の途中は付け替えられません");
            var moved = new List<string>();
            foreach (var (from, to) in TrackerSwaps)
                if (bindings.MoveSite(from, to) != null) moved.Add($"{BodySiteNames.Japanese(from)}→{BodySiteNames.Japanese(to)}");
            if (moved.Count == 0) return Fail("付け替えるトラッカーがありません");
            string text = string.Join("、", moved);
            Report(SessionEventTypes.TrackerSwap, "session", "trackers", "", text, $"トラッカーの割り当てを付け替えました：{text}");
            Say($"トラッカーの割り当てを付け替えました：{text}");
            return true;
        }

        /// <summary>付け替えの案内（左足首 → 左肩 など）。</summary>
        public string TrackerSwapText()
        {
            var parts = new List<string>();
            foreach (var (from, to) in TrackerSwaps) parts.Add($"{BodySiteNames.Japanese(from)} → {BodySiteNames.Japanese(to)}");
            return string.Join("　", parts);
        }

        /// <summary>次の条件を読み込む（PageDown）。</summary>
        public bool Next()
        {
            if (!RequireExperiment()) return false;
            if (!HasPlan) return Fail("提示順がありません（参加者IDを入れて「提示順を作成」）");
            if (currentIndex + 1 >= plan.Count) return Fail("最後の条件です");
            return Load(currentIndex + 1);
        }

        /// <summary>前の条件を読み込む（PageUp）。</summary>
        public bool Previous()
        {
            if (!RequireExperiment()) return false;
            if (!HasPlan) return Fail("提示順がありません");
            if (currentIndex <= 0) return Fail("最初の条件です");
            return Load(currentIndex - 1);
        }

        /// <summary>今の条件を読み込み直して、始めからやり直す（R）。HMD には通知を出さない。</summary>
        public bool Redo()
        {
            if (!RequireExperiment()) return false;
            if (IsRunning) return Fail("課題の途中です。中止（Esc）してから");
            if (Current == null) return Fail("条件を読み込んでいません");
            if (phase == TrialPhase.Rating) Abort();
            return Load(currentIndex, false);
        }

        // ---- 課題 ----

        /// <summary>
        /// 読み込んだ条件の課題を始める（Enter）：参加者が止まるのを待って Re-center → 案内 → カウントダウン → 前半 → 案内 → カウントダウン → 後半。
        /// 始められないとき（CanStart）は理由を出して断る。
        /// </summary>
        public bool StartTrial()
        {
            if (!CanStart(out string reason)) return Fail(reason);
            BeginSettle(false);
            return true;
        }

        /// <summary>今の設定のまま、同じ流れで練習する（提示順には数えない。評価は取らない）。</summary>
        public bool StartPractice()
        {
            if (IsRunning) return Fail("課題の途中です");
            if (phase == TrialPhase.Rating) return Fail("口頭評価を入れてから");
            if (trackerCheck.IsActive) return Fail("トラッカーの確認の途中です");
            var problems = new List<string>();
            AddTrackingProblems(problems);
            if (problems.Count > 0) return Fail(string.Join("\n", problems));
            phaseBeforePractice = phase;
            BeginSettle(true);
            return true;
        }

        /// <summary>
        /// 今の条件の課題を始められるか（2026-11-15）。体の寸法を入れてあり、そのブロックの部位のトラッカーを確認してあり、
        /// 動かしている追加肢の入力を追跡できていること。始められなければ、理由を改行で区切って reason に入れる。
        /// </summary>
        public bool CanStart(out string reason)
        {
            var problems = new List<string>();
            if (!IsExperimentMode)
            {
                problems.Add("デモモードでは使えません（実験モードに切り替えてください）");
            }
            else if (phase != TrialPhase.Ready || Current == null)
            {
                problems.Add(phase == TrialPhase.Idle || Current == null ? "先に条件を読み込んでください（次へ：PgDn）"
                    : IsRunning ? "課題の途中です" : "この条件は終わっています（やり直しは R、次へは PgDn）");
            }
            else
            {
                var body = session != null ? session.Body : null;
                if (body != null && body.IsReady && !body.Calibration.IsCalibrated) problems.Add("身長・肩幅・手の長さを入れて「反映」を押してください");
                if (trackerCheck.IsActive) problems.Add("トラッカーの確認の途中です");
                var missing = new List<string>();
                foreach (var site in TrackerSitesForBlock(Current.block))
                    if (!IsTrackerSiteDone(site)) missing.Add(BodySiteNames.Japanese(site));
                if (missing.Count > 0) problems.Add($"トラッカーの確認がまだです（{string.Join("・", missing)}）");
                AddTrackingProblems(problems);
            }
            reason = string.Join("\n", problems);
            return problems.Count == 0;
        }

        // 右の追加肢の入力を追跡できていること。左はトラッカーがなくても始められる（左の追加肢は止め、右だけで行う。LeftLimbNote）。
        // 左右の同じ部位のトラッカーがそろわず、前の向きも記録していなければ、前の向きを記録してもらう
        void AddTrackingProblems(List<string> problems)
        {
            if (session == null) return;
            foreach (var channel in session.Channels)
            {
                if (!channel.Active) continue;
                if (!channel.IsInputValid)
                {
                    if (channel.Side != LimbSide.Left) problems.Add($"{channel.Label}の追加肢：{channel.StatusMessage}");
                    continue;
                }
                if (!channel.Pipeline.BodyFrame.HasForward && !session.HasForwardPair(channel.ReferenceId))
                    problems.Add($"{channel.Label}の追加肢：前の向きがまだありません（左右の同じ部位のトラッカーがないため自動で求められません）。" +
                                 $"参加者に前を向いてもらい、Re-center の後に入力の部位を前へ {session.MinForwardGestureMeters * 100f:F0} cm 以上出して F を押してください（一度記録すれば保存されます）");
            }
        }

        // ---- 開始の前の待ち（2026-11-15） ----

        void BeginSettle(bool isPractice)
        {
            settlePractice = isPractice;
            settledByHand = false;
            stillness.Reset();
            SetPhase(TrialPhase.Settle);
            ResolveNotice();
            notice?.Show(FeetPairOnly() ? SettleFeetText : SettleText, 600f);
            Report(SessionEventTypes.Settle, isPractice ? "practice" : "condition", "start", "", "", "参加者が止まるのを待っています（止まったら Re-center して始めます）");
            Say("");
        }

        // 前の向きを膝か足首の組から求めるとき（肩の組がないとき）は、両足をそろえてもらう。左右の組がなければ（右だけ）そろえなくてよい
        bool FeetPairOnly() => !PairTracked(BodySite.RightShoulder) && (PairTracked(BodySite.RightKnee) || PairTracked(BodySite.RightAnkle));

        bool PairTracked(BodySite rightSite)
        {
            var registry = session != null ? session.Registry : null;
            if (registry == null) return false;
            var leftSite = BodySites.Mirror(rightSite);
            if (session.IsSiteHeld(rightSite) || session.IsSiteHeld(leftSite)) return false;
            foreach (var space in new[] { PoseSpace.Replay, PoseSpace.SteamVR, PoseSpace.Virtual })
            {
                var right = registry.FindBySite(rightSite, space);
                var left = registry.FindBySite(leftSite, space);
                if (right != null && left != null && right.TryGetPose(out _) && left.TryGetPose(out _)) return true;
            }
            return false;
        }

        void UpdateSettle()
        {
            settlePoses.Clear();
            var registry = session != null ? session.Registry : null;
            if (session != null && registry != null)
            {
                foreach (var channel in session.Channels)
                {
                    if (!channel.Active || string.IsNullOrEmpty(channel.InputId)) continue;
                    // 止めている左の追加肢（トラッカーがない）は待たない
                    if (channel.Side == LimbSide.Left && !channel.IsInputValid) continue;
                    var pose = default(Pose);
                    bool tracked = registry.TryGet(channel.InputId, out var source) && source.TryGetPose(out pose);
                    settlePoses.Add((channel.InputId, tracked, pose));
                }
            }
            if (settledByHand || stillness.Update(settlePoses, Time.unscaledDeltaTime)) FinishSettle();
        }

        void FinishSettle()
        {
            bool byHand = settledByHand;
            notice?.Clear();
            using (session != null ? session.WithOrigin(ChangeOrigins.TrialStart) : default)
            {
                if (!byHand) session?.RequestRecenter();
                Report(SessionEventTypes.Settle, settlePractice ? "practice" : "condition", byHand ? "manual" : "still", "", "",
                    byHand ? "実験者が Re-center したので始めます" : "参加者が止まったので Re-center して始めます");
            }
            // ブロックの最初の開始では、休憩で座り直したのに合わせて円を置き直す
            if (!settlePractice && IsExperimentMode && Current != null && Current.block != ringBlock)
            {
                ResolveTask();
                task?.Relayout();
                ringBlock = Current.block;
            }
            BeginTrial(settlePractice);
        }

        void BeginTrial(bool isPractice)
        {
            practice = isPractice;
            if (practice) practiceCount++;
            else attempts[currentIndex]++;
            seg1Start = seg1End = seg2Start = seg2End = double.NaN;
            hits[0] = hits[1] = null;
            startedAt = Elapsed();
            matchesAtStart = practice || Matches(Current);
            drivenAtStart = DrivenLimbCount();
            ResolveNotice();
            ResolveTask();
            timeline.Start(Now, timing);
            if (practice)
                Report(SessionEventTypes.TrialStart, "practice", "", "", Int(practiceCount), $"練習を始めました（{practiceCount}回目）");
            else
                Report(SessionEventTypes.TrialStart, "condition", Number(currentIndex), "", Int(attempts[currentIndex]),
                    $"条件 {currentIndex + 1} の課題を始めました（{attempts[currentIndex]}回目）" + (matchesAtStart ? "" : "。設定が条件と違います"));
            SetPhase(TrialPhase.Lead);
            ProcessCues();
            Say("");
        }

        /// <summary>課題を中止する（Esc）。同じ条件の始め（準備）に戻る。評価の入力中なら評価を捨てる。開始の前の待ちならやめるだけ。</summary>
        public bool Abort()
        {
            if (phase == TrialPhase.Settle)
            {
                notice?.Clear();
                Report(SessionEventTypes.Settle, settlePractice ? "practice" : "condition", "abort", "", "", "開始の前の待ちをやめました");
                SetPhase(settlePractice ? phaseBeforePractice : TrialPhase.Ready);
                Say("開始をやめました");
                return true;
            }
            if (IsRunning)
            {
                var was = phase;
                timeline.Stop();
                task?.CancelSegment();
                notice?.Show(abortText, 2f);
                Report(SessionEventTypes.TrialAbort, practice ? "practice" : "condition", practice ? "" : Number(currentIndex), was.ToString(), "",
                    practice ? "練習を中止しました" : $"条件 {currentIndex + 1} の課題を中止しました（{PhaseLabel(was)}）");
                WriteRow(practice ? "practice_aborted" : "aborted");
                if (practice)
                {
                    practice = false;
                    SetPhase(phaseBeforePractice);
                    Say("練習を中止しました");
                }
                else
                {
                    if (StatusOf(currentIndex) != ConditionStatus.Done) statuses[currentIndex] = ConditionStatus.Aborted;
                    SetPhase(TrialPhase.Ready);
                    Say("中止しました");
                }
                return true;
            }
            if (phase == TrialPhase.Rating)
            {
                Report(SessionEventTypes.TrialAbort, "condition", Number(currentIndex), phase.ToString(), "", $"条件 {currentIndex + 1} の口頭評価をやめました");
                WriteRow("aborted");
                if (StatusOf(currentIndex) != ConditionStatus.Done) statuses[currentIndex] = ConditionStatus.Aborted;
                rating.Clear();
                SetPhase(TrialPhase.Ready);
                Say("評価を中止しました");
                return true;
            }
            return false;
        }

        void Update()
        {
            if (trackerCheck.IsActive) UpdateTrackerCheck();
            if (phase == TrialPhase.Settle) UpdateSettle();
            if (timeline.Running) ProcessCues();
        }

        void ProcessCues()
        {
            cueBuffer.Clear();
            timeline.Advance(Now, cueBuffer);
            if (cueBuffer.Count == 0) return;
            using (session != null ? session.WithOrigin(ChangeOrigins.System) : default)
            {
                foreach (var cue in cueBuffer) HandleCue(cue);
            }
        }

        void HandleCue(TrialCue cue)
        {
            string subject = practice ? "practice" : "condition";
            switch (cue)
            {
                case TrialCue.LeadMessage:
                    notice?.Show(segment1Text, timing.leadMessageSeconds);
                    break;
                case TrialCue.Countdown1:
                case TrialCue.Countdown2:
                    notice?.Countdown(timing.countdown, startText);
                    break;
                case TrialCue.Segment1Start:
                    seg1Start = Elapsed();
                    SetPhase(TrialPhase.Segment1);
                    task?.BeginSegment(1, false);
                    Report(SessionEventTypes.SegmentStart, subject, "1", "", "", "前半（追加肢だけ）を始めました");
                    SegmentStarted?.Invoke(1, false);
                    break;
                case TrialCue.Segment1End:
                    seg1End = Elapsed();
                    hits[0] = task?.EndSegment(1);
                    Report(SessionEventTypes.SegmentEnd, subject, "1", "", hits[0].HasValue ? Int(hits[0].Value) : "",
                        "前半を終えました" + (hits[0].HasValue ? $"（到達 {hits[0].Value}）" : ""));
                    SegmentEnded?.Invoke(1);
                    SetPhase(TrialPhase.Interlude);
                    break;
                case TrialCue.InterludeMessage:
                    notice?.Show(segment2Text, timing.interludeMessageSeconds);
                    break;
                case TrialCue.Segment2Start:
                    seg2Start = Elapsed();
                    SetPhase(TrialPhase.Segment2);
                    task?.BeginSegment(2, true);
                    Report(SessionEventTypes.SegmentStart, subject, "2", "", "", "後半（本来の腕も使う）を始めました");
                    SegmentStarted?.Invoke(2, true);
                    break;
                case TrialCue.Segment2End:
                    seg2End = Elapsed();
                    hits[1] = task?.EndSegment(2);
                    Report(SessionEventTypes.SegmentEnd, subject, "2", "", hits[1].HasValue ? Int(hits[1].Value) : "",
                        "後半を終えました" + (hits[1].HasValue ? $"（到達 {hits[1].Value}）" : ""));
                    SegmentEnded?.Invoke(2);
                    notice?.Show(endText, 2f);
                    FinishTrial();
                    break;
            }
        }

        void FinishTrial()
        {
            if (practice)
            {
                WriteRow("practice");
                practice = false;
                SetPhase(phaseBeforePractice);
                Say("練習が終わりました");
                return;
            }
            rating.Clear();
            SetPhase(TrialPhase.Rating);
            Say("");
        }

        // ---- 口頭評価 ----

        /// <summary>次の項目に評価（1〜7）を入れる（数字キー）。3項目そろったら条件を終える。</summary>
        public bool EnterRating(int value)
        {
            if (phase != TrialPhase.Rating)
                return Fail(phase == TrialPhase.Done || phase == TrialPhase.Break || phase == TrialPhase.Finished
                    ? "評価を直すときは、パネルの数字のボタンを押してください"
                    : "今は評価の入力中ではありません");
            int item = rating.Enter(value);
            if (item < 0) return false;
            AfterRating(item);
            return true;
        }

        /// <summary>
        /// 項目 item に評価を入れる（パネル）。評価の入力中ならそのまま入れ、条件を終えた後なら直して conditions.csv に1行足す（status＝corrected）。
        /// </summary>
        public bool SetRating(int item, int value)
        {
            if (phase == TrialPhase.Rating)
            {
                if (!rating.Set(item, value)) return false;
                AfterRating(item);
                return true;
            }
            if ((phase == TrialPhase.Done || phase == TrialPhase.Break || phase == TrialPhase.Finished) && StatusOf(currentIndex) == ConditionStatus.Done)
            {
                int old = rating[item];
                if (old == value || !rating.Set(item, value)) return false;
                Report(SessionEventTypes.Rating, "condition", ConditionRating.ItemKeys[item], old > 0 ? Int(old) : "", Int(value),
                    $"条件 {currentIndex + 1} の「{ConditionRating.ItemNames[item]}」を {value} に直しました");
                WriteRow("corrected");
                Say($"「{ConditionRating.ItemNames[item]}」を {value} に直しました");
                return true;
            }
            return Fail("今は評価を入れられません");
        }

        void AfterRating(int item)
        {
            Report(SessionEventTypes.Rating, "condition", ConditionRating.ItemKeys[item], "", Int(rating[item]),
                $"条件 {currentIndex + 1}：「{ConditionRating.ItemNames[item]}」{rating[item]}");
            if (rating.IsComplete)
            {
                CompleteCondition();
                return;
            }
            Say("");
        }

        void CompleteCondition()
        {
            var spec = Current;
            statuses[currentIndex] = ConditionStatus.Done;
            WriteRow("done");
            Report(SessionEventTypes.ConditionDone, "condition", Number(currentIndex), "", rating.ToString(),
                $"条件 {currentIndex + 1}/{plan.Count} を終えました（評価 {rating}）");
            bool last = currentIndex >= plan.Count - 1;
            if (last)
            {
                SetPhase(TrialPhase.Finished);
                notice?.Show(finishedText, 3f);
                int remaining = plan.Count - DoneCount;
                Report(SessionEventTypes.Finished, "session", "", "", Int(DoneCount), "最後の条件を終えました");
                Say(remaining == 0 ? "" : $"終えていない条件が {remaining} あります（提示順の一覧から選べます）");
            }
            else if (plan.conditions[currentIndex + 1].block != spec.block)
            {
                breakStartedAt = Time.unscaledTime;
                SetPhase(TrialPhase.Break);
                notice?.Show(breakText, 3f);
                Report(SessionEventTypes.Break, "session", Number(spec.block), "", "", $"ブロック {spec.block + 1}/{plan.BlockCount}（{spec.inputSiteName}）を終えました。休憩");
                Say("");
            }
            else
            {
                SetPhase(TrialPhase.Done);
                Say("");
            }
        }

        // ---- 記録 ----

        void WriteRow(string status)
        {
            if (recorder == null || !recorder.IsRecording) return;
            var spec = practice ? null : Current;
            var config = session != null && session.Primary != null ? session.Primary.Config : null;
            string actual = config != null ? $"{config.attachPointId}|{config.inputSite}|{config.mappingPresetId}" : "";
            row.Clear()
                .Add(plan?.participantId ?? "")
                .Add(plan != null ? Number(plan.latinRow) : "")
                .Add(spec != null ? Number(spec.order) : "practice")
                .Add(spec?.Code ?? "")
                .Add(spec != null ? Number(spec.block) : "")
                .Add(spec != null ? spec.inputSite.ToString() : "")
                .Add(spec != null ? Number(spec.indexInBlock) : "")
                .Add(spec != null ? spec.attachPointId : config?.attachPointId ?? "")
                .Add(spec != null ? spec.inputSite.ToString() : config?.inputSite.ToString() ?? "")
                .Add(spec != null ? spec.mappingPresetId : config?.mappingPresetId ?? "");
            if (config != null) row.Add(config.positionGain, 2);
            else row.AddEmpty();
            row.Add(matchesAtStart)
                .Add(spec != null ? AttemptsOf(currentIndex) : practiceCount)
                .Add(status)
                .Add(loadedAt, 3).Add(startedAt, 3)
                .Add(seg1Start, 3).Add(seg1End, 3).Add(seg2Start, 3).Add(seg2End, 3);
            AddCount(hits[0]);
            AddCount(hits[1]);
            for (int i = 0; i < rating.Count; i++) AddCount(rating[i] > 0 ? rating[i] : (int?)null);
            row.Add(recenterRequests)
                .Add(spec != null && !matchesAtStart ? $"設定が条件と違う：{actual}" : "")
                .Add(session != null && session.TwoLimbs ? 2 : 1)
                .Add(drivenAtStart);
            recorder.WriteTableRow(ConditionsFileName, ConditionColumns, row);
        }

        // トラッカーで動かしている追加肢の数（左のトラッカーがなく、左を止めていれば 1）
        int DrivenLimbCount()
        {
            int count = 0;
            if (session != null)
                foreach (var channel in session.Channels)
                    if (channel.Active && channel.IsInputValid) count++;
            return count;
        }

        void AddCount(int? value)
        {
            if (value.HasValue) row.Add(value.Value);
            else row.AddEmpty();
        }

        double Elapsed() => recorder != null && recorder.IsRecording ? recorder.ElapsedSeconds : double.NaN;

        void ResetTimes()
        {
            loadedAt = startedAt = double.NaN;
            seg1Start = seg1End = seg2Start = seg2End = double.NaN;
            hits[0] = hits[1] = null;
        }

        void Report(string type, string subject, string key, string oldValue, string newValue, string text) =>
            session?.Report(type, subject, key, oldValue, newValue, text);

        void SetPhase(TrialPhase next)
        {
            if (phase == next) return;
            phase = next;
            PhaseChanged?.Invoke(next);
        }

        void Say(string text) => message = text ?? "";

        bool Fail(string text)
        {
            Say(text);
            return false;
        }

        // 課題の時刻は実時間（記録の時刻と同じ時計。フレームの途中で呼ばれても古い時刻にならない）
        static double Now => Time.realtimeSinceStartupAsDouble;

        // 記録には 1 始まりの番号を書く
        static string Number(int zeroBased) => (zeroBased + 1).ToString(CultureInfo.InvariantCulture);

        static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
