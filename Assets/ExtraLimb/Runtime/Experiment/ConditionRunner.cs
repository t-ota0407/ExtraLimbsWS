using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ExtraLimb.Core;
using ExtraLimb.Limb;
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
    /// 実験モードに入ると記録を始め、2本目を隠して、読み込んである条件の設定に戻す。
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
            "seg1_hits", "seg2_hits", "rating_ease", "rating_body", "rating_fatigue", "recenter_requests", "note",
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

        /// <summary>課題の時間を進めている途中か（案内・カウントダウン・前半・後半）。</summary>
        public bool IsRunning => phase == TrialPhase.Lead || phase == TrialPhase.Segment1 || phase == TrialPhase.Interlude || phase == TrialPhase.Segment2;

        /// <summary>練習（今の設定で、提示順に数えない）の途中か。</summary>
        public bool IsPractice => practice;

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
                case TrialPhase.Ready: return "準備（Re-center → 動かし方を伝える → 開始）";
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
            if (e.type == SessionEventTypes.RecenterRequest) recenterRequests++;
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

        // 実験モードに入ったとき：使うのは右の1本だけ。準備中の条件があれば、その設定に戻す（デモで変えたものを戻す）
        void RestoreConditionSettings()
        {
            if (session == null) return;
            if (Current != null && phase == TrialPhase.Ready && !Matches(Current))
            {
                ApplySpec(Current);
                return;
            }
            if (session.TwoLimbs) session.SetTwoLimbs(false);
            session.SelectSide(LimbSide.Right);
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
            string siteName = BodySiteNames.Japanese(site);
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
                if (preset != null) options.Add(new ConditionSequence.Option(preset.id, preset.displayName));
                return options;
            }, row);
            if (newPlan.Count == 0)
            {
                Say("条件がありません（生やす位置・入力部位・マッピングの型を確かめてください）");
                return false;
            }

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
                parts.Add($"{BodySiteNames.Japanese(site)}＝{(preset != null ? MappingPresets.ShortName(preset) : "なし")}");
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
            if (session.TwoLimbs) session.SetTwoLimbs(false);
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
        }

        bool Matches(ConditionSpec spec)
        {
            var channel = session != null ? session.Primary : null;
            if (spec == null || channel == null || session.TwoLimbs) return false;
            var config = channel.Config;
            return config.attachPointId == spec.attachPointId && config.inputSite == spec.inputSite
                   && string.IsNullOrEmpty(config.inputSourceId) && config.mappingPresetId == spec.mappingPresetId;
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

        /// <summary>読み込んだ条件の課題を始める（Enter）：案内 → カウントダウン → 前半 → 案内 → カウントダウン → 後半。</summary>
        public bool StartTrial()
        {
            if (!RequireExperiment()) return false;
            if (phase != TrialPhase.Ready)
                return Fail(phase == TrialPhase.Idle ? "先に条件を読み込んでください（次へ：PgDn）" :
                    IsRunning ? "課題の途中です" : "この条件は終わっています（やり直しは R、次へは PgDn）");
            if (Current == null) return Fail("条件を読み込んでいません");
            BeginTrial(false);
            return true;
        }

        /// <summary>今の設定のまま、同じ流れで練習する（提示順には数えない。評価は取らない）。</summary>
        public bool StartPractice()
        {
            if (IsRunning) return Fail("課題の途中です");
            if (phase == TrialPhase.Rating) return Fail("口頭評価を入れてから");
            phaseBeforePractice = phase;
            BeginTrial(true);
            return true;
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

        /// <summary>課題を中止する（Esc）。同じ条件の始め（準備）に戻る。評価の入力中なら評価を捨てる。</summary>
        public bool Abort()
        {
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
                .Add(spec != null && !matchesAtStart ? $"設定が条件と違う：{actual}" : "");
            recorder.WriteTableRow(ConditionsFileName, ConditionColumns, row);
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
