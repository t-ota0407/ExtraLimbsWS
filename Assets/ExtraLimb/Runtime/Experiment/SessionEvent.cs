namespace ExtraLimb.Experiment
{
    /// <summary>
    /// イベントログ（events.csv）の1行（Step 4-1）。設定の変更は項目ごとに1行（key＝項目のパス、old・new＝変更前後の値）。
    /// </summary>
    public readonly struct SessionEvent
    {
        /// <summary>種類（SessionEventTypes）。</summary>
        public readonly string type;

        /// <summary>対象：right / left（追加肢）、both（両方）、hand_left / hand_right（本来の手）、session、トラッカーの部位など。</summary>
        public readonly string subject;

        /// <summary>何で起きたか（ChangeOrigins）：panel、key:F1、api、system、trigger など。</summary>
        public readonly string origin;

        public readonly string key;
        public readonly string oldValue;
        public readonly string newValue;

        /// <summary>人が読む説明。</summary>
        public readonly string message;

        public SessionEvent(string type, string subject, string origin, string key = "", string oldValue = "", string newValue = "", string message = "")
        {
            this.type = type ?? "";
            this.subject = subject ?? "";
            this.origin = origin ?? "";
            this.key = key ?? "";
            this.oldValue = oldValue ?? "";
            this.newValue = newValue ?? "";
            this.message = message ?? "";
        }

        public override string ToString() => $"[{type}] {subject} {key}: {oldValue} → {newValue}（{origin}）{message}";
    }

    /// <summary>イベントの種類。</summary>
    public static class SessionEventTypes
    {
        /// <summary>設定の1項目が変わった（key＝項目のパス）。</summary>
        public const string Config = "config";

        /// <summary>2本目を出した・隠した（old・new＝true / false）。</summary>
        public const string TwoLimbs = "two_limbs";

        /// <summary>パネルとホットキーで設定する追加肢を選んだ。</summary>
        public const string Select = "select";

        /// <summary>Re-center を頼んだ（Space、パネルなど）。</summary>
        public const string RecenterRequest = "recenter_request";

        /// <summary>Re-center した（入力が変わったときの自動も含む）。</summary>
        public const string Recenter = "recenter";

        /// <summary>前の記録を頼んだ（F、パネル）。</summary>
        public const string ForwardRequest = "forward_request";

        /// <summary>前を記録した（new＝体の前の向き、基準の座標）。</summary>
        public const string Forward = "forward";

        /// <summary>前を記録できなかった（動きが小さい）。</summary>
        public const string ForwardFailed = "forward_failed";

        /// <summary>入力・基準のソースが変わった（key＝input / reference）。</summary>
        public const string Sources = "sources";

        /// <summary>入力・基準の追跡が切れた・戻った（old・new＝1 / 0）。</summary>
        public const string Tracking = "tracking";

        /// <summary>Attach Point への付け替えが終わった。</summary>
        public const string Mount = "mount";

        /// <summary>トラッカーを部位に割り当てた（key＝部位、old＝前にその部位だったトラッカー、new＝割り当てたトラッカー）。</summary>
        public const string TrackerAssign = "tracker_assign";

        /// <summary>フレームCSVの各部位の列に入れるトラッカーが変わった（subject＝部位、new＝ID）。</summary>
        public const string TrackerMap = "tracker_map";

        /// <summary>身長・肩幅を変えた（key＝height_cm / shoulder_width_cm）。</summary>
        public const string Body = "body";

        /// <summary>掴んだ（key＝物の名前）。</summary>
        public const string Grasp = "grasp";

        /// <summary>放した（key＝物の名前）。</summary>
        public const string Release = "release";

        /// <summary>実験者のマーカー（key＝通し番号）。</summary>
        public const string Marker = "marker";

        /// <summary>今の設定を JSON に保存した（new＝ファイル名）。</summary>
        public const string ConfigSaved = "config_saved";

        /// <summary>鏡を出した・隠した（old・new＝true / false）。</summary>
        public const string Mirror = "mirror";

        /// <summary>HMD に通知を出した（new＝出した文）。</summary>
        public const string HmdNotice = "hmd_notice";

        /// <summary>実験者のための可視化を参加者（HMD）にも見せる・見せない（old・new＝true / false）。</summary>
        public const string OverlayInHmd = "overlay_hmd";

        /// <summary>参加者IDから提示順を作った（key＝参加者ID、new＝ラテン方格の行とブロックの順）。Step 4-3。</summary>
        public const string Sequence = "sequence";

        /// <summary>デモモードと実験モードを切り替えた（old・new＝demo / experiment）。</summary>
        public const string Mode = "mode";

        /// <summary>実験モードで入力部位に使うマッピングの型を選んだ（key＝部位、old・new＝型のID）。</summary>
        public const string ExperimentMapping = "experiment_mapping";

        /// <summary>トラッカーの割り当てを付け替えた（new＝付け替えた部位の組。4台を左右2部位ずつ使うので、後半のブロックの前に付け替える）。</summary>
        public const string TrackerSwap = "tracker_swap";

        /// <summary>条件を読み込んだ（key＝提示順の番号（1 始まり）、new＝条件の中身）。</summary>
        public const string Condition = "condition";

        /// <summary>課題を始めた（カウントダウンの前。subject＝condition / practice、key＝番号、new＝何回目か）。</summary>
        public const string TrialStart = "trial_start";

        /// <summary>前半・後半が始まった（key＝1 / 2）。</summary>
        public const string SegmentStart = "segment_start";

        /// <summary>前半・後半が終わった（key＝1 / 2、new＝到達数（課題がなければ空））。</summary>
        public const string SegmentEnd = "segment_end";

        /// <summary>課題を中止した（key＝番号、old＝止めたときの段階）。</summary>
        public const string TrialAbort = "trial_abort";

        /// <summary>口頭評価を入れた（key＝項目 ease / body / fatigue、new＝1〜7）。</summary>
        public const string Rating = "rating";

        /// <summary>条件を終えた（評価まで入れた。key＝番号、new＝評価の3項目）。</summary>
        public const string ConditionDone = "condition_done";

        /// <summary>入力部位のブロックの間の休憩に入った（key＝終えたブロック（1 始まり））。</summary>
        public const string Break = "break";

        /// <summary>すべての条件を終えた。</summary>
        public const string Finished = "finished";

        /// <summary>ログの再生を始めた・やめた（key＝start / stop、new＝再生する記録のフォルダ名）。Step 4-4。</summary>
        public const string Replay = "replay";

        /// <summary>
        /// リーチング課題の円（Step 5-1）。key＝place（置いた・置き直した）か current（円を置いた後に記録を始めた）、
        /// new＝中心・前の向き・半径・ターゲットの数と大きさ。
        /// </summary>
        public const string ReachRing = "reach_ring";

        /// <summary>当たる範囲（効果器の球）を見せるかの切り替え（key＝show、new＝1/0。2026-11-13）。</summary>
        public const string ReachProbes = "reach_probes";

        /// <summary>トラッカーの確認（key＝start／site（部位、new＝トラッカーの ID）／done／cancel。2026-11-15）。</summary>
        public const string TrackerCheck = "tracker_check";

        /// <summary>Re-center のときに見つけたトラッカーの付け間違いの疑い（new＝文。2026-11-15）。</summary>
        public const string TrackerLayout = "tracker_layout";

        /// <summary>課題の開始で、参加者が止まるのを待つ（key＝start／still（止まった）／manual（実験者が Re-center した）。2026-11-15）。</summary>
        public const string Settle = "settle";

        public const string LogStart = "log_start";
        public const string LogStop = "log_stop";
    }

    /// <summary>何で起きたか。</summary>
    public static class ChangeOrigins
    {
        /// <summary>実験者パネルのボタン。</summary>
        public const string Panel = "panel";

        /// <summary>ほかのスクリプトから（後の条件の自動切替など）。</summary>
        public const string Api = "api";

        /// <summary>システムが自動で（起動時、入力が変わったときの Re-center など）。</summary>
        public const string System = "system";

        /// <summary>参加者のコントローラーのトリガー。</summary>
        public const string Trigger = "trigger";

        /// <summary>参加者のコントローラーのグリップ。</summary>
        public const string Grip = "grip";

        /// <summary>記録の再生（Step 4-4）。</summary>
        public const string Replay = "replay";

        /// <summary>課題の開始のとき、参加者が止まってから自動で（Re-center。2026-11-15）。</summary>
        public const string TrialStart = "start";

        /// <summary>実験者のキー（例：key:F1、key:Ctrl+2）。</summary>
        public static string Key(string name) => "key:" + name;
    }
}
