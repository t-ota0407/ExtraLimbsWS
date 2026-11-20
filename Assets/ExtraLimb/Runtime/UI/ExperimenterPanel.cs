using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ExtraLimb.Body;
using ExtraLimb.Core;
using ExtraLimb.Experiment;
using ExtraLimb.Limb;
using ExtraLimb.Tracking;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtraLimb.UI
{
    /// <summary>
    /// 実験者パネル。UI Toolkit で、モニタの右にパネル、左上に大きな文字の表示を出す。パネルは画面（モニタ）にだけ描かれ、HMD には映らない。
    /// <list type="bullet">
    /// <item>左上の表示：条件と段階・残り時間（大きな数字と進み具合の線）、動かしている追加肢の設定（生やす位置｜入力の部位｜型｜ゲイン）と追跡が切れたときの札（赤）、
    /// 記録の状態、機器の異常（ITrackerProviderStatus のうち知らせることがあるものだけ）。</item>
    /// <item>パネルの上で、デモと実験を切り替える（WorkshopMode）。デモでは「デモ」の欄（今の設定のままの練習、生やす位置 × 入力部位 × 型の全組み合わせの一覧、
    /// 実験で使う型の選択）を、実験では「条件の進行」の欄を出す。実験モードでは、条件で決まる設定を変えるボタンを押せなくする。</item>
    /// <item>パネル：使う順に、参加者（ID・提示順・身長と肩幅）、デモまたは条件の進行（読み込み・課題・口頭評価）、リーチング課題、追加肢、記録。
    /// たまに使う欄（表示、HMD へのメッセージ、入力の詳細、ログの再生、動作確認用のボタン、ホットキー）はたたんでおく。</item>
    /// </list>
    /// 実験者が読むので、手順に要らない情報（実装のステップ、クラス名、内部の値）は出さない。ボタンのキーは小さく添える。
    /// 選ぶもの（生やす位置・入力の部位・型・評価）はつながった切り替えボタンにし、次に押すボタン（Re-center → 開始 → 次へ）だけを塗る。
    /// ボタンからの操作は LimbSession などの公開のメソッドを呼び、出どころを panel として記録に残す。
    /// 字は Windows の日本語フォントを使う（UIFonts）。見た目は Resources/ExtraLimbUI/ExperimenterPanel.uss。
    /// </summary>
    [DefaultExecutionOrder(10060)]
    [RequireComponent(typeof(UIDocument))]
    public class ExperimenterPanel : MonoBehaviour
    {
        /// <summary>見た目（USS）を Resources から読むときのパス。</summary>
        public const string StyleResourcePath = "ExtraLimbUI/ExperimenterPanel";

        [Tooltip("空ならシーンから探す")]
        [SerializeField] LimbSession session;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] ExperimentRecorder recorder;
        [Tooltip("空ならシーンから探す（なければ「動作確認用のボタン」の欄を出さない）")]
        [SerializeField] PracticeButtonBoard practice;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] AvatarMirror mirror;
        [Tooltip("空なら同じ GameObject かシーンから探す")]
        [SerializeField] HotkeyMap hotkeys;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] SpectatorCamera spectator;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] DebugOverlay overlay;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] HmdNotice notice;
        [Tooltip("空ならシーンから探す（なければ「条件の進行」の欄を出さない）")]
        [SerializeField] ConditionRunner runner;
        [Tooltip("空ならシーンから探す（なければ「ログの再生」の欄を出さない）")]
        [SerializeField] LogReplayer replayer;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] ReachTask reach;
        [Tooltip("見た目。空なら Resources の StyleResourcePath から読む")]
        [SerializeField] StyleSheet styleSheet;
        [Tooltip("パネルを出すか（ホットキー H で切り替える）。左上の大きな表示はいつも出す")]
        [SerializeField] bool visible = true;
        [Tooltip("表示を更新する間隔（秒）")]
        [SerializeField] float refreshInterval = 0.066f;

        static readonly string[] HandAxisNames = { "右", "上", "前" };
        const string SelectedClass = "el-button--selected";
        const string MissingClass = "el-button--missing";
        const string PrimaryClass = "el-button--primary";
        // リッチテキストに使う色（USS の色にそろえる）
        const string LiveHex = "#5CC2BD", CautionHex = "#E0B15C", DangerHex = "#E8665C";

        UIDocument document;
        VisualElement root, panel, banner;
        bool built;
        float nextRefresh;

        // 左上の表示
        VisualElement bannerHeadline, bannerProgress, bannerProgressFill, bannerDevices;
        Label bannerMeta, bannerPhase, bannerTimer, bannerSub;
        readonly List<(VisualElement row, Label text, Label pill)> bannerLimbs = new List<(VisualElement, Label, Label)>();
        readonly List<Label> bannerDeviceLines = new List<Label>();
        readonly List<ITrackerProviderStatus> providers = new List<ITrackerProviderStatus>();
        TrackerMonitor monitor;

        // モード
        Button demoModeButton, experimentModeButton;

        // 参加者
        TextField participantField, rowField, heightField, shoulderField, handLengthField;
        Label planLabel, designLabel, trackerPlanLabel, checklistLabel, participantMessage, bodyMessage;
        Button createPlanButton;
        VisualElement idRow;

        // デモ（今の設定のままの練習、組み合わせの一覧、実験で使う型）
        Foldout demoSection;
        Label demoPhaseLabel, demoMessage;
        Button demoStartButton, demoAbortButton;
        readonly List<(Button button, int pointIndex, string pointId, BodySite site, MappingPreset preset)> comboButtons =
            new List<(Button, int, string, BodySite, MappingPreset)>();
        readonly List<(Button button, BodySite site, MappingPreset preset)> designChips = new List<(Button, BodySite, MappingPreset)>();

        // 条件の進行
        Foldout runnerSection;
        Label conditionLabel, conditionInstruction, phaseLabel, recenterLabel, runnerMessage;
        Button startButton, abortButton, redoButton, prevButton, nextButton, practiceButton;
        readonly Label[] ratingLabels = new Label[3];
        readonly Button[,] ratingButtons = new Button[3, ConditionRating.Max];
        Button recenterButton, trackerCheckButton, trackerCheckSkipLeftButton, trackerCheckCancelButton;
        Label trackerCheckLabel, trackerCheckList, startBlockedLabel, layoutWarningLabel, leftLimbNoteLabel;
        Foldout sequenceSection;
        VisualElement sequenceList;
        ConditionPlan listedPlan;
        readonly List<Button> sequenceButtons = new List<Button>();

        // リーチング課題
        Foldout reachSection;
        Label reachRingLabel, reachReachableLabel, reachLiveLabel, reachMessageLabel;
        Button reachPlaceButton;
        Toggle reachIdleToggle, reachProbesToggle, practiceVisibleToggle;

        // 追加肢
        Foldout limbSection;
        VisualElement sideRow;
        Button rightButton, leftButton, twoLimbsButton, trackerListButton;
        Label attachState;
        readonly List<(Button button, AttachPointDefinition point)> attachButtons = new List<(Button, AttachPointDefinition)>();
        readonly List<(Button button, BodySite site)> siteButtons = new List<(Button, BodySite)>();
        Label channelStatus, assignHint, assignMessage;
        Button assignButton;
        VisualElement presetRow;
        readonly List<(Button button, MappingPreset preset)> presetButtons = new List<(Button, MappingPreset)>();
        BodySite? presetSite;
        Label axesLabel, gainLabel, forwardLabel, forwardMessage, limbLayoutWarning, limbLockNote;
        readonly Toggle[] invertToggles = new Toggle[3];
        // 実験モードでは押せない（条件で決まる設定を変える）ボタン
        readonly List<VisualElement> lockables = new List<VisualElement>();

        // 入力の詳細
        Foldout detailSection;
        Label inputLabel, referenceLabel, uLabel, instructionLabel, leverLabel, graspLabel;
        Button referenceButton;
        readonly Label[,] channelCells = new Label[6, 5];

        // 記録
        Label recordStatus, recordFolder, recordMessage;
        Button recordButton;

        // 表示・HMD へのメッセージ
        Toggle markerToggle, mirrorToggle, overlayToggle, overlayHmdToggle, autoForwardToggle;
        Label viewLabel, noticeLabel;
        TextField noticeField;

        // ログの再生
        Foldout replaySection;
        Label replayFolderLabel, replayStatusLabel, replayErrorLabel;
        Button replayPlayButton;
        readonly List<(Button button, float speed)> replaySpeedButtons = new List<(Button, float)>();

        // 動作確認用のボタン・ホットキー
        Label practiceLabel;
        VisualElement hotkeyList;

        /// <summary>パネルを出しているか（左上の大きな表示はいつも出す）。</summary>
        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
                if (panel != null) panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>パネルの文字の欄（身長など）に入力しているか。この間はホットキーを無視する。</summary>
        public bool IsTypingText
        {
            get
            {
                var focused = root?.panel?.focusController?.focusedElement as VisualElement;
                return focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);
            }
        }

        void OnEnable()
        {
            document = GetComponent<UIDocument>();
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            if (recorder == null) recorder = FindFirstObjectByType<ExperimentRecorder>();
            if (practice == null) practice = FindFirstObjectByType<PracticeButtonBoard>();
            if (mirror == null) mirror = FindFirstObjectByType<AvatarMirror>();
            if (hotkeys == null) hotkeys = GetComponent<HotkeyMap>();
            if (hotkeys == null) hotkeys = FindFirstObjectByType<HotkeyMap>();
            if (spectator == null) spectator = FindFirstObjectByType<SpectatorCamera>();
            if (overlay == null) overlay = FindFirstObjectByType<DebugOverlay>();
            if (notice == null) notice = FindFirstObjectByType<HmdNotice>();
            if (runner == null) runner = FindFirstObjectByType<ConditionRunner>();
            if (replayer == null) replayer = FindFirstObjectByType<LogReplayer>();
            if (reach == null) reach = FindFirstObjectByType<ReachTask>();
            monitor = FindFirstObjectByType<TrackerMonitor>();
            if (styleSheet == null) styleSheet = Resources.Load<StyleSheet>(StyleResourcePath);
            // UIDocument を入れ直すと要素が作り直されるので、次の Update で組み立て直す
            built = false;
        }

        void Update()
        {
            if (!built) TryBuild();
            // 第三者視点はパネルの左に描く
            if (spectator != null) spectator.RightMarginPixels = built && visible && panel != null ? PanelWidth() : 0f;
            if (!built || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            Refresh();
        }

        float PanelWidth()
        {
            float width = panel.resolvedStyle.width;
            return float.IsNaN(width) || width <= 0f ? 448f : width;
        }

        // ---- 組み立て ----

        void TryBuild()
        {
            if (document == null || document.rootVisualElement == null) return;
            // 追加肢の準備（LimbSession.Start）ができてから組み立てる
            if (session != null && session.Channels.Count == 0 && Time.frameCount < 300) return;

            var documentRoot = document.rootVisualElement;
            documentRoot.Clear();
            if (styleSheet != null && !documentRoot.styleSheets.Contains(styleSheet)) documentRoot.styleSheets.Add(styleSheet);
            UIFonts.ApplyJapanese(documentRoot);
            root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList("el-root");
            root.style.position = Position.Absolute;
            root.style.left = root.style.right = root.style.top = root.style.bottom = 0;
            documentRoot.Add(root);

            providers.Clear();
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour is ITrackerProviderStatus status) providers.Add(status);
            // 機器の状態は左上の表示に出すので、モニタは一覧を開いたときだけ描く
            if (monitor != null) monitor.StatusShownElsewhere = true;

            BuildBanner();
            BuildPanel();
            Visible = visible;
            built = true;
            Refresh();
        }

        // 左上の表示：条件と段階・残り時間（大きく）、進み具合の線、追加肢の設定、記録の状態、機器の異常
        void BuildBanner()
        {
            banner = Box("el-banner", root);
            bannerMeta = BannerLabel("el-banner__meta", banner);
            bannerHeadline = Box("el-banner__headline", banner);
            bannerPhase = BannerLabel("el-banner__phase", bannerHeadline);
            bannerTimer = BannerLabel("el-banner__timer", bannerHeadline);
            bannerProgress = Box("el-banner__progress", banner);
            bannerProgressFill = Box("el-banner__progress-fill", bannerProgress);
            bannerLimbs.Clear();
            for (int i = 0; i < 2; i++)
            {
                var row = Box("el-banner__limb", banner);
                var text = BannerLabel("el-banner__limb-text", row);
                var pill = BannerLabel("el-pill", row);
                pill.AddToClassList("el-pill--danger");
                pill.text = "追跡切れ";
                bannerLimbs.Add((row, text, pill));
            }
            bannerSub = BannerLabel("el-banner__status", banner);
            bannerDevices = Box("el-banner__devices", banner);
            bannerDeviceLines.Clear();
        }

        static VisualElement Box(string className, VisualElement parent)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            parent.Add(element);
            return element;
        }

        static Label BannerLabel(string className, VisualElement parent)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        void BuildPanel()
        {
            panel = new VisualElement();
            panel.AddToClassList("el-panel");
            var header = new VisualElement();
            header.AddToClassList("el-panel__header");
            var title = new Label("実験者パネル");
            title.AddToClassList("el-panel__title");
            header.Add(title);
            if (runner != null)
            {
                // デモ／実験の切り替え
                var modes = Segmented(header);
                modes.AddToClassList("el-segmented--mode");
                demoModeButton = Btn(modes, "デモ", () => runner.SetMode(WorkshopMode.Demo));
                experimentModeButton = Btn(modes, "実験", () => runner.SetMode(WorkshopMode.Experiment));
            }
            var hide = new Label("H で隠す");
            hide.AddToClassList("el-panel__hint");
            header.Add(hide);
            panel.Add(header);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.style.flexGrow = 1;
            panel.Add(scroll);
            root.Add(panel);

            if (session == null)
            {
                scroll.Add(Text("追加肢の設定が見つかりません（シーンに LimbSession がありません）", "el-warn"));
                return;
            }

            lockables.Clear();
            BuildParticipantSection(scroll);
            BuildDemoSection(scroll);
            BuildRunnerSection(scroll);
            BuildReachSection(scroll);
            BuildLimbSection(scroll);
            BuildRecordSection(scroll);
            BuildDisplaySection(scroll);
            BuildNoticeSection(scroll);
            BuildDetailSection(scroll);
            BuildReplaySection(scroll);
            BuildPracticeSection(scroll);
            BuildHotkeySection(scroll);
        }

        void BuildParticipantSection(VisualElement parent)
        {
            var section = Section(parent, "参加者", true);
            if (runner != null)
            {
                idRow = Row(section);
                Text("参加者ID", "el-field-label", idRow);
                participantField = Field(idRow, 90);
                Text("順序", "el-field-label", idRow);
                rowField = Field(idRow, 60);
                rowField.textEdition.placeholder = "自動";
                createPlanButton = Btn(idRow, "提示順を作成", CreatePlan);
                participantMessage = Text("", "el-caution", section);
                planLabel = Text("", "el-muted", section);
                trackerPlanLabel = Text("", "el-muted", section);
                // 準備のチェックリスト（2026-11-15）：済んでいないものがあると開始できない
                checklistLabel = Text("", null, section);
                designLabel = Text("", "el-muted", section);
            }

            var bodyRow = Row(section);
            Text("身長", "el-field-label", bodyRow);
            heightField = Field(bodyRow, 52);
            Text("cm", "el-unit", bodyRow);
            Text("肩幅", "el-field-label", bodyRow);
            shoulderField = Field(bodyRow, 52);
            Text("cm", "el-unit", bodyRow);
            // 手の長さ（手首のしわから中指の先まで。本来の手と追加肢の手の両方。2026-11-13）
            var handRow = Row(section);
            Text("手の長さ", "el-field-label", handRow);
            handLengthField = Field(handRow, 52);
            Text("cm", "el-unit", handRow);
            Btn(handRow, "反映", ApplyBodyDimensions);
            Btn(handRow, "初期値", () =>
            {
                session.ResetBodyDimensions();
                FillBodyFields();
            });
            Text("手の長さは手首のしわから中指の先まで（本来の手と追加肢の手に使う）", "el-muted", section);
            bodyMessage = Text("", "el-muted", section);
            FillBodyFields();
        }

        void CreatePlan()
        {
            int? row = null;
            string text = rowField.value != null ? rowField.value.Trim() : "";
            if (text.Length > 0)
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < 1)
                {
                    ShowText(participantMessage, "順序は 1 以上の数字を入れてください（空欄なら参加者IDから自動で決まります）");
                    return;
                }
                row = number - 1;
            }
            ShowText(participantMessage, "");
            runner.CreatePlan(participantField.value, row);
        }

        // デモ：今の設定のまま練習する。生やす位置 × 入力部位 × 型の全組み合わせを一覧から選び、実験で使う型（部位ごとに1つ）も選ぶ
        void BuildDemoSection(VisualElement parent)
        {
            if (runner == null) return;
            demoSection = Section(parent, "デモ", true);
            demoPhaseLabel = Text("", "el-phase", demoSection);
            var row = Row(demoSection);
            Btn(row, Key("Re-center", "Space"), () => session.RequestRecenter(), true);
            demoStartButton = Btn(row, Key("課題を開始", "Enter"), () => runner.StartPractice(), true);
            demoAbortButton = Btn(row, Key("中止", "Esc"), () => runner.Abort(), true);
            demoAbortButton.AddToClassList("el-button--danger");
            demoMessage = Text("", "el-caution", demoSection);

            SubHeader(demoSection, "組み合わせ", "押すとその組み合わせになります");
            comboButtons.Clear();
            designChips.Clear();
            var mount = session.Primary?.Mount;
            if (mount == null)
            {
                Text("生やす位置が設定されていません", "el-muted", demoSection);
                return;
            }
            var points = mount.Registry.Points;
            var head = new VisualElement();
            head.AddToClassList("el-combo-row");
            head.AddToClassList("el-combo-row--head");
            demoSection.Add(head);
            Text("入力の部位・型", "el-combo-label", head);
            foreach (var point in points)
                if (point != null) Text(point.displayName, "el-combo-head", head);
            Text("実験", "el-combo-head", head).AddToClassList("el-combo-chipcol");
            foreach (var site in session.InputSites)
            {
                bool first = true;
                foreach (var preset in MappingPresets.ForSite(session.MappingPresetList, site))
                {
                    var comboRow = new VisualElement();
                    comboRow.AddToClassList("el-combo-row");
                    if (first) comboRow.AddToClassList("el-combo-row--site");
                    demoSection.Add(comboRow);
                    var label = new VisualElement();
                    label.AddToClassList("el-combo-label");
                    comboRow.Add(label);
                    Text(first ? BodySiteNames.Kind(site) : "", "el-combo-site", label);
                    Text(MappingPresets.ShortName(preset), "el-combo-type", label);
                    for (int i = 0; i < points.Count; i++)
                    {
                        var point = points[i];
                        if (point == null) continue;
                        int pointIndex = i;
                        var targetSite = site;
                        var targetPreset = preset;
                        var cell = Btn(comboRow, "", () => ApplyCombo(pointIndex, targetSite, targetPreset));
                        cell.AddToClassList("el-combo-cell");
                        comboButtons.Add((cell, pointIndex, point.id, site, preset));
                    }
                    var designSite = site;
                    var designPreset = preset;
                    var chip = Btn(comboRow, "実験", () => runner.SetExperimentMapping(designSite, designPreset));
                    chip.AddToClassList("el-button--chip");
                    designChips.Add((chip, site, preset));
                    first = false;
                }
            }
            Text("2本のときは、右の追加肢を右の部位、左の追加肢を左の同じ部位で動かします（左は右の鏡写し）。「実験」の印は、実験モードでその入力部位に使う型です（部位ごとに1つ。押して選びます）", "el-muted", demoSection);
        }

        // 一覧の組み合わせにする：生やす位置（左右とも）、右の追加肢の入力の部位と型（ゲインは型の初期値）。2本のときは左を右の鏡写しにする
        void ApplyCombo(int pointIndex, BodySite site, MappingPreset preset)
        {
            session.SelectSide(LimbSide.Right);
            session.SelectAttachPoint(pointIndex);
            session.SelectInputSite(site);
            session.SelectMappingPreset(preset);
            if (session.TwoLimbs) session.MirrorLeftFromRight();
        }

        void BuildRunnerSection(VisualElement parent)
        {
            if (runner == null) return;
            var section = runnerSection = Section(parent, "条件の進行", true);
            conditionLabel = Text("", "el-condition", section);
            conditionInstruction = Text("", "el-muted", section);
            phaseLabel = Text("", "el-phase", section);
            recenterLabel = Text("", null, section);
            layoutWarningLabel = Text("", "el-warn", section);
            leftLimbNoteLabel = Text("", "el-caution", section);

            // トラッカーの確認（装着の後と、後半のブロックの前の付け替えの後。2026-11-15）
            trackerCheckLabel = Text("", "el-caution", section);
            trackerCheckList = Text("", null, section);
            var checkRow = Row(section);
            trackerCheckButton = Btn(checkRow, "トラッカーの確認を始める", () => runner.StartTrackerCheck(), true);
            trackerCheckSkipLeftButton = Btn(checkRow, "左はなし（右だけで行う）", () => runner.SkipLeftTrackers(), true);
            trackerCheckCancelButton = Btn(checkRow, "確認をやめる", () => runner.CancelTrackerCheck(), true);

            var main = Row(section);
            recenterButton = Btn(main, Key("Re-center", "Space"), () => session.RequestRecenter(), true);
            startButton = Btn(main, Key("開始", "Enter"), () => runner.StartTrial(), true);
            abortButton = Btn(main, Key("中止", "Esc"), () => runner.Abort(), true);
            abortButton.AddToClassList("el-button--danger");
            startBlockedLabel = Text("", "el-warn", section);
            var move = Row(section);
            prevButton = Btn(move, Key("前へ", "PgUp"), () => runner.Previous(), true);
            redoButton = Btn(move, Key("やり直し", "R"), () => runner.Redo(), true);
            nextButton = Btn(move, Key("次へ", "PgDn"), () => runner.Next(), true);
            var extra = Row(section);
            practiceButton = Btn(extra, "練習（記録上は条件に数えない）", () => runner.StartPractice(), true);

            var ratingHeader = SubHeader(section, "口頭評価", "1〜7 キーで上から順に入力");
            for (int item = 0; item < ratingLabels.Length; item++)
            {
                var ratingRow = new VisualElement();
                ratingRow.AddToClassList("el-rating-row");
                section.Add(ratingRow);
                ratingLabels[item] = Text(ConditionRating.ItemNames[item], "el-rating-label", ratingRow);
                var scale = Segmented(ratingRow);
                scale.AddToClassList("el-segmented--compact");
                for (int value = ConditionRating.Min; value <= ConditionRating.Max; value++)
                {
                    int index = item, score = value;
                    ratingButtons[item, value - 1] = Btn(scale, value.ToString(CultureInfo.InvariantCulture), () => runner.SetRating(index, score));
                }
            }
            runnerMessage = Text("", "el-caution", section);
            sequenceSection = Section(section, "提示順の一覧", false);
            sequenceSection.AddToClassList("el-section--sub");
            Text("押すとその条件を読み込みます", "el-muted", sequenceSection);
            sequenceList = new VisualElement();
            sequenceSection.Add(sequenceList);
        }

        void BuildReachSection(VisualElement parent)
        {
            if (reach == null) return;
            reachSection = Section(parent, "リーチング課題", true);
            reachRingLabel = Text("", null, reachSection);
            reachReachableLabel = Text("", null, reachSection);
            reachLiveLabel = Text("", null, reachSection);
            reachMessageLabel = Text("", "el-muted", reachSection);
            var row = Row(reachSection);
            reachPlaceButton = Btn(row, "今の姿勢で円を置き直す", () => reach.Place());
            reachProbesToggle = AddToggle(reachSection, "当たる範囲を表示（追加肢と本来の手の指先の球。参加者にも見える）", value => Act(() =>
            {
                reach.ShowProbes = value;
                session.Report(SessionEventTypes.ReachProbes, "session", "show", value ? "0" : "1", value ? "1" : "0",
                    value ? "当たる範囲を表示しました" : "当たる範囲を隠しました");
            }));
            reachIdleToggle = AddToggle(reachSection, "課題中以外もターゲットを表示", value => reach.ShowIdleTargets = value);
            if (practice != null) practiceVisibleToggle = AddToggle(reachSection, "動作確認用のボタンと立方体を表示", value => practice.Visible = value);
        }

        void BuildLimbSection(VisualElement parent)
        {
            limbSection = Section(parent, "追加肢", true);
            limbLockNote = Text("実験モードでは、条件で決まる設定（生やす位置・入力の部位・型・反転・ゲイン・2本目）は変えられません。変えるときはデモモードへ", "el-muted", limbSection);

            sideRow = Row(limbSection);
            var sides = Segmented(sideRow);
            rightButton = Btn(sides, Key("右", "→"), () => session.SelectSide(LimbSide.Right), true);
            leftButton = Btn(sides, Key("左", "←"), () => session.SelectSide(LimbSide.Left), true);
            twoLimbsButton = Btn(sideRow, "", () => session.SetTwoLimbs(!session.TwoLimbs));
            lockables.Add(twoLimbsButton);
            if (!session.HasLeftLimb) Show(sideRow, false);

            var attachHeader = SubHeader(limbSection, "生やす位置", null);
            attachState = Text("", "el-muted", attachHeader);
            var attachRow = Segmented(limbSection);
            attachButtons.Clear();
            var mount = session.Primary?.Mount;
            if (mount != null)
            {
                var points = mount.Registry.Points;
                for (int i = 0; i < points.Count; i++)
                {
                    var point = points[i];
                    if (point == null) continue;
                    int index = i;
                    var button = Btn(attachRow, Key(point.displayName, $"F{i + 1}"), () => session.SelectAttachPoint(index), true);
                    attachButtons.Add((button, point));
                    lockables.Add(button);
                }
            }
            else
            {
                attachState.text = "固定";
            }

            var siteHeader = SubHeader(limbSection, "入力の部位", "Ctrl+1〜4");
            if (monitor != null)
            {
                trackerListButton = Btn(siteHeader, "", () => monitor.ListOpen = !monitor.ListOpen);
                trackerListButton.AddToClassList("el-button--link");
            }
            var siteRow = Segmented(limbSection);
            siteButtons.Clear();
            foreach (var site in session.InputSites)
            {
                var target = site;
                var button = Btn(siteRow, BodySiteNames.Japanese(site), () => session.SelectInputSite(target), true);
                siteButtons.Add((button, site));
                lockables.Add(button);
            }
            channelStatus = Text("", "el-warn", limbSection);
            assignHint = Text("", "el-muted", limbSection);
            assignButton = Btn(limbSection, "", () => session.AssignRecentlyMovedTracker());
            assignButton.AddToClassList("el-button--block");
            assignMessage = Text("", "el-muted", limbSection);

            SubHeader(limbSection, "マッピングの型", "P で切り替え");
            presetRow = Segmented(limbSection);
            axesLabel = Text("", "el-muted", limbSection);

            var adjust = Row(limbSection);
            Text("反転", "el-field-label", adjust);
            for (int axis = 0; axis < 3; axis++)
            {
                int index = axis;
                var toggle = new Toggle { text = HandAxisNames[axis], focusable = false };
                toggle.AddToClassList("el-toggle");
                toggle.RegisterValueChangedCallback(evt =>
                {
                    var channel = session.Selected;
                    if (channel != null && channel.Config.axisMapping.IsInverted(index) != evt.newValue) Act(() => session.ToggleInverted(index));
                });
                adjust.Add(toggle);
                invertToggles[axis] = toggle;
            }
            var gain = Row(limbSection);
            gainLabel = Text("", "el-field-label", gain);
            gainLabel.style.minWidth = 76;
            lockables.Add(Btn(gain, Key("−0.5", "↓"), () => ChangeGain(-0.5f)));
            lockables.Add(Btn(gain, Key("＋0.5", "↑"), () => ChangeGain(0.5f)));

            var forward = Row(limbSection);
            forward.AddToClassList("el-row--nowrap");
            forwardLabel = Text("", null, forward);
            forwardLabel.style.flexGrow = 1;
            forwardLabel.style.flexShrink = 1;
            Btn(forward, Key("前の向きを記録", "F"), () => session.RequestRecordForward());
            autoForwardToggle = AddToggle(limbSection, "Re-center のときに左右のトラッカーから前の向きを求める", value => Act(() => session.AutoForwardFromSides = value));
            forwardMessage = Text("", "el-muted", limbSection);
            limbLayoutWarning = Text("", "el-warn", limbSection);
        }

        void BuildRecordSection(VisualElement parent)
        {
            var section = Section(parent, "記録", true);
            if (recorder == null)
            {
                Text("記録の機能がありません（シーンに ExperimentRecorder がありません）", "el-warn", section);
                return;
            }
            recordStatus = Text("", null, section);
            recordFolder = Text("", "el-muted", section);
            var row = Row(section);
            recordButton = Btn(row, "", () =>
            {
                if (recorder.IsRecording) recorder.StopRecording(ChangeOrigins.Panel);
                else recorder.StartRecording(ChangeOrigins.Panel);
            }, true);
            Btn(row, Key("マーカー", "K"), () => recorder.InsertMarker(ChangeOrigins.Panel), true);
            Btn(row, Key("設定を保存", "Ctrl+S"), () => recorder.SaveCurrentConfig(ChangeOrigins.Panel), true);
            recordMessage = Text("", "el-muted", section);
        }

        void BuildDisplaySection(VisualElement parent)
        {
            var section = Section(parent, "表示", false);
            if (spectator != null)
            {
                var view = Row(section);
                viewLabel = Text("", null, view);
                viewLabel.style.flexGrow = 1;
                Btn(view, Key("視点を切り替え", "V"), () => spectator.NextView());
            }
            if (mirror != null)
            {
                mirrorToggle = AddToggle(section, "鏡を表示（M）", value =>
                {
                    if (mirror.Visible != value) Act(() => HotkeyMap.ToggleMirror(mirror, session));
                });
            }
            if (overlay != null) overlayToggle = AddToggle(section, "モニタに補助表示（基準点・可動範囲など）", value => overlay.Show = value);
            if (spectator != null)
            {
                overlayHmdToggle = AddToggle(section, "補助表示を参加者（HMD）にも見せる", value =>
                {
                    if (spectator.OverlayInHmd != value) Act(() => spectator.OverlayInHmd = value);
                });
            }
            markerToggle = AddToggle(section, "手首の目標位置を表示（モニタのみ）", value => session.ShowTargetMarker = value);
        }

        void BuildNoticeSection(VisualElement parent)
        {
            if (notice == null) return;
            var section = Section(parent, "HMD へのメッセージ", false);
            var custom = Row(section);
            noticeField = new TextField { value = "" };
            noticeField.AddToClassList("el-field");
            noticeField.style.flexGrow = 1;
            custom.Add(noticeField);
            Btn(custom, "表示", () =>
            {
                if (!string.IsNullOrWhiteSpace(noticeField.value)) notice.Show(noticeField.value.Trim());
            });
            var presets = Row(section);
            Btn(presets, "「条件が変わります」", () => notice.Show("条件が変わります"));
            Btn(presets, "カウントダウン", () => notice.Countdown());
            Btn(presets, "消去", () => notice.Clear());
            noticeLabel = Text("", "el-muted", section);
        }

        void BuildDetailSection(VisualElement parent)
        {
            detailSection = Section(parent, "入力の詳細", false);
            inputLabel = Text("", null, detailSection);
            var reference = Row(detailSection);
            referenceLabel = Text("", null, reference);
            referenceLabel.style.flexGrow = 1;
            referenceButton = Btn(reference, "基準を切り替え", () => session.CycleReference());
            lockables.Add(referenceButton);
            uLabel = Text("", null, detailSection);

            var header = new VisualElement();
            header.AddToClassList("el-table-row");
            AddCell(header, "入力の値", "el-col-name");
            AddCell(header, "現在", "el-col-num");
            AddCell(header, "最小", "el-col-num");
            AddCell(header, "最大", "el-col-num");
            AddCell(header, "", "el-col-unit");
            AddCell(header, "手先", null);
            detailSection.Add(header);
            for (int i = 0; i < InputChannelInfo.All.Length; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("el-table-row");
                channelCells[i, 0] = AddCell(row, InputChannelInfo.Describe(InputChannelInfo.All[i]), "el-col-name");
                channelCells[i, 1] = AddCell(row, "", "el-col-num");
                channelCells[i, 2] = AddCell(row, "", "el-col-num");
                channelCells[i, 3] = AddCell(row, "", "el-col-num");
                AddCell(row, InputChannelInfo.All[i].IsRotation() ? "°" : "cm", "el-col-unit");
                channelCells[i, 4] = AddCell(row, "", null);
                detailSection.Add(row);
            }
            Text("値は Re-center した時点からの変化", "el-muted", detailSection);
            instructionLabel = Text("", "el-muted", detailSection);
            leverLabel = Text("", "el-muted", detailSection);
            Btn(detailSection, "最小・最大をリセット", () => session.Selected?.ResetChannelRange()).AddToClassList("el-button--block");
            SubHeader(detailSection, "把持", "追加肢はトリガー、本来の手はグリップ（G は予備）");
            graspLabel = Text("", null, detailSection);
        }

        void BuildReplaySection(VisualElement parent)
        {
            if (replayer == null) return;
            replaySection = Section(parent, "ログの再生", false);
            replayFolderLabel = Text("", null, replaySection);
            var folderRow = Row(replaySection);
            Btn(folderRow, "新しい記録へ", () => replayer.SelectFolder(-1));
            Btn(folderRow, "古い記録へ", () => replayer.SelectFolder(1));
            Btn(folderRow, "一覧を更新", () => replayer.RefreshFolders());
            Btn(folderRow, "読み込み", () => replayer.BeginLoad(), true);
            replayStatusLabel = Text("", null, replaySection);
            var playRow = Row(replaySection);
            replayPlayButton = Btn(playRow, "再生", () => replayer.TogglePlay(), true);
            Btn(playRow, "−10秒", () => replayer.Skip(-10));
            Btn(playRow, "＋10秒", () => replayer.Skip(10));
            Btn(playRow, "終了（実機に戻す）", () => replayer.Stop());
            var speedRow = Row(replaySection);
            Text("速さ", "el-field-label", speedRow);
            var speeds = Segmented(speedRow);
            replaySpeedButtons.Clear();
            foreach (float value in new[] { 0.5f, 1f, 2f, 4f })
            {
                float speed = value;
                replaySpeedButtons.Add((Btn(speeds, $"×{value.ToString("0.#", CultureInfo.InvariantCulture)}", () => replayer.Speed = speed), speed));
            }
            replayErrorLabel = Text("", "el-muted", replaySection);
            Text("再生中は、記録したトラッカーの動き・設定・Re-center で追加肢を動かします。終了すると実機に戻ります（設定は再生の最後のまま）", "el-muted", replaySection);
        }

        void BuildPracticeSection(VisualElement parent)
        {
            if (practice == null) return;
            var section = Section(parent, "動作確認用のボタン", false);
            practiceLabel = Text("", null, section);
            var row = Row(section);
            Btn(row, "Re-center して並べ直す", () => session.RequestRecenter());
            Btn(row, "立方体を元の位置へ", () => practice.ResetCubes());
            Btn(row, "回数をリセット", () => practice.ResetCounts());
        }

        void BuildHotkeySection(VisualElement parent)
        {
            var section = Section(parent, "ホットキー", false);
            hotkeyList = new VisualElement();
            section.Add(hotkeyList);
            Text("実験モードでは、生やす位置・入力の部位・型・ゲイン・2本目のキーは効きません。デモモードの Enter は練習の開始です", "el-muted", section);
            Text("Unity エディタで使うときは、Game ビューの「Unity Shortcuts」をオフにしてください", "el-muted", section);
            FillHotkeys();
        }

        void FillHotkeys()
        {
            if (hotkeyList == null) return;
            hotkeyList.Clear();
            if (hotkeys == null)
            {
                hotkeyList.Add(Text("ホットキーが設定されていません", "el-warn"));
                return;
            }
            foreach (var entry in hotkeys.Entries())
            {
                var row = new VisualElement();
                row.AddToClassList("el-hotkey-row");
                Text(entry.action == "Rating" ? "1〜7" : ShortKeys(entry.keys), "el-hotkey-key", row);
                var description = Text(entry.description, null, row);
                description.style.flexShrink = 1;
                hotkeyList.Add(row);
            }
        }

        // 「Enter | Num Enter」のような表記を短くする（テンキーは省く）
        static string ShortKeys(string keys)
        {
            var parts = new List<string>();
            foreach (var raw in (keys ?? "").Split('|'))
            {
                string key = raw.Trim();
                if (key.Length == 0 || key.StartsWith("Num ", StringComparison.Ordinal)) continue;
                key = key.Replace("Control+", "Ctrl+").Replace("Page Down", "PgDn").Replace("Page Up", "PgUp")
                    .Replace("Escape", "Esc").Replace("Right Arrow", "→").Replace("Left Arrow", "←").Replace("Up Arrow", "↑").Replace("Down Arrow", "↓");
                if (key == "Right") key = "→";
                else if (key == "Left") key = "←";
                else if (key == "Up") key = "↑";
                else if (key == "Down") key = "↓";
                parts.Add(key);
            }
            return string.Join(" / ", parts);
        }

        void FillBodyFields()
        {
            var body = session.Body;
            if (body == null || !body.IsReady || heightField == null) return;
            heightField.SetValueWithoutNotify((body.Calibration.Height * 100f).ToString("F0", CultureInfo.InvariantCulture));
            shoulderField.SetValueWithoutNotify((body.Calibration.ShoulderWidth * 100f).ToString("F0", CultureInfo.InvariantCulture));
            handLengthField.SetValueWithoutNotify((body.Calibration.HandLength * 100f).ToString("0.#", CultureInfo.InvariantCulture));
        }

        void ApplyBodyDimensions()
        {
            if (TryParse(heightField.value, out float height) && TryParse(shoulderField.value, out float shoulder) && TryParse(handLengthField.value, out float hand))
                session.ApplyBodyDimensions(height, shoulder, hand);
            else
                bodyMessage.text = "数字を入れてください";
        }

        void ChangeGain(float delta)
        {
            var channel = session.Selected;
            if (channel != null) session.SetPositionGain(channel.Config.positionGain + delta);
        }

        static bool TryParse(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.TryParse(text, out value);

        // ---- 部品 ----

        /// <summary>ボタンの文字にキーを小さく添える。</summary>
        static string Key(string text, string key) => $"{text}  <size=11><alpha=#80>{key}";

        // 欄の中の小見出し。右に操作キーなどを小さく添える。返す行の右に小さなボタンを足せる
        static VisualElement SubHeader(VisualElement parent, string title, string hint)
        {
            var header = new VisualElement();
            header.AddToClassList("el-subheader");
            parent.Add(header);
            Text(title, "el-subheading", header);
            if (!string.IsNullOrEmpty(hint)) Text(hint, "el-muted", header);
            var spacer = new VisualElement();
            spacer.AddToClassList("el-spacer");
            header.Add(spacer);
            return header;
        }

        // つながった切り替えボタンの入れ物（選んでいるものを el-button--selected で示す）
        static VisualElement Segmented(VisualElement parent)
        {
            var group = new VisualElement();
            group.AddToClassList("el-segmented");
            parent.Add(group);
            return group;
        }

        // パネルからの操作：出どころを panel にして呼び、すぐに表示を更新する。
        // 文字の欄のフォーカスを外す（入力の後にホットキーが効くように）
        void Act(Action action)
        {
            if (session != null)
            {
                using (session.WithOrigin(ChangeOrigins.Panel)) action();
            }
            else
            {
                action();
            }
            root?.panel?.focusController?.focusedElement?.Blur();
            Refresh();
        }

        Foldout Section(VisualElement parent, string title, bool open)
        {
            var foldout = new Foldout { text = title, value = open };
            foldout.AddToClassList("el-section");
            // 見出しにフォーカスが残ると、Enter（課題の開始）で欄が開閉してしまう
            var header = foldout.Q<Toggle>();
            if (header != null) header.focusable = false;
            parent.Add(foldout);
            return foldout;
        }

        Toggle AddToggle(VisualElement parent, string text, Action<bool> onChange)
        {
            var toggle = new Toggle { text = text, focusable = false };
            toggle.AddToClassList("el-toggle");
            toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            parent.Add(toggle);
            return toggle;
        }

        static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("el-row");
            parent.Add(row);
            return row;
        }

        static Label Text(string text, string className = null, VisualElement parent = null)
        {
            var label = new Label(text);
            label.AddToClassList("el-text");
            if (className != null) label.AddToClassList(className);
            parent?.Add(label);
            return label;
        }

        static Label AddCell(VisualElement row, string text, string className)
        {
            var label = new Label(text);
            if (className != null) label.AddToClassList(className);
            row.Add(label);
            return label;
        }

        Button Btn(VisualElement parent, string text, Action onClick, bool grow = false)
        {
            // ボタンにフォーカスが残ると、Enter（課題の開始）でそのボタンも押されてしまうので、フォーカスを受けない
            var button = new Button(() => Act(onClick)) { text = text, focusable = false };
            button.AddToClassList("el-button");
            if (grow) button.AddToClassList("el-button--grow");
            parent.Add(button);
            return button;
        }

        static TextField Field(VisualElement parent, float width)
        {
            var field = new TextField { isDelayed = false };
            field.AddToClassList("el-field");
            field.style.width = width;
            parent.Add(field);
            return field;
        }

        static void SetText(TextElement element, string text)
        {
            if (element != null && element.text != text) element.text = text;
        }

        static void Show(VisualElement element, bool show)
        {
            if (element != null) element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // 文があれば出し、なければ隠す
        static void ShowText(TextElement element, string text)
        {
            if (element == null) return;
            SetText(element, text ?? "");
            Show(element, !string.IsNullOrEmpty(text));
        }

        static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int colon = id.IndexOf(':');
            return colon >= 0 ? id.Substring(colon + 1) : id;
        }

        // ---- 表示の更新 ----

        void Refresh()
        {
            if (!built || session == null) return;
            RefreshBanner();
            if (!visible) return;
            RefreshMode();
            RefreshParticipant();
            RefreshDemo();
            RefreshRunner();
            RefreshReach();
            var channel = session.Selected;
            if (channel != null)
            {
                RefreshLimb(channel);
                if (detailSection != null && detailSection.value) RefreshDetail(channel);
            }
            RefreshRecord();
            RefreshDisplay();
            RefreshReplay();
            RefreshPractice();
        }

        // トラッカーの確認の進み具合（済んだ部位はトラッカーの ID の末尾）
        static string TrackerCheckListText(TrackerCheck check)
        {
            var parts = new List<string>();
            for (int i = 0; i < check.Sites.Count; i++)
            {
                var site = check.Sites[i];
                string name = BodySiteNames.Japanese(site);
                if (check.TryGetFound(site, out string id)) parts.Add($"{name} 済（{ShortId(id)}）");
                else if (check.IsSkipped(site)) parts.Add($"{name} なし");
                else if (i == check.Index) parts.Add($"{name} ← 今");
                else parts.Add($"{name} まだ");
            }
            return string.Join("　", parts);
        }

        // 「条件の進行」の欄の、今の段階
        string RunnerPhaseText()
        {
            if (runner == null) return "";
            string prefix = runner.IsPractice ? "練習：" : "";
            switch (runner.Phase)
            {
                case TrialPhase.Ready: return "準備中：動かし方を説明 → 開始（参加者が止まると Re-center して始まります）";
                case TrialPhase.Settle:
                    return $"{prefix}参加者が止まるのを待っています（止まって {runner.SettleStillSeconds:F1}/{runner.SettleHoldTime:F1} 秒）。今の姿勢で始めるなら Space";
                case TrialPhase.Lead: return $"{prefix}まもなく前半（{Mathf.CeilToInt((float)runner.SecondsToSegmentStart)} 秒後）";
                case TrialPhase.Segment1: return $"{prefix}前半（追加肢のみ）　残り {Clock(runner.SegmentRemaining)}";
                case TrialPhase.Interlude: return $"{prefix}まもなく後半（{Mathf.CeilToInt((float)runner.SecondsToSegmentStart)} 秒後）";
                case TrialPhase.Segment2: return $"{prefix}後半（本来の手も可）　残り {Clock(runner.SegmentRemaining)}";
                case TrialPhase.Rating:
                    int cursor = runner.Rating.Cursor;
                    return cursor >= 0 ? $"口頭評価：「{ConditionRating.ItemNames[cursor]}」（{cursor + 1}/3）" : "口頭評価";
                case TrialPhase.Done: return "完了。次の条件へ（PgDn）";
                case TrialPhase.Break: return $"休憩中 {Clock(runner.BreakElapsed)}（HMD を外す）。次のブロックへ（PgDn）";
                case TrialPhase.Finished: return "すべての条件が終わりました";
                default: return runner.HasPlan ? "次へ（PgDn）で最初の条件を読み込みます" : "";
            }
        }

        // 左上の大きな表示の、段階の名前と時間（離れても読めるように短く）。time は残り（課題中）か経過（休憩）、progress は課題の進み具合（0〜1、なければ負）
        void RunnerBannerParts(out string phase, out string time, out float progress)
        {
            time = "";
            progress = -1f;
            float segment = Mathf.Max(1f, runner.Timing.segmentSeconds);
            switch (runner.Phase)
            {
                case TrialPhase.Ready: phase = "準備中"; break;
                case TrialPhase.Settle: phase = "止まるのを待っています"; break;
                case TrialPhase.Lead:
                    phase = "まもなく前半";
                    time = Mathf.CeilToInt((float)runner.SecondsToSegmentStart).ToString(CultureInfo.InvariantCulture);
                    progress = 0f;
                    break;
                case TrialPhase.Segment1:
                case TrialPhase.Segment2:
                    phase = runner.Phase == TrialPhase.Segment1 ? "前半　追加肢のみ" : "後半　両手も可";
                    time = Clock(runner.SegmentRemaining);
                    progress = 1f - Mathf.Clamp01((float)runner.SegmentRemaining / segment);
                    break;
                case TrialPhase.Interlude:
                    phase = "まもなく後半";
                    time = Mathf.CeilToInt((float)runner.SecondsToSegmentStart).ToString(CultureInfo.InvariantCulture);
                    progress = 1f;
                    break;
                case TrialPhase.Rating:
                    int cursor = runner.Rating.Cursor;
                    phase = cursor >= 0 ? $"口頭評価　{cursor + 1}/3" : "口頭評価";
                    break;
                case TrialPhase.Done: phase = "完了　PgDn で次へ"; break;
                case TrialPhase.Break:
                    phase = "休憩";
                    time = Clock(runner.BreakElapsed);
                    break;
                case TrialPhase.Finished: phase = "すべての条件が終了"; break;
                default: phase = runner.HasPlan ? "PgDn で最初の条件へ" : ""; break;
            }
        }

        static string Clock(double seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        void RefreshBanner()
        {
            // モード、条件と段階（デモでは練習の間だけ段階を出す）
            bool demo = runner != null && !runner.IsExperimentMode;
            bool showRunner = runner != null && (runner.IsPractice || (!demo && runner.HasPlan));
            Show(bannerMeta, runner != null);
            Show(bannerHeadline, showRunner);
            Show(bannerProgress, false);
            if (runner != null)
            {
                var spec = runner.Current;
                SetText(bannerMeta, demo ? (runner.IsPractice ? "デモ　練習" : "デモ")
                    : runner.IsPractice ? "練習（条件に数えない）"
                    : spec != null ? $"条件 {spec.order + 1}/{runner.Plan.Count}　ブロック {spec.block + 1}/{runner.Plan.BlockCount}"
                    : runner.HasPlan ? runner.Plan.participantId
                    : "実験　提示順を作成してください");
            }
            if (showRunner)
            {
                RunnerBannerParts(out string phase, out string time, out float progress);
                SetText(bannerPhase, phase);
                SetText(bannerTimer, time);
                Show(bannerTimer, time.Length > 0);
                bool live = runner.IsRunning;
                bool attention = runner.Phase == TrialPhase.Rating || runner.Phase == TrialPhase.Break;
                bannerTimer.EnableInClassList("el-live", live);
                bannerTimer.EnableInClassList("el-caution", attention);
                bannerPhase.EnableInClassList("el-caution", attention);
                if (progress >= 0f)
                {
                    Show(bannerProgress, true);
                    bannerProgressFill.style.width = Length.Percent(progress * 100f);
                }
            }

            // 追加肢の設定（2本のときは左右とも）
            int line = 0;
            foreach (var channel in session.Channels)
            {
                if (!channel.Active || line >= bannerLimbs.Count) continue;
                var (row, text, pill) = bannerLimbs[line++];
                Show(row, true);
                var preset = MappingPresets.Find(session.MappingPresetList, channel.Config.mappingPresetId);
                string attach = channel.Mount != null && channel.Mount.Current != null ? channel.Mount.Current.displayName : "—";
                string side = session.TwoLimbs ? $"{channel.Label}　" : "";
                SetText(text, $"{side}{attach}｜{BodySiteNames.Japanese(channel.Config.inputSite)}｜{(preset != null ? preset.displayName : "型なし")}｜ゲイン {channel.Config.positionGain:F1}");
                Show(pill, !channel.IsInputValid);
            }
            for (; line < bannerLimbs.Count; line++) Show(bannerLimbs[line].row, false);

            // 記録の状態など
            var sub = new StringBuilder();
            if (recorder != null && recorder.IsRecording)
            {
                var elapsed = TimeSpan.FromSeconds(recorder.ElapsedSeconds);
                sub.Append($"<color={DangerHex}>●</color>  記録中 {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}");
                if (recorder.MarkerCount > 0) sub.Append($"　マーカー {recorder.MarkerCount}");
            }
            else if (runner != null && !runner.IsExperimentMode)
            {
                sub.Append("記録していません（L で開始）");
            }
            else
            {
                sub.Append($"<color={CautionHex}>記録していません（L で開始）</color>");
            }
            if (reach != null && reach.IsRunning) sub.Append($"　<color={LiveHex}>到達 {reach.Hits}</color>");
            if (session.TwoLimbs && session.Selected != null) sub.Append($"　操作中：{session.Selected.Label}");
            if (notice != null && notice.CurrentText.Length > 0) sub.Append($"　HMD：{notice.CurrentText}");
            if (replayer != null && replayer.IsLoaded) sub.Append($"　<color={LiveHex}>ログの再生 {(replayer.IsPlaying ? "▶" : "Ⅱ")} {Clock(replayer.Position)}</color>");
            SetText(bannerSub, sub.ToString());

            // 機器の異常（知らせることがある機器だけ）
            int shown = 0;
            foreach (var provider in providers)
            {
                if (provider == null || (provider is UnityEngine.Object unityObject && unityObject == null)) continue;
                string status = provider.StatusText;
                if (!provider.NeedsAttention || string.IsNullOrEmpty(status)) continue;
                if (shown >= bannerDeviceLines.Count)
                {
                    var label = BannerLabel("el-banner__device", bannerDevices);
                    bannerDeviceLines.Add(label);
                }
                var deviceLine = bannerDeviceLines[shown++];
                Show(deviceLine, true);
                SetText(deviceLine, $"<color={DangerHex}>●</color>  {provider.ProviderName}：{status}");
            }
            for (int i = shown; i < bannerDeviceLines.Count; i++) Show(bannerDeviceLines[i], false);
            Show(bannerDevices, shown > 0);

            // トラッカーの一覧（IMGUI）は左上の表示の下に出す
            if (monitor != null)
            {
                var bounds = banner.worldBound;
                if (!float.IsNaN(bounds.yMax)) monitor.GuiPosition = new Vector2(bounds.xMin, bounds.yMax + 10f);
                if (!float.IsNaN(bounds.xMin)) monitor.GuiMaxWidth = Screen.width - (visible ? PanelWidth() : 0f) - bounds.xMin - 12f;
            }
        }

        // モードの切り替えと、モードで出す欄
        void RefreshMode()
        {
            if (runner == null) return;
            bool experiment = runner.IsExperimentMode;
            bool canSwitch = !runner.IsRunning && runner.Phase != TrialPhase.Rating;
            demoModeButton.EnableInClassList(SelectedClass, !experiment);
            experimentModeButton.EnableInClassList(SelectedClass, experiment);
            demoModeButton.SetEnabled(canSwitch || !experiment);
            experimentModeButton.SetEnabled(canSwitch || experiment);
            Show(idRow, experiment);
            Show(planLabel, experiment && planLabel.text.Length > 0);
            Show(designLabel, experiment);
            if (!experiment) Show(participantMessage, false);
            Show(demoSection, !experiment);
            Show(runnerSection, experiment);
        }

        void RefreshDemo()
        {
            if (runner == null || demoSection == null || runner.IsExperimentMode) return;
            bool running = runner.IsRunning;
            SetText(demoPhaseLabel, runner.IsPractice ? RunnerPhaseText() : "今の設定のまま課題を試せます（Re-center → 課題を開始）");
            demoPhaseLabel.EnableInClassList("el-live", runner.Segment > 0);
            demoPhaseLabel.EnableInClassList("el-muted", !runner.IsPractice);
            demoStartButton.SetEnabled(!running && runner.Phase != TrialPhase.Rating);
            demoStartButton.EnableInClassList(PrimaryClass, !running);
            demoAbortButton.SetEnabled(running);
            ShowText(demoMessage, runner.Message);
            if (!demoSection.value) return;

            var channel = session.Primary;
            var config = channel != null ? channel.Config : null;
            foreach (var (button, _, pointId, site, preset) in comboButtons)
            {
                bool current = config != null && config.attachPointId == pointId && config.inputSite == site
                               && config.mappingPresetId == preset.id && string.IsNullOrEmpty(config.inputSourceId);
                button.EnableInClassList(SelectedClass, current);
                button.SetEnabled(!running);
            }
            foreach (var (chip, site, preset) in designChips)
                chip.EnableInClassList(SelectedClass, runner.ExperimentMapping(site) == preset);
        }

        void RefreshParticipant()
        {
            if (participantMessage != null && participantMessage.text.Length == 0) Show(participantMessage, false);
            if (planLabel != null)
            {
                var plan = runner.Plan;
                if (plan != null && participantField != null && string.IsNullOrEmpty(participantField.value) && !IsTypingText)
                    participantField.SetValueWithoutNotify(plan.participantId);
                ShowText(planLabel, plan != null && runner.IsExperimentMode
                    ? $"{plan.participantId}　順序 {plan.latinRow + 1}/{plan.latinRowCount}：{plan.BlockOrderText}　完了 {runner.DoneCount}/{plan.Count}"
                    : "");
            }
            if (designLabel != null && runner.IsExperimentMode) SetText(designLabel, $"実験で使う型：{runner.DesignText()}（変えるときはデモモードで）");
            if (trackerPlanLabel != null)
            {
                var first = runner.FirstTrackerSites;
                string text = "";
                if (runner.IsExperimentMode && runner.HasPlan && first.Count > 0)
                {
                    var names = new List<string>();
                    for (int i = 0; i < first.Count; i += 2) names.Add(BodySiteNames.Kind(first[i]));
                    var later = new List<string>();
                    foreach (var (_, to) in runner.TrackerSwaps)
                        if (BodySites.IsRight(to)) later.Add(BodySiteNames.Kind(to));
                    text = $"トラッカー：最初は{string.Join("・", names)}（左右）に付け、3つ目のブロックの前に{string.Join("・", later)}へ付け替える。" +
                           "どちらも付けた後に「トラッカーの確認」をする（どのトラッカーをどこに付けてもよい）";
                }
                ShowText(trackerPlanLabel, text);
            }
            if (checklistLabel != null) ShowText(checklistLabel, runner.IsExperimentMode ? ChecklistText() : "");
            if (bodyMessage != null) ShowText(bodyMessage, session.BodyMessage);
        }

        // 準備のチェックリスト：提示順・体の寸法・トラッカーの確認（次のブロックの部位）・円
        string ChecklistText()
        {
            var plan = runner.Plan;
            var body = session.Body;
            bool hasPlan = runner.HasPlan;
            bool bodyDone = body != null && body.IsReady && body.Calibration.IsCalibrated;
            var required = runner.RequiredTrackerSites;
            var missing = new List<string>();
            bool anyAbsent = false;
            foreach (var site in required)
            {
                if (runner.IsTrackerSiteAbsent(site)) anyAbsent = true;
                else if (!runner.IsTrackerSiteVerified(site)) missing.Add(BodySiteNames.Japanese(site));
            }
            var kinds = new List<string>();
            for (int i = 0; i < required.Count; i += 2) kinds.Add(BodySiteNames.Kind(required[i]));
            string trackers = required.Count == 0 ? "トラッカーの確認：提示順を作ってから"
                : missing.Count > 0 ? $"トラッカーの確認：まだ（{string.Join("・", missing)}）"
                : anyAbsent ? $"トラッカーの確認：済み（{string.Join("・", kinds)}。左はトラッカーなしで右の追加肢だけ）"
                : $"トラッカーの確認：済み（{string.Join("・", kinds)}の左右）";
            var spec = runner.Current;
            int block = spec != null ? spec.block : -1;
            string ring = block >= 0 && runner.RingBlock == block ? "円：このブロックで置き直し済み" : "円：ブロックの最初の開始で置き直します";
            return $"準備　{(hasPlan ? "提示順：済み" : "提示順：まだ")}　{(bodyDone ? "体の寸法：済み" : "体の寸法：まだ（反映を押す）")}\n{trackers}\n{ring}";
        }

        void RefreshRunner()
        {
            if (runner == null || conditionLabel == null || !runner.IsExperimentMode) return;
            var plan = runner.Plan;
            var spec = runner.Current;
            SetText(conditionLabel, spec != null
                ? $"条件 {spec.order + 1}/{plan.Count}（ブロック {spec.block + 1}/{plan.BlockCount}・{spec.indexInBlock + 1}/{spec.blockSize}）\n{spec.Label}"
                : plan != null ? "条件はまだ読み込んでいません" : "参加者IDを入れて「提示順を作成」を押してください");
            conditionLabel.EnableInClassList("el-muted", spec == null);

            var preset = spec != null ? MappingPresets.Find(session.MappingPresetList, spec.mappingPresetId) : null;
            ShowText(conditionInstruction, preset != null && preset.instruction.Length > 0 ? $"動かし方：{preset.instruction}" : "");

            var phase = runner.Phase;
            ShowText(phaseLabel, RunnerPhaseText());
            phaseLabel.EnableInClassList("el-live", runner.Segment > 0);
            phaseLabel.EnableInClassList("el-caution", phase == TrialPhase.Rating || phase == TrialPhase.Break);

            bool beforeOrDuring = phase == TrialPhase.Ready || runner.IsRunning;
            bool mismatch = spec != null && !runner.IsPractice && beforeOrDuring && !runner.MatchesCurrent;
            string recenter = mismatch ? "今の設定が条件と異なります（R で読み込み直し）"
                : spec == null || phase != TrialPhase.Ready ? ""
                : "Re-center は開始のときに、参加者が止まるのを待って自動で行います";
            ShowText(recenterLabel, recenter);
            recenterLabel.EnableInClassList("el-warn", mismatch);
            recenterLabel.EnableInClassList("el-muted", !mismatch);
            ShowText(layoutWarningLabel, session.LayoutWarning);
            ShowText(leftLimbNoteLabel, runner.LeftLimbNote);

            // トラッカーの確認
            var check = runner.TrackerCheck;
            bool checking = check.IsActive;
            bool needsCheck = runner.NeedsTrackerCheck;
            string checkText = "";
            if (checking)
            {
                checkText = ConditionRunner.CheckInstruction(check.CurrentSite) + (check.Hint.Length > 0 ? "\n" + check.Hint : "");
                if (runner.CanSkipLeftTrackers)
                    checkText += "\n左のトラッカーを付けていなければ「左はなし」を押してください（左の部位らしい位置にトラッカーがなければ自動で飛ばします）";
            }
            else if (needsCheck)
            {
                var required = runner.RequiredTrackerSites;
                var kinds = new List<string>();
                for (int i = 0; i < required.Count; i += 2) kinds.Add(BodySiteNames.Kind(required[i]));
                bool later = spec != null ? spec.block >= 2 || (phase != TrialPhase.Ready && runner.CurrentIndex + 1 < plan.Count && plan.conditions[runner.CurrentIndex + 1].block >= 2)
                    : false;
                checkText = $"トラッカーを{string.Join("・", kinds)}の左右に{(later ? "付け替えて" : "付けて")}から、確認してください（どのトラッカーをどこに付けてもかまいません。" +
                            "左のトラッカーがなければ右だけでもかまいません）";
            }
            ShowText(trackerCheckLabel, checkText);
            ShowText(trackerCheckList, checking || check.IsComplete && needsCheck ? TrackerCheckListText(check) : "");
            Show(trackerCheckButton, !checking && (needsCheck || phase == TrialPhase.Ready || phase == TrialPhase.Break || phase == TrialPhase.Done || phase == TrialPhase.Idle));
            trackerCheckButton.SetEnabled(!runner.IsRunning && phase != TrialPhase.Rating);
            SetText(trackerCheckButton, needsCheck ? "トラッカーの確認を始める" : "トラッカーの確認をやり直す");
            Show(trackerCheckSkipLeftButton, runner.CanSkipLeftTrackers);
            Show(trackerCheckCancelButton, checking);

            // 始められない理由（準備中だけ）
            string blocked = "";
            bool canStart = phase == TrialPhase.Ready && runner.CanStart(out blocked);
            ShowText(startBlockedLabel, phase == TrialPhase.Ready && !canStart ? "始められません：" + blocked.Replace("\n", "\n・") : "");

            bool running = runner.IsRunning;
            prevButton.SetEnabled(runner.HasPlan && !running && runner.CurrentIndex > 0);
            nextButton.SetEnabled(runner.HasPlan && !running && runner.CurrentIndex + 1 < plan.Count);
            startButton.SetEnabled(canStart);
            abortButton.SetEnabled(running || phase == TrialPhase.Rating);
            redoButton.SetEnabled(spec != null && !running);
            practiceButton.SetEnabled(!running && phase != TrialPhase.Rating);

            // 次に押すボタンだけを塗る（トラッカーの確認 → 開始、終わったら次へ）
            Button primary = null;
            if (phase == TrialPhase.Ready && canStart) primary = startButton;
            else if (!running && phase != TrialPhase.Rating && phase != TrialPhase.Finished && runner.HasPlan
                     && (spec == null || phase == TrialPhase.Done || phase == TrialPhase.Break) && runner.CurrentIndex + 1 < plan.Count) primary = nextButton;
            if (needsCheck && !checking) primary = trackerCheckButton;
            foreach (var button in new[] { recenterButton, startButton, nextButton, trackerCheckButton }) button.EnableInClassList(PrimaryClass, button == primary);
            createPlanButton?.EnableInClassList(PrimaryClass, !runner.HasPlan);

            var rating = runner.Rating;
            int cursor = phase == TrialPhase.Rating ? rating.Cursor : -1;
            bool editable = phase == TrialPhase.Rating || ((phase == TrialPhase.Done || phase == TrialPhase.Break || phase == TrialPhase.Finished)
                                                           && runner.StatusOf(runner.CurrentIndex) == ConditionStatus.Done);
            for (int item = 0; item < ratingLabels.Length; item++)
            {
                ratingLabels[item].EnableInClassList("el-current", item == cursor);
                for (int value = ConditionRating.Min; value <= ConditionRating.Max; value++)
                {
                    var button = ratingButtons[item, value - 1];
                    button.EnableInClassList(SelectedClass, rating[item] == value);
                    button.SetEnabled(editable);
                }
            }
            ShowText(runnerMessage, runner.Message);
            RefreshSequenceList();
        }

        void RefreshSequenceList()
        {
            var plan = runner.Plan;
            if (plan != listedPlan)
            {
                listedPlan = plan;
                sequenceList.Clear();
                sequenceButtons.Clear();
                if (plan != null)
                    for (int i = 0; i < plan.Count; i++)
                    {
                        int index = i;
                        var button = Btn(sequenceList, "", () => runner.Load(index));
                        button.AddToClassList("el-list-button");
                        sequenceButtons.Add(button);
                    }
            }
            if (plan == null || !sequenceSection.value) return;
            for (int i = 0; i < sequenceButtons.Count; i++)
            {
                var spec = plan.conditions[i];
                var status = runner.StatusOf(i);
                string mark = i == runner.CurrentIndex ? "▶" : status == ConditionStatus.Done ? "✓" : status == ConditionStatus.Aborted ? "×" : "・";
                int attempts = runner.AttemptsOf(i);
                SetText(sequenceButtons[i], $"{mark} {i + 1}　{spec.Label}" + (attempts > 1 ? $"（{attempts}回目）" : ""));
                sequenceButtons[i].EnableInClassList(SelectedClass, i == runner.CurrentIndex);
                sequenceButtons[i].SetEnabled(!runner.IsRunning);
            }
        }

        void RefreshReach()
        {
            if (reach == null || reachSection == null) return;
            reachIdleToggle?.SetValueWithoutNotify(reach.ShowIdleTargets);
            reachProbesToggle?.SetValueWithoutNotify(reach.ShowProbes);
            if (practice != null) practiceVisibleToggle?.SetValueWithoutNotify(practice.Visible);
            reachPlaceButton.SetEnabled(!reach.IsRunning);
            if (!reachSection.value) return;

            var ring = reach.Ring;
            SetText(reachRingLabel, reach.IsPlaced
                ? $"円：中心の高さ {ring.center.y * 100f:F0} cm　直径 {ring.radius * 200f:F0} cm"
                : "円はまだありません（最初の Re-center で置かれます）");
            reachRingLabel.EnableInClassList("el-caution", !reach.IsPlaced);

            int reachable = reach.CountReachable(out string unreachable);
            ShowText(reachReachableLabel, reachable < 0 ? "" : reachable == ring.Count
                ? $"追加肢が届くターゲット：{reachable}/{ring.Count}"
                : $"追加肢が届くターゲット：{reachable}/{ring.Count}（届かない：{unreachable}）");
            reachReachableLabel.EnableInClassList("el-ok", reachable >= 0 && reachable == ring.Count);
            reachReachableLabel.EnableInClassList("el-warn", reachable >= 0 && reachable < ring.Count);

            string live = "";
            if (reach.IsRunning)
            {
                live = $"{(reach.Segment == 1 ? "前半" : "後半")}　到達 {reach.Hits}　点灯中：{ReachRing.PositionName(reach.LitIndex, ring.Count)}（{reach.LitSeconds:F1} 秒）";
                if (reach.Segment == 1 && reach.IgnoredInSegment > 0) live += $"\n本来の手で触れた回数（数えない）：{reach.IgnoredInSegment}";
                if (reach.LastEffector != EffectorKind.None) live += $"\n直前：{EffectorKinds.Japanese(reach.LastEffector)}　{reach.LastReachSeconds:F2} 秒";
            }
            else if (runner != null)
            {
                var hits1 = runner.HitsInSegment(1);
                var hits2 = runner.HitsInSegment(2);
                if (hits1.HasValue || hits2.HasValue)
                    live = $"前回の到達数：前半 {(hits1.HasValue ? hits1.Value.ToString(CultureInfo.InvariantCulture) : "—")}　後半 {(hits2.HasValue ? hits2.Value.ToString(CultureInfo.InvariantCulture) : "—")}";
            }
            ShowText(reachLiveLabel, live);
            reachLiveLabel.EnableInClassList("el-live", reach.IsRunning);
            ShowText(reachMessageLabel, reach.Message);
        }

        void RefreshLimb(LimbChannel channel)
        {
            var config = channel.Config;
            // 実験モードでは、条件で決まる設定を変えるボタンを押せなくする
            bool locked = runner != null && runner.ConditionSettingsLocked;
            Show(limbLockNote, locked);
            foreach (var element in lockables) element.SetEnabled(!locked);
            if (session.HasLeftLimb)
            {
                rightButton.EnableInClassList(SelectedClass, channel.Side == LimbSide.Right);
                leftButton.EnableInClassList(SelectedClass, channel.Side == LimbSide.Left);
                leftButton.SetEnabled(session.TwoLimbs);
                SetText(twoLimbsButton, Key(session.TwoLimbs ? "2本目を隠す" : "2本目を出す", "T"));
            }
            limbSection.text = session.TwoLimbs ? $"追加肢（{channel.Label}を設定中）" : "追加肢";

            // 生やす位置
            var mount = channel.Mount;
            if (mount != null)
            {
                var current = mount.Current;
                foreach (var (button, point) in attachButtons) button.EnableInClassList(SelectedClass, point == current);
                SetText(attachState, mount.IsTransitioning ? "付け替え中" : session.TwoLimbs ? "左右とも" : "");
            }

            // 入力の部位（トラッカーが見つからない部位は赤）
            // 左の追加肢では左の同じ部位を出す
            var registry = session.Registry;
            bool leftSide = channel.Side == LimbSide.Left;
            foreach (var (button, site) in siteButtons)
            {
                var actual = BodySites.OnSide(site, leftSide);
                SetText(button, BodySiteNames.Japanese(actual));
                bool available = registry != null && SourceSelector.SelectInput(registry, "", actual) != null;
                button.EnableInClassList(MissingClass, !available);
                button.EnableInClassList(SelectedClass, actual == config.inputSite && string.IsNullOrEmpty(config.inputSourceId));
            }
            if (trackerListButton != null) SetText(trackerListButton, monitor.ListOpen ? "トラッカーの一覧を閉じる" : "トラッカーの一覧");
            ShowText(channelStatus, channel.IsInputValid ? "" : channel.StatusMessage);
            RefreshAssign(channel);

            // マッピングの型（部位を変えたらボタンを作り直す）
            var presets = MappingPresets.ForSite(session.MappingPresetList, config.inputSite);
            if (presetSite != config.inputSite)
            {
                presetSite = config.inputSite;
                presetRow.Clear();
                presetButtons.Clear();
                foreach (var preset in presets)
                {
                    var target = preset;
                    presetButtons.Add((Btn(presetRow, preset.displayName, () => session.SelectMappingPreset(target), true), preset));
                }
                if (presets.Count == 0) Text("この部位の型はありません", "el-muted", presetRow);
            }
            foreach (var (button, preset) in presetButtons)
            {
                button.EnableInClassList(SelectedClass, preset.id == config.mappingPresetId);
                button.SetEnabled(!locked);
            }
            var mapping = config.axisMapping;
            SetText(axesLabel, $"手先の右 ← {mapping.DescribeAxis(0)}　上 ← {mapping.DescribeAxis(1)}　前 ← {mapping.DescribeAxis(2)}");
            for (int axis = 0; axis < 3; axis++)
            {
                invertToggles[axis].SetValueWithoutNotify(mapping.IsInverted(axis));
                invertToggles[axis].SetEnabled(mapping[axis].IsUsed && !locked);
            }
            SetText(gainLabel, $"ゲイン {config.positionGain:F1}");

            bool hasForward = channel.Pipeline.BodyFrame.HasForward;
            SetText(forwardLabel, hasForward ? $"前の向き：{(channel.ForwardSource.Length > 0 ? channel.ForwardSource : "記録済み")}" : "前の向き：未記録");
            forwardLabel.EnableInClassList("el-caution", !hasForward);
            forwardLabel.EnableInClassList("el-ok", hasForward);
            autoForwardToggle?.SetValueWithoutNotify(session.AutoForwardFromSides);
            string forwardText = channel.ForwardMessage;
            if (!hasForward && forwardText.Length == 0)
                forwardText = session.AutoForwardFromSides
                    ? "左右の同じ部位にトラッカーを付けると、Re-center のときに前の向きを求めます（F で記録もできます）"
                    : "入力の部位を前へ出してから記録してください";
            ShowText(forwardMessage, forwardText);
            ShowText(limbLayoutWarning, session.LayoutWarning);
        }

        // 振ったトラッカーを、設定している追加肢の入力の部位に割り当てる。困ったときだけ案内とボタンを出す
        void RefreshAssign(LimbChannel channel)
        {
            var config = channel.Config;
            var registry = session.Registry;
            string siteName = BodySiteNames.Japanese(config.inputSite);
            string recent = session.RecentlyMovedTracker;
            ITrackedPoseSource tracker = null;
            if (recent != null && (registry == null || !registry.TryGet(recent, out tracker))) recent = null;

            string hint = "";
            bool showButton = false;
            if (recent != null && tracker.Site == config.inputSite)
            {
                hint = $"振ったトラッカー（{ShortId(recent)}）は{siteName}に割り当て済みです";
            }
            else if (recent != null)
            {
                showButton = true;
                SetText(assignButton, $"振ったトラッカー（{ShortId(recent)}、今は{BodySiteNames.Japanese(tracker.Site)}）を{siteName}に割り当てる");
            }
            else if (channel.InputId == null)
            {
                hint = $"{siteName}に付けたトラッカーを振ると、割り当てのボタンが出ます";
            }
            ShowText(assignHint, hint);
            Show(assignButton, showButton);
            ShowText(assignMessage, session.AssignMessage);
        }

        void RefreshDetail(LimbChannel channel)
        {
            SetText(inputLabel, $"入力：{session.DescribeSource(channel.InputId)}");
            SetText(referenceLabel, $"基準：{session.DescribeSource(channel.ReferenceId)}");
            var u = channel.Pipeline.LastDisplacement * 100f;
            bool edge = channel.Pipeline.AtWorkspaceBoundary;
            SetText(uLabel, $"手先への入力：右 {u.x,6:F1}　上 {u.y,6:F1}　前 {u.z,6:F1} cm" + (edge ? "　（手先が可動範囲の端）" : ""));
            uLabel.EnableInClassList("el-caution", edge);

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
                bool isUsed = used.Length > 0;
                SetText(channelCells[i, 1], LimbChannel.DisplayValue(values, inputChannel).ToString("+0.0;-0.0", CultureInfo.InvariantCulture));
                SetText(channelCells[i, 2], hasRange ? channel.ChannelMin(i).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) : "");
                SetText(channelCells[i, 3], hasRange ? channel.ChannelMax(i).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) : "");
                SetText(channelCells[i, 4], used);
                for (int c = 0; c < 5; c++) channelCells[i, c].EnableInClassList("el-used", isUsed);
            }
            var preset = MappingPresets.Find(session.MappingPresetList, channel.Config.mappingPresetId);
            ShowText(instructionLabel, preset != null && preset.instruction.Length > 0 ? $"動かし方：{preset.instruction}" : "");
            ShowText(leverLabel, mapping.UsesRotation
                ? $"回転は {mapping.leverLength * 100f:F0} cm の長さに換算（10° で {mapping.leverLength * 10f * Mathf.Deg2Rad * 100f:F1} cm）"
                : "");
            RefreshGrasp();
        }

        void RefreshGrasp()
        {
            var grasp = session.Grasp;
            if (grasp == null)
            {
                SetText(graspLabel, "把持の機能がありません");
                return;
            }
            var text = new StringBuilder();
            foreach (var channel in session.Channels)
            {
                if (!channel.Active) continue;
                var grasper = grasp.GrasperFor(channel);
                if (grasper == null) continue;
                string side = channel.Config.grasp.hand == ControllerHand.Left ? "左" : "右";
                if (text.Length > 0) text.Append('\n');
                text.Append($"{channel.Label}の追加肢：{side}トリガー {grasp.TriggerValue(channel):F2}{(grasper.Pressed ? "（掴む）" : "")}　{(grasper.Held != null ? grasper.Held.name : "—")}");
            }
            if (grasp.LeftHand != null || grasp.RightHand != null)
            {
                text.Append('\n');
                text.Append($"本来の手：左グリップ {grasp.GripValue(ControllerHand.Left):F2} {(grasp.LeftHand != null && grasp.LeftHand.Held != null ? grasp.LeftHand.Held.name : "—")}");
                text.Append($"　右グリップ {grasp.GripValue(ControllerHand.Right):F2} {(grasp.RightHand != null && grasp.RightHand.Held != null ? grasp.RightHand.Held.name : "—")}");
            }
            if (grasp.KeyPressed) text.Append("\nG キーで掴んでいます");
            SetText(graspLabel, text.ToString());
        }

        void RefreshRecord()
        {
            if (recorder == null || recordButton == null) return;
            if (recorder.IsRecording)
            {
                var elapsed = TimeSpan.FromSeconds(recorder.ElapsedSeconds);
                SetText(recordStatus, $"<color={DangerHex}>●</color>  記録中 {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}　マーカー {recorder.MarkerCount}");
                SetText(recordFolder, $"保存先：{System.IO.Path.GetFileName(recorder.CurrentDirectory)}");
            }
            else
            {
                SetText(recordStatus, "記録していません");
                SetText(recordFolder, $"保存先：{recorder.RootDirectory}");
            }
            recordStatus.EnableInClassList("el-warn", !recorder.IsRecording && (runner == null || runner.IsExperimentMode));
            SetText(recordButton, Key(recorder.IsRecording ? "記録を停止" : "記録を開始", "L"));
            ShowText(recordMessage, recorder.Message);
        }

        void RefreshDisplay()
        {
            markerToggle?.SetValueWithoutNotify(session.ShowTargetMarker);
            if (mirror != null) mirrorToggle?.SetValueWithoutNotify(mirror.Visible);
            if (overlay != null) overlayToggle?.SetValueWithoutNotify(overlay.Show);
            if (spectator != null)
            {
                overlayHmdToggle?.SetValueWithoutNotify(spectator.OverlayInHmd);
                SetText(viewLabel, $"モニタの視点：{spectator.ViewName}");
            }
            if (notice != null && noticeLabel != null)
            {
                string where = notice.OnHmd ? "" : "（HMD がないため、モニタに表示）";
                SetText(noticeLabel, notice.CurrentText.Length > 0 ? $"表示中{where}：{notice.CurrentText}" : "表示していません");
            }
        }

        void RefreshReplay()
        {
            if (replayer == null || replaySection == null || !replaySection.value) return;
            var folders = replayer.Folders;
            string selected = replayer.SelectedFolder;
            SetText(replayFolderLabel, selected != null
                ? $"記録：{System.IO.Path.GetFileName(selected)}（新しい順に {replayer.SelectedFolderIndex + 1}/{folders.Count}）"
                : "再生できる記録がありません");
            var log = replayer.Log;
            string status;
            if (replayer.IsLoading) status = $"読み込み中 {replayer.LoadProgress * 100f:F0}%";
            else if (replayer.Error.Length > 0) status = replayer.Error;
            else if (log == null) status = "読み込んでいません";
            else
            {
                status = $"{(replayer.IsPlaying ? "再生中" : "一時停止")} {Clock(replayer.Position)} / {Clock(replayer.Duration)}（×{replayer.Speed.ToString("0.#", CultureInfo.InvariantCulture)}）";
                if (!log.HasSavedState) status += "\n古い形式の記録のため、Re-center の時刻に合わせた近似で再生します";
            }
            SetText(replayStatusLabel, status);
            replayStatusLabel.EnableInClassList("el-warn", replayer.Error.Length > 0);
            replayStatusLabel.EnableInClassList("el-live", replayer.IsPlaying);
            SetText(replayPlayButton, replayer.IsPlaying ? "一時停止" : "再生");
            replayPlayButton.SetEnabled(log != null);
            foreach (var (button, speed) in replaySpeedButtons) button.EnableInClassList(SelectedClass, Mathf.Approximately(replayer.Speed, speed));
            ShowText(replayErrorLabel, replayer.ErrorSamples > 0
                ? $"記録とのずれ：平均 {replayer.MeanErrorMm:F1} mm、最大 {replayer.MaxErrorMm:F1} mm"
                : "");
        }

        void RefreshPractice()
        {
            if (practice == null || practiceLabel == null) return;
            SetText(practiceLabel, $"押した回数 {practice.Hits}　直前 {practice.LastSeconds:F2} 秒　次：{practice.TargetLabel}　立方体を掴んだ回数 {practice.Grabs}");
        }
    }
}
