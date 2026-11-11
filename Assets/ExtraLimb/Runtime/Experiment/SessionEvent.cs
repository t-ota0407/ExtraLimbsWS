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

        /// <summary>実験者のキー（例：key:F1、key:Ctrl+2）。</summary>
        public static string Key(string name) => "key:" + name;
    }
}
