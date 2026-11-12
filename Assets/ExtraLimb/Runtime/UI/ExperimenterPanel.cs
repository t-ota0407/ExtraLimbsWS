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
    /// 実験者パネル（Step 4-2、計画書の Part 2-4）。UI Toolkit で、モニタの右にパネル、上に大きな文字の表示を出す。
    /// パネルは画面（モニタ）にだけ描かれ、HMD には映らない。Step 3 までの IMGUI のパネル（追加肢、入力の6つの値、仮のボタン、記録）をここにまとめた。
    /// <list type="bullet">
    /// <item>上の表示：動かしている追加肢ごとに「生やす位置｜入力の部位｜マッピングの型・ゲイン」、記録の状態、追跡が切れたときの警告（赤）。</item>
    /// <item>パネル：条件の進行（Step 4-3：参加者IDと提示順、条件の読み込み、課題の開始・中止、口頭評価）、設定する追加肢、入力と状態、
    /// マッピングの型、生やす位置、入力の6つの値、把持、体の寸法、記録、仮のボタン、表示、ホットキーの一覧。</item>
    /// <item>上の表示には、条件の進行（何番目の条件か、前半・後半と残り時間）も出す。</item>
    /// </list>
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
        [Tooltip("空ならシーンから探す（なければ「仮のボタン」の欄を出さない）")]
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
        [Tooltip("見た目。空なら Resources の StyleResourcePath から読む")]
        [SerializeField] StyleSheet styleSheet;
        [Tooltip("パネルを出すか（ホットキー H で切り替える）。上の大きな表示はいつも出す")]
        [SerializeField] bool visible = true;
        [Tooltip("表示を更新する間隔（秒）")]
        [SerializeField] float refreshInterval = 0.066f;

        static readonly string[] HandAxisNames = { "右", "上", "前" };
        const string SelectedClass = "el-button--selected";

        UIDocument document;
        VisualElement root, panel, banner;
        bool built;
        float nextRefresh;

        // 上の表示
        readonly List<Label> bannerLines = new List<Label>();
        Label bannerSub, bannerRunner;

        // 条件の進行
        TextField participantField, rowField;
        Label planLabel, conditionLabel, phaseLabel, recenterLabel, conditionInstruction, runnerMessage;
        Button startButton, abortButton, redoButton, prevButton, nextButton, practiceButton;
        readonly Label[] ratingLabels = new Label[3];
        readonly Button[,] ratingButtons = new Button[3, ConditionRating.Max];
        Foldout sequenceSection;
        VisualElement sequenceList;

        // ログの再生
        Foldout replaySection;
        Label replayFolderLabel, replayStatusLabel, replayErrorLabel;
        Button replayPlayButton;
        readonly List<(Button button, float speed)> replaySpeedButtons = new List<(Button, float)>();
        ConditionPlan listedPlan;
        readonly List<Button> sequenceButtons = new List<Button>();

        // 追加肢
        Button rightButton, leftButton, twoLimbsButton;

        // 入力と状態
        Foldout stateSection;
        Label inputLabel, assignHint, assignMessage, referenceLabel, statusLabel, forwardLabel, forwardMessage, uLabel;
        Button assignButton;
        readonly List<(Button button, BodySite site)> siteButtons = new List<(Button, BodySite)>();

        // マッピングの型
        Label presetLabel, axesLabel, gainLabel;
        Button nextPresetButton;
        readonly Toggle[] invertToggles = new Toggle[3];

        // 生やす位置
        Label attachLabel;
        readonly List<(Button button, AttachPointDefinition point)> attachButtons = new List<(Button, AttachPointDefinition)>();

        // 入力の6つの値
        Foldout channelSection;
        readonly Label[,] channelCells = new Label[6, 5];
        Label instructionLabel, leverLabel;

        // 把持・体・記録・仮のボタン・表示
        Label graspLabel, bodyMessage, recordStatus, recordFolder, recordMessage, practiceLabel;
        TextField heightField, shoulderField;
        Button recordButton;
        Toggle markerToggle, mirrorToggle, overlayToggle, overlayHmdToggle;
        Label viewLabel, noticeLabel;
        TextField noticeField;
        VisualElement hotkeyList;

        /// <summary>パネルを出しているか（上の大きな表示はいつも出す）。</summary>
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

            BuildBanner();
            BuildPanel();
            Visible = visible;
            built = true;
            Refresh();
        }

        void BuildBanner()
        {
            banner = new VisualElement { pickingMode = PickingMode.Ignore };
            banner.AddToClassList("el-banner");
            bannerRunner = new Label { pickingMode = PickingMode.Ignore };
            bannerRunner.AddToClassList("el-banner__line");
            banner.Add(bannerRunner);
            bannerLines.Clear();
            for (int i = 0; i < 2; i++)
            {
                var line = new Label { pickingMode = PickingMode.Ignore };
                line.AddToClassList("el-banner__line");
                banner.Add(line);
                bannerLines.Add(line);
            }
            bannerSub = new Label { pickingMode = PickingMode.Ignore };
            bannerSub.AddToClassList("el-banner__sub");
            banner.Add(bannerSub);
            root.Add(banner);
        }

        void BuildPanel()
        {
            panel = new VisualElement();
            panel.AddToClassList("el-panel");
            var title = new Label("実験者パネル（Step 4-3）　H：隠す");
            title.AddToClassList("el-panel__title");
            panel.Add(title);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            panel.Add(scroll);
            root.Add(panel);

            if (session == null)
            {
                scroll.Add(Text("LimbSession がシーンにありません", "el-warn"));
                return;
            }

            BuildRunnerSection(scroll);
            BuildLimbSection(scroll);
            BuildStateSection(scroll);
            BuildMappingSection(scroll);
            BuildAttachSection(scroll);
            BuildChannelSection(scroll);
            BuildGraspSection(scroll);
            BuildBodySection(scroll);
            BuildRecordSection(scroll);
            BuildReplaySection(scroll);
            BuildPracticeSection(scroll);
            BuildDisplaySection(scroll);
            BuildHotkeySection(scroll);
        }

        void BuildRunnerSection(VisualElement parent)
        {
            if (runner == null) return;
            var section = Section(parent, "条件の進行（Step 4-3）", true);
            var idRow = Row(section);
            Text("参加者ID", null, idRow);
            participantField = Field(idRow);
            participantField.style.width = 80;
            Text("順序", null, idRow);
            rowField = Field(idRow);
            rowField.style.width = 36;
            Btn(idRow, "提示順を作る", CreatePlan);
            Text("順序（ラテン方格の行）は、空なら参加者IDの数字から決める（P01 → 1）", "el-muted", section);
            planLabel = Text("", "el-muted", section);
            conditionLabel = Text("", "el-condition", section);
            phaseLabel = Text("", "el-phase", section);
            recenterLabel = Text("", null, section);
            conditionInstruction = Text("", "el-muted", section);
            var buttons = Row(section);
            prevButton = Btn(buttons, "前へ（PgUp）", () => runner.Previous());
            startButton = Btn(buttons, "開始（Enter）", () => runner.StartTrial(), true);
            abortButton = Btn(buttons, "中止（Esc）", () => runner.Abort());
            redoButton = Btn(buttons, "やり直す（R）", () => runner.Redo());
            nextButton = Btn(buttons, "次へ（PgDn）", () => runner.Next(), true);
            var extra = Row(section);
            practiceButton = Btn(extra, "練習（今の設定で。提示順に数えない）", () => runner.StartPractice());
            Text("口頭評価（7段階。数字キー 1〜7 で上から順に入る。入力中の項目は黄色）", "el-muted", section);
            for (int item = 0; item < ratingLabels.Length; item++)
            {
                var ratingRow = Row(section);
                ratingRow.style.flexWrap = Wrap.NoWrap;
                ratingLabels[item] = Text(ConditionRating.ItemNames[item], "el-rating-label", ratingRow);
                for (int value = ConditionRating.Min; value <= ConditionRating.Max; value++)
                {
                    int index = item, score = value;
                    var button = Btn(ratingRow, value.ToString(CultureInfo.InvariantCulture), () => runner.SetRating(index, score));
                    button.AddToClassList("el-button--rating");
                    ratingButtons[item, value - 1] = button;
                }
            }
            runnerMessage = Text("", "el-caution", section);
            sequenceSection = Section(section, "提示順の一覧（押すとその条件を読み込む）", false);
            sequenceList = new VisualElement();
            sequenceSection.Add(sequenceList);
        }

        void CreatePlan()
        {
            int? row = null;
            string text = rowField.value != null ? rowField.value.Trim() : "";
            if (text.Length > 0)
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < 1)
                {
                    SetText(runnerMessage, "順序は 1 以上の数字で入れてください（空なら参加者IDから決める）");
                    return;
                }
                row = number - 1;
            }
            runner.CreatePlan(participantField.value, row);
        }

        void BuildLimbSection(VisualElement parent)
        {
            var section = Section(parent, "設定する追加肢", true);
            var row = Row(section);
            rightButton = Btn(row, "右の追加肢（→）", () => session.SelectSide(LimbSide.Right), true);
            leftButton = Btn(row, "左の追加肢（←）", () => session.SelectSide(LimbSide.Left), true);
            twoLimbsButton = Btn(row, "2本目を出す（T）", () => session.SetTwoLimbs(!session.TwoLimbs), true);
            if (!session.HasLeftLimb)
            {
                leftButton.SetEnabled(false);
                twoLimbsButton.SetEnabled(false);
            }
        }

        void BuildStateSection(VisualElement parent)
        {
            stateSection = Section(parent, "入力と状態", true);
            inputLabel = Text("", null, stateSection);
            var sites = Row(stateSection);
            siteButtons.Clear();
            for (int i = 0; i < session.InputSites.Count; i++)
            {
                var site = session.InputSites[i];
                var button = Btn(sites, $"{i + 1} {BodySiteNames.Japanese(site)}", () => session.SelectInputSite(site));
                siteButtons.Add((button, site));
            }
            Text("Ctrl+1〜4 でも切り替えられる", "el-muted", stateSection);
            assignHint = Text("", null, stateSection);
            assignButton = Btn(stateSection, "", () => session.AssignRecentlyMovedTracker());
            assignMessage = Text("", "el-muted", stateSection);
            var reference = Row(stateSection);
            referenceLabel = Text("", null, reference);
            referenceLabel.style.flexGrow = 1;
            Btn(reference, "基準を切替", () => session.CycleReference());
            statusLabel = Text("", null, stateSection);
            forwardLabel = Text("", null, stateSection);
            forwardMessage = Text("", "el-muted", stateSection);
            uLabel = Text("", null, stateSection);
            var buttons = Row(stateSection);
            Btn(buttons, "Re-center（Space）", () => session.RequestRecenter(), true);
            Btn(buttons, "前を記録（F）", () => session.RequestRecordForward(), true);
        }

        void BuildMappingSection(VisualElement parent)
        {
            var section = Section(parent, "マッピングの型とゲイン", true);
            var row = Row(section);
            presetLabel = Text("", null, row);
            presetLabel.style.flexGrow = 1;
            nextPresetButton = Btn(row, "次の型（P）", () => session.CycleMappingPreset());
            axesLabel = Text("", null, section);
            var invert = Row(section);
            Text("反転", null, invert);
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
                invert.Add(toggle);
                invertToggles[axis] = toggle;
            }
            var gain = Row(section);
            gainLabel = Text("", null, gain);
            gainLabel.style.minWidth = 110;
            Btn(gain, "−0.5（↓）", () => ChangeGain(-0.5f));
            Btn(gain, "＋0.5（↑）", () => ChangeGain(0.5f));
        }

        void BuildAttachSection(VisualElement parent)
        {
            var section = Section(parent, "生やす位置（左右とも）", true);
            attachLabel = Text("", null, section);
            var row = Row(section);
            attachButtons.Clear();
            var mount = session.Primary?.Mount;
            if (mount == null)
            {
                attachLabel.text = "生やす位置：固定（LimbMountController がありません）";
                return;
            }
            var points = mount.Registry.Points;
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (point == null) continue;
                int index = i;
                var button = Btn(row, $"F{i + 1} {point.displayName}", () => session.SelectAttachPoint(index), true);
                attachButtons.Add((button, point));
            }
        }

        void BuildChannelSection(VisualElement parent)
        {
            channelSection = Section(parent, "入力の6つの値（体の軸、Re-center時を0）", false);
            var header = new VisualElement();
            header.AddToClassList("el-table-row");
            AddCell(header, "", "el-col-name");
            AddCell(header, "今", "el-col-num");
            AddCell(header, "最小", "el-col-num");
            AddCell(header, "最大", "el-col-num");
            AddCell(header, "", "el-col-unit");
            AddCell(header, "手先", null);
            channelSection.Add(header);
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
                channelSection.Add(row);
            }
            instructionLabel = Text("", "el-muted", channelSection);
            leverLabel = Text("", "el-muted", channelSection);
            Btn(channelSection, "範囲をリセット", () => session.Selected?.ResetChannelRange());
        }

        void BuildGraspSection(VisualElement parent)
        {
            var section = Section(parent, "把持（追加肢はトリガー、本来の手はグリップ、G は予備）", true);
            graspLabel = Text("", null, section);
        }

        void BuildBodySection(VisualElement parent)
        {
            var section = Section(parent, "体の寸法", false);
            var row = Row(section);
            Text("身長", null, row);
            heightField = Field(row);
            Text("cm　肩幅", null, row);
            shoulderField = Field(row);
            Text("cm", null, row);
            Btn(row, "反映", ApplyBodyDimensions);
            Btn(row, "モデルの大きさ", () =>
            {
                session.ResetBodyDimensions();
                FillBodyFields();
            });
            bodyMessage = Text("", "el-muted", section);
            FillBodyFields();
        }

        void BuildRecordSection(VisualElement parent)
        {
            var section = Section(parent, "記録", true);
            recordStatus = Text("", null, section);
            recordFolder = Text("", "el-muted", section);
            var row = Row(section);
            recordButton = Btn(row, "", () =>
            {
                if (recorder == null) return;
                if (recorder.IsRecording) recorder.StopRecording(ChangeOrigins.Panel);
                else recorder.StartRecording(ChangeOrigins.Panel);
            }, true);
            Btn(row, "マーカー（K）", () => recorder?.InsertMarker(ChangeOrigins.Panel), true);
            Btn(row, "設定を保存（Ctrl+S）", () => recorder?.SaveCurrentConfig(ChangeOrigins.Panel), true);
            recordMessage = Text("", "el-muted", section);
            if (recorder == null) recordStatus.text = "ExperimentRecorder がシーンにありません";
        }

        void BuildReplaySection(VisualElement parent)
        {
            if (replayer == null) return;
            replaySection = Section(parent, "ログの再生（Step 4-4）", false);
            replayFolderLabel = Text("", null, replaySection);
            var folderRow = Row(replaySection);
            Btn(folderRow, "新しい記録", () => replayer.SelectFolder(-1));
            Btn(folderRow, "古い記録", () => replayer.SelectFolder(1));
            Btn(folderRow, "一覧を更新", () => replayer.RefreshFolders());
            Btn(folderRow, "読み込む", () => replayer.BeginLoad(), true);
            replayStatusLabel = Text("", null, replaySection);
            var playRow = Row(replaySection);
            replayPlayButton = Btn(playRow, "再生", () => replayer.TogglePlay(), true);
            Btn(playRow, "−10秒", () => replayer.Skip(-10));
            Btn(playRow, "＋10秒", () => replayer.Skip(10));
            Btn(playRow, "止める（実機に戻す）", () => replayer.Stop());
            var speedRow = Row(replaySection);
            Text("速さ", null, speedRow);
            replaySpeedButtons.Clear();
            foreach (float value in new[] { 0.5f, 1f, 2f, 4f })
            {
                float speed = value;
                replaySpeedButtons.Add((Btn(speedRow, $"×{value.ToString("0.#", CultureInfo.InvariantCulture)}", () => replayer.Speed = speed), speed));
            }
            replayErrorLabel = Text("", "el-muted", replaySection);
            Text("再生中は、記録したトラッカーの動き・設定・Re-center が追加肢に入る（実機と仮想のトラッカーより優先）。止めると実機に戻り、設定は再生の最後のまま", "el-muted", replaySection);
        }

        void BuildPracticeSection(VisualElement parent)
        {
            if (practice == null) return;
            var section = Section(parent, "仮のボタン（動作確認用）", false);
            practiceLabel = Text("", null, section);
            var row = Row(section);
            Btn(row, "Re-centerして並べ直す", () => session.RequestRecenter());
            Btn(row, "立方体を戻す", () => practice.ResetCubes());
            Btn(row, "数を0に戻す", () => practice.ResetCounts());
        }

        void BuildDisplaySection(VisualElement parent)
        {
            var section = Section(parent, "モニタと HMD の表示", true);
            if (spectator != null)
            {
                var view = Row(section);
                viewLabel = Text("", null, view);
                viewLabel.style.flexGrow = 1;
                Btn(view, "次の視点（V）", () => spectator.NextView());
            }
            if (overlay != null)
            {
                overlayToggle = AddToggle(section, "モニタに可視化（基準点・変位・届く範囲・トリガー）", value => overlay.Show = value);
            }
            if (spectator != null)
            {
                overlayHmdToggle = AddToggle(section, "参加者（HMD）にも可視化を見せる（フェーズC）", value =>
                {
                    if (spectator.OverlayInHmd != value) Act(() => spectator.OverlayInHmd = value);
                });
            }
            markerToggle = AddToggle(section, "手首のIKターゲット（右は水色、左は桃色の球。モニタだけ）", value => session.ShowTargetMarker = value);
            if (mirror != null)
            {
                mirrorToggle = AddToggle(section, "鏡（M）", value =>
                {
                    if (mirror.Visible != value) Act(() => HotkeyMap.ToggleMirror(mirror, session));
                });
            }
            if (notice != null)
            {
                Text("HMD への通知", "el-muted", section);
                var custom = Row(section);
                noticeField = new TextField { value = "" };
                noticeField.AddToClassList("el-field");
                noticeField.style.width = 220;
                custom.Add(noticeField);
                Btn(custom, "HMD に出す", () =>
                {
                    if (!string.IsNullOrWhiteSpace(noticeField.value)) notice.Show(noticeField.value.Trim());
                });
                var presets = Row(section);
                Btn(presets, "条件が変わります", () => notice.Show("条件が変わります"));
                Btn(presets, "カウントダウン（3, 2, 1）", () => notice.Countdown());
                Btn(presets, "消す", () => notice.Clear());
                noticeLabel = Text("", "el-muted", section);
            }
        }

        Toggle AddToggle(VisualElement parent, string text, Action<bool> onChange)
        {
            var toggle = new Toggle { text = text, focusable = false };
            toggle.AddToClassList("el-toggle");
            toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            parent.Add(toggle);
            return toggle;
        }

        void BuildHotkeySection(VisualElement parent)
        {
            var section = Section(parent, "ホットキー", false);
            hotkeyList = new VisualElement();
            section.Add(hotkeyList);
            Text("割り当ては Resources/ExtraLimbUI/ExperimenterHotkeys.inputactions で変えられる。Game ビューの「Unity Shortcuts」はオフにしておく", "el-muted", section);
            FillHotkeys();
        }

        void FillHotkeys()
        {
            if (hotkeyList == null) return;
            hotkeyList.Clear();
            if (hotkeys == null)
            {
                hotkeyList.Add(Text("HotkeyMap がシーンにありません", "el-warn"));
                return;
            }
            foreach (var entry in hotkeys.Entries())
            {
                var row = new VisualElement();
                row.AddToClassList("el-hotkey-row");
                var key = Text(entry.keys, "el-hotkey-key", row);
                key.style.whiteSpace = WhiteSpace.NoWrap;
                Text(entry.description, null, row);
                hotkeyList.Add(row);
            }
        }

        void FillBodyFields()
        {
            var body = session.Body;
            if (body == null || !body.IsReady || heightField == null) return;
            heightField.SetValueWithoutNotify((body.Calibration.Height * 100f).ToString("F0", CultureInfo.InvariantCulture));
            shoulderField.SetValueWithoutNotify((body.Calibration.ShoulderWidth * 100f).ToString("F0", CultureInfo.InvariantCulture));
        }

        void ApplyBodyDimensions()
        {
            if (TryParse(heightField.value, out float height) && TryParse(shoulderField.value, out float shoulder))
                session.ApplyBodyDimensions(height, shoulder);
            else
                bodyMessage.text = "数字で入れてください";
        }

        void ChangeGain(float delta)
        {
            var channel = session.Selected;
            if (channel != null) session.SetPositionGain(channel.Config.positionGain + delta);
        }

        static bool TryParse(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.TryParse(text, out value);

        // ---- 部品 ----

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

        static TextField Field(VisualElement parent)
        {
            var field = new TextField { isDelayed = false };
            field.AddToClassList("el-field");
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

        // ---- 表示の更新 ----

        void Refresh()
        {
            if (!built || session == null) return;
            RefreshBanner();
            if (!visible) return;
            RefreshRunner();
            var channel = session.Selected;
            RefreshLimbs(channel);
            if (channel == null) return;
            RefreshState(channel);
            RefreshMapping(channel);
            RefreshAttach(channel);
            if (channelSection != null && channelSection.value) RefreshChannels(channel);
            RefreshGrasp();
            RefreshBody();
            RefreshRecord();
            RefreshReplay();
            RefreshPractice();
            RefreshDisplay();
        }

        // 上の表示と「条件の進行」の欄に出す、今の段階
        string RunnerPhaseText()
        {
            if (runner == null) return "";
            string prefix = runner.IsPractice ? "練習：" : "";
            switch (runner.Phase)
            {
                case TrialPhase.Ready: return "準備：Re-center → 動かし方を伝える → 開始（Enter）";
                case TrialPhase.Lead: return $"{prefix}まもなく前半（あと {Mathf.CeilToInt((float)runner.SecondsToSegmentStart)} 秒）";
                case TrialPhase.Segment1: return $"{prefix}前半（追加肢だけ）　残り {Clock(runner.SegmentRemaining)}";
                case TrialPhase.Interlude: return $"{prefix}まもなく後半（あと {Mathf.CeilToInt((float)runner.SecondsToSegmentStart)} 秒）";
                case TrialPhase.Segment2: return $"{prefix}後半（本来の腕も使う）　残り {Clock(runner.SegmentRemaining)}";
                case TrialPhase.Rating:
                    int cursor = runner.Rating.Cursor;
                    return cursor >= 0 ? $"口頭評価：「{ConditionRating.ItemNames[cursor]}」（{cursor + 1}/3）" : "口頭評価";
                case TrialPhase.Done: return "終了。次へ（PgDn）";
                case TrialPhase.Break: return $"休憩 {Clock(runner.BreakElapsed)}（HMDを外す）。次へ（PgDn）で次のブロック";
                case TrialPhase.Finished: return "すべての条件を終えました";
                default: return runner.HasPlan ? "次へ（PgDn）で1つ目を読み込む" : "提示順がありません";
            }
        }

        // 上の大きな表示用の短い形（離れても読めるように）
        string RunnerBannerText()
        {
            string prefix = runner.IsPractice ? "練習：" : "";
            switch (runner.Phase)
            {
                case TrialPhase.Ready: return "準備";
                case TrialPhase.Lead: return $"{prefix}まもなく前半 {Mathf.CeilToInt((float)runner.SecondsToSegmentStart)}";
                case TrialPhase.Segment1: return $"{prefix}前半（追加肢だけ）{Clock(runner.SegmentRemaining)}";
                case TrialPhase.Interlude: return $"{prefix}まもなく後半 {Mathf.CeilToInt((float)runner.SecondsToSegmentStart)}";
                case TrialPhase.Segment2: return $"{prefix}後半（両手も）{Clock(runner.SegmentRemaining)}";
                case TrialPhase.Rating:
                    int cursor = runner.Rating.Cursor;
                    return cursor >= 0 ? $"評価 {cursor + 1}/3" : "評価";
                case TrialPhase.Done: return "終了";
                case TrialPhase.Break: return $"休憩 {Clock(runner.BreakElapsed)}";
                case TrialPhase.Finished: return "すべて終了";
                default: return "";
            }
        }

        static string Clock(double seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        void RefreshBanner()
        {
            if (bannerRunner != null)
            {
                var spec = runner != null ? runner.Current : null;
                bool show = runner != null && (runner.HasPlan || runner.IsPractice);
                Show(bannerRunner, show);
                if (show)
                {
                    string head = runner.IsPractice ? "練習" : spec != null ? $"条件 {spec.order + 1}/{runner.Plan.Count}（{spec.Code}）" : $"{runner.Plan.participantId}";
                    SetText(bannerRunner, $"{head}　{RunnerBannerText()}");
                    bannerRunner.EnableInClassList("el-ok", runner.Segment > 0);
                    bannerRunner.EnableInClassList("el-caution", runner.Phase == TrialPhase.Rating || runner.Phase == TrialPhase.Break);
                }
            }
            var channels = session.Channels;
            int line = 0;
            foreach (var channel in channels)
            {
                if (!channel.Active || line >= bannerLines.Count) continue;
                var label = bannerLines[line++];
                Show(label, true);
                var preset = MappingPresets.Find(session.MappingPresetList, channel.Config.mappingPresetId);
                string attach = channel.Mount != null && channel.Mount.Current != null ? channel.Mount.Current.displayName : "—";
                string text = $"{channel.Label}　{attach}｜{BodySiteNames.Japanese(channel.Config.inputSite)}｜{(preset != null ? preset.displayName : "型なし")}　Gp {channel.Config.positionGain:F1}";
                if (!channel.IsInputValid) text += "　追跡切れ";
                SetText(label, text);
                label.EnableInClassList("el-warn", !channel.IsInputValid);
            }
            for (; line < bannerLines.Count; line++) Show(bannerLines[line], false);

            var sub = new StringBuilder();
            if (recorder != null && recorder.IsRecording)
            {
                var elapsed = TimeSpan.FromSeconds(recorder.ElapsedSeconds);
                sub.Append($"<color=#ff5a50>●</color> 記録中 {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}　版 {session.Revision}　マーカー {recorder.MarkerCount}");
            }
            else
            {
                sub.Append("記録していません（L で始める）");
            }
            if (session.Selected != null && session.HasLeftLimb) sub.Append($"　設定中：{session.Selected.Label}の追加肢");
            if (notice != null && notice.CurrentText.Length > 0) sub.Append($"　<color=#ffd678>HMD：{notice.CurrentText}</color>");
            if (replayer != null && replayer.IsLoaded) sub.Append($"　<color=#8fd3ff>ログの再生 {(replayer.IsPlaying ? "▶" : "Ⅱ")} {Clock(replayer.Position)}</color>");
            SetText(bannerSub, sub.ToString());
            bannerSub.EnableInClassList("el-warn", recorder != null && !recorder.IsRecording);
        }

        void RefreshRunner()
        {
            if (runner == null || conditionLabel == null) return;
            var plan = runner.Plan;
            SetText(planLabel, plan != null
                ? $"{plan.participantId}　順序 {plan.latinRow + 1}/{plan.latinRowCount}：{plan.BlockOrderText}　全{plan.Count}条件（終了 {runner.DoneCount}）"
                : "提示順はまだありません");
            var spec = runner.Current;
            SetText(conditionLabel, spec != null
                ? $"条件 {spec.order + 1}/{plan.Count}（ブロック {spec.block + 1}/{plan.BlockCount} の {spec.indexInBlock + 1}/{spec.blockSize}）\n{spec.Label}"
                : plan != null ? "次へ（PgDn）で1つ目の条件を読み込む" : "参加者IDを入れて「提示順を作る」");

            var phase = runner.Phase;
            SetText(phaseLabel, "段階：" + RunnerPhaseText());
            phaseLabel.EnableInClassList("el-ok", runner.Segment > 0);
            phaseLabel.EnableInClassList("el-caution", phase == TrialPhase.Rating || phase == TrialPhase.Break);

            bool mismatch = spec != null && !runner.IsPractice && (phase == TrialPhase.Ready || runner.IsRunning) && !runner.MatchesCurrent;
            bool beforeOrDuring = phase == TrialPhase.Ready || runner.IsRunning;
            string recenter = spec == null || !beforeOrDuring ? "" : runner.RecenterRequestsSinceLoad > 0 ? "読み込み後の Re-center：済み" : "読み込み後の Re-center：まだ（Space）";
            SetText(recenterLabel, mismatch ? recenter + "　今の設定が条件と違います（読み込み直すときは R）" : recenter);
            Show(recenterLabel, recenter.Length > 0);
            recenterLabel.EnableInClassList("el-caution", runner.RecenterRequestsSinceLoad == 0 && !mismatch);
            recenterLabel.EnableInClassList("el-warn", mismatch);
            recenterLabel.EnableInClassList("el-ok", runner.RecenterRequestsSinceLoad > 0 && !mismatch);

            var preset = spec != null ? MappingPresets.Find(session.MappingPresetList, spec.mappingPresetId) : null;
            SetText(conditionInstruction, preset != null && preset.instruction.Length > 0 ? $"動かし方：{preset.instruction}" : "");
            Show(conditionInstruction, conditionInstruction.text.Length > 0);

            bool running = runner.IsRunning;
            prevButton.SetEnabled(runner.HasPlan && !running && runner.CurrentIndex > 0);
            nextButton.SetEnabled(runner.HasPlan && !running && runner.CurrentIndex + 1 < plan.Count);
            startButton.SetEnabled(phase == TrialPhase.Ready);
            abortButton.SetEnabled(running || phase == TrialPhase.Rating);
            redoButton.SetEnabled(spec != null && !running);
            practiceButton.SetEnabled(!running && phase != TrialPhase.Rating);

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
            SetText(runnerMessage, runner.Message);
            Show(runnerMessage, runner.Message.Length > 0);
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
                SetText(sequenceButtons[i], $"{mark} {i + 1}（{spec.Code}）{spec.Label}" + (attempts > 1 ? $"　{attempts}回" : ""));
                sequenceButtons[i].EnableInClassList(SelectedClass, i == runner.CurrentIndex);
                sequenceButtons[i].SetEnabled(!runner.IsRunning);
            }
        }

        void RefreshLimbs(LimbChannel channel)
        {
            rightButton.EnableInClassList(SelectedClass, channel != null && channel.Side == LimbSide.Right);
            leftButton.EnableInClassList(SelectedClass, channel != null && channel.Side == LimbSide.Left);
            if (session.HasLeftLimb) leftButton.SetEnabled(session.TwoLimbs);
            SetText(twoLimbsButton, session.TwoLimbs ? "2本目を隠す（T）" : "2本目を出す（T）");
            stateSection.text = $"入力と状態（{(channel != null ? channel.Label : "—")}の追加肢）";
        }

        void RefreshState(LimbChannel channel)
        {
            var config = channel.Config;
            var registry = session.Registry;
            SetText(inputLabel, $"入力：{session.DescribeSource(channel.InputId)}");
            for (int index = 0; index < siteButtons.Count; index++)
            {
                var (button, site) = siteButtons[index];
                bool available = registry != null && SourceSelector.SelectInput(registry, "", site) != null;
                SetText(button, $"{index + 1} {BodySiteNames.Japanese(site)}{(available ? "" : "×")}");
                button.EnableInClassList(SelectedClass, site == config.inputSite && string.IsNullOrEmpty(config.inputSourceId));
            }

            // 振ったトラッカーを、設定している追加肢の入力の部位に割り当てる
            string siteName = BodySiteNames.Japanese(config.inputSite);
            string recent = session.RecentlyMovedTracker;
            ITrackedPoseSource tracker = null;
            if (recent != null && (registry == null || !registry.TryGet(recent, out tracker))) recent = null;
            if (recent != null && tracker.Site == config.inputSite)
            {
                SetText(assignHint, $"振ったトラッカー {recent} は「{siteName}」です");
                assignHint.EnableInClassList("el-caution", false);
                Show(assignButton, false);
            }
            else if (recent != null)
            {
                Show(assignHint, false);
                SetText(assignButton, $"振ったトラッカー {recent}（今：{BodySiteNames.Japanese(tracker.Site)}）を「{siteName}」にする");
                Show(assignButton, true);
            }
            else
            {
                Show(assignButton, false);
                bool missing = channel.InputId == null;
                SetText(assignHint, missing
                    ? $"{siteName}のトラッカーを振ると、割り当てのボタンが出ます"
                    : $"割り当て直すときは、{siteName}のトラッカーを振ってください");
                assignHint.EnableInClassList("el-caution", missing);
            }
            if (recent == null || tracker.Site == config.inputSite) Show(assignHint, true);
            SetText(assignMessage, session.AssignMessage);
            Show(assignMessage, session.AssignMessage.Length > 0);

            SetText(referenceLabel, $"基準：{session.DescribeSource(channel.ReferenceId)}");
            SetText(statusLabel, $"状態：{channel.StatusMessage}（{(session.TickedAfterBody ? "体を解いた直後に計算" : "LateUpdateで計算")}）");
            statusLabel.EnableInClassList("el-warn", !channel.IsInputValid);
            statusLabel.EnableInClassList("el-ok", channel.IsInputValid);
            bool hasForward = channel.Pipeline.BodyFrame.HasForward;
            SetText(forwardLabel, hasForward ? "前の向き：記録済み" : "前の向き：未記録（基準の軸で代用）。入力の部位を前へ出して F");
            forwardLabel.EnableInClassList("el-caution", !hasForward);
            SetText(forwardMessage, channel.ForwardMessage);
            Show(forwardMessage, channel.ForwardMessage.Length > 0);
            var u = channel.Pipeline.LastDisplacement * 100f;
            bool edge = channel.Pipeline.AtWorkspaceBoundary;
            SetText(uLabel, $"型を通した入力 u：右 {u.x,6:F1}　上 {u.y,6:F1}　前 {u.z,6:F1} cm" + (edge ? "　手先が範囲の端" : ""));
            uLabel.EnableInClassList("el-caution", edge);
        }

        void RefreshMapping(LimbChannel channel)
        {
            var config = channel.Config;
            var presets = MappingPresets.ForSite(session.MappingPresetList, config.inputSite);
            var preset = MappingPresets.Find(presets, config.mappingPresetId);
            string count = presets.Count > 1 && preset != null ? $"（{presets.IndexOf(preset) + 1}/{presets.Count}）" : "";
            SetText(presetLabel, $"型：{(preset != null ? preset.displayName : "なし")}{count}");
            nextPresetButton.SetEnabled(presets.Count > 1);
            var mapping = config.axisMapping;
            SetText(axesLabel, $"手先の右←{mapping.DescribeAxis(0)}　上←{mapping.DescribeAxis(1)}　前←{mapping.DescribeAxis(2)}");
            for (int axis = 0; axis < 3; axis++)
            {
                invertToggles[axis].SetValueWithoutNotify(mapping.IsInverted(axis));
                invertToggles[axis].SetEnabled(mapping[axis].IsUsed);
            }
            SetText(gainLabel, $"ゲイン Gp：{config.positionGain:F1}");
        }

        void RefreshAttach(LimbChannel channel)
        {
            var mount = channel.Mount;
            if (mount == null) return;
            var current = mount.Current;
            SetText(attachLabel, $"今：{(current != null ? current.displayName : "—")}" + (session.TwoLimbs ? "（左右とも）" : "") + (mount.IsTransitioning ? "（付け替え中）" : ""));
            foreach (var (button, point) in attachButtons) button.EnableInClassList(SelectedClass, point == current);
        }

        void RefreshChannels(LimbChannel channel)
        {
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
            SetText(instructionLabel, preset != null && preset.instruction.Length > 0 ? $"動かし方：{preset.instruction}" : "");
            SetText(leverLabel, mapping.UsesRotation
                ? $"回転は、てこ {mapping.leverLength * 100f:F0} cm で長さに直す（10° で {mapping.leverLength * 10f * Mathf.Deg2Rad * 100f:F1} cm）"
                : "");
            channelSection.text = $"入力の6つの値（{channel.Label}の追加肢、{BodySiteNames.Japanese(channel.Config.inputSite)}、Re-center時を0）";
        }

        void RefreshGrasp()
        {
            var grasp = session.Grasp;
            if (grasp == null)
            {
                SetText(graspLabel, "GraspRig がありません");
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
                text.Append($"{channel.Label}の追加肢：{side}トリガー {grasp.TriggerValue(channel):F2}{(grasper.Pressed ? "（掴む）" : "")}　{(grasper.Held != null ? grasper.Held.name : "なし")}");
            }
            if (grasp.LeftHand != null || grasp.RightHand != null)
            {
                text.Append('\n');
                text.Append($"本来の手：左グリップ {grasp.GripValue(ControllerHand.Left):F2} {(grasp.LeftHand != null && grasp.LeftHand.Held != null ? grasp.LeftHand.Held.name : "なし")}");
                text.Append($"　右グリップ {grasp.GripValue(ControllerHand.Right):F2} {(grasp.RightHand != null && grasp.RightHand.Held != null ? grasp.RightHand.Held.name : "なし")}");
            }
            if (grasp.KeyPressed) text.Append("\nG を押しています");
            SetText(graspLabel, text.ToString());
        }

        void RefreshBody()
        {
            if (bodyMessage != null) SetText(bodyMessage, session.BodyMessage);
        }

        void RefreshRecord()
        {
            if (recorder == null || recordButton == null) return;
            if (recorder.IsRecording)
            {
                var elapsed = TimeSpan.FromSeconds(recorder.ElapsedSeconds);
                var logger = recorder.Logger;
                SetText(recordStatus, $"<color=#ff5a50>●</color> 記録中 {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}　フレーム {logger.FrameRows}　イベント {logger.EventRows}　マーカー {recorder.MarkerCount}　版 {session.Revision}");
                SetText(recordFolder, recorder.CurrentDirectory);
            }
            else
            {
                SetText(recordStatus, "記録していません");
                SetText(recordFolder, $"保存先：{recorder.RootDirectory}");
            }
            recordStatus.EnableInClassList("el-warn", !recorder.IsRecording);
            SetText(recordButton, recorder.IsRecording ? "記録を止める（L）" : "記録を始める（L）");
            SetText(recordMessage, recorder.Message);
        }

        void RefreshReplay()
        {
            if (replayer == null || replaySection == null || !replaySection.value) return;
            var folders = replayer.Folders;
            string selected = replayer.SelectedFolder;
            SetText(replayFolderLabel, selected != null
                ? $"記録：{System.IO.Path.GetFileName(selected)}（新しい方から {replayer.SelectedFolderIndex + 1}/{folders.Count}）"
                : $"記録がありません（{replayer.Root}）");
            var log = replayer.Log;
            string status;
            if (replayer.IsLoading) status = $"読み込み中 {replayer.LoadProgress * 100f:F0}%：{System.IO.Path.GetFileName(replayer.LoadingDirectory)}";
            else if (replayer.Error.Length > 0) status = replayer.Error;
            else if (log == null) status = "再生していません";
            else
            {
                status = $"{(replayer.IsPlaying ? "再生中" : "一時停止")} {Clock(replayer.Position)} / {Clock(replayer.Duration)}（×{replayer.Speed.ToString("0.#", CultureInfo.InvariantCulture)}）　{System.IO.Path.GetFileName(log.Directory)}";
                if (!log.HasSavedState) status += "\n古い記録（基準姿勢と前の向きなし）：Re-center の時刻にその場で合わせる近似の再生";
            }
            SetText(replayStatusLabel, status);
            replayStatusLabel.EnableInClassList("el-warn", replayer.Error.Length > 0);
            replayStatusLabel.EnableInClassList("el-ok", replayer.IsPlaying);
            SetText(replayPlayButton, replayer.IsPlaying ? "一時停止" : "再生");
            replayPlayButton.SetEnabled(log != null);
            foreach (var (button, speed) in replaySpeedButtons) button.EnableInClassList(SelectedClass, Mathf.Approximately(replayer.Speed, speed));
            SetText(replayErrorLabel, replayer.ErrorSamples > 0
                ? $"記録との差（右の追加肢の u）：平均 {replayer.MeanErrorMm:F1} mm、最大 {replayer.MaxErrorMm:F1} mm（{replayer.ErrorSamples} フレーム）"
                : log != null ? "記録との差：再生すると出る" : "");
        }

        void RefreshPractice()
        {
            if (practice == null || practiceLabel == null) return;
            SetText(practiceLabel, $"押した数 {practice.Hits}　直前 {practice.LastSeconds:F2} 秒　対象「{practice.TargetLabel}」　立方体を掴んだ数 {practice.Grabs}");
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
                string where = notice.OnHmd ? "HMD" : "HMD のカメラがないので、確認用にモニタ";
                SetText(noticeLabel, notice.CurrentText.Length > 0 ? $"表示中（{where}）：{notice.CurrentText}" : "表示していません");
            }
        }
    }
}
