using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 実験の記録をファイルに書く（Step 4-1）。記録1回（セッション）ごとにフォルダを作り、次のファイルを書く。
    /// <list type="bullet">
    /// <item>frames.csv：毎フレームの状態（列は呼ぶ側が決める）。</item>
    /// <item>events.csv：設定の変更・Re-center・把持・マーカーなど（EventColumns の列）。</item>
    /// <item>config_history.jsonl：設定の全体。記録の開始時と、設定が変わるたびに1行。</item>
    /// <item>session.json：開始・終了の時刻、行数など（SessionInfo）。</item>
    /// </list>
    /// CSV は UTF-8（BOM 付き。Excel で開いても文字化けしない）。イベントは1行ごと、フレームは Flush で書き出す。
    /// MonoBehaviour から切り離してあるので、EditMode でファイルの中身をテストできる。
    /// </summary>
    public class ExperimentLogger : IDisposable
    {
        public const string FramesFileName = "frames.csv";
        public const string EventsFileName = "events.csv";
        public const string ConfigHistoryFileName = "config_history.jsonl";
        public const string SessionFileName = "session.json";

        /// <summary>events.csv の列。time は記録の開始からの秒、unix_ms は UNIX 時刻（ミリ秒）、clock はその PC の時計。</summary>
        public static readonly string[] EventColumns = { "time", "unix_ms", "clock", "frame", "type", "subject", "origin", "key", "old", "new", "message" };

        static readonly UTF8Encoding Utf8WithBom = new UTF8Encoding(true);
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        StreamWriter frames, events, configHistory;
        readonly CsvRow eventRow = new CsvRow();
        readonly SessionInfo info;

        /// <summary>このセッションのフォルダ（絶対パス）。</summary>
        public string Directory { get; }

        public bool IsOpen => frames != null;
        public int FrameRows { get; private set; }
        public int EventRows { get; private set; }
        public int ConfigRows { get; private set; }

        /// <summary>frames.csv の列の数。</summary>
        public int FrameColumnCount { get; }

        /// <summary>記録を始めたときのこの PC の時刻。</summary>
        public DateTime StartedAt { get; }

        /// <summary>既定の保存先：プロジェクトのフォルダ（ビルドでは exe のフォルダ）の ExperimentData。リポジトリでは gitignore 対象。</summary>
        public static string DefaultRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ExperimentData"));

        /// <summary>フォルダ名：日時（yyyyMMdd_HHmmss）と、あれば名前（参加者IDなど。ファイル名に使えない文字は _ にする）。</summary>
        public static string SessionName(DateTime time, string label = null)
        {
            string name = time.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string safe = Sanitize(label);
            return safe.Length > 0 ? $"{name}_{safe}" : name;
        }

        /// <summary>ファイル名に使えない文字と空白を _ にする。</summary>
        public static string Sanitize(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var text = new StringBuilder(label.Trim().Length);
            foreach (char c in label.Trim()) text.Append(Array.IndexOf(invalid, c) >= 0 || char.IsWhiteSpace(c) ? '_' : c);
            return text.ToString();
        }

        /// <summary>
        /// root の下にセッションのフォルダを作って書き始める。同じ名前のフォルダがあれば _2、_3 … を付ける。
        /// </summary>
        public ExperimentLogger(string root, string sessionName, IReadOnlyList<string> frameColumns, SessionInfo sessionInfo = null)
        {
            if (string.IsNullOrEmpty(root)) root = DefaultRoot;
            if (string.IsNullOrEmpty(sessionName)) sessionName = SessionName(DateTime.Now);
            string directory = Path.Combine(root, sessionName);
            for (int i = 2; System.IO.Directory.Exists(directory); i++) directory = Path.Combine(root, $"{sessionName}_{i}");
            System.IO.Directory.CreateDirectory(directory);
            Directory = Path.GetFullPath(directory);
            StartedAt = DateTime.Now;
            FrameColumnCount = frameColumns?.Count ?? 0;

            frames = Create(FramesFileName, Utf8WithBom, autoFlush: false);
            frames.WriteLine(CsvRow.Join(frameColumns ?? Array.Empty<string>()));
            events = Create(EventsFileName, Utf8WithBom, autoFlush: true);
            events.WriteLine(CsvRow.Join(EventColumns));
            configHistory = Create(ConfigHistoryFileName, Utf8, autoFlush: true);

            info = sessionInfo ?? new SessionInfo();
            info.sessionName = Path.GetFileName(Directory);
            info.startedAt = StartedAt.ToString("o", CultureInfo.InvariantCulture);
            info.startedUnixMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            info.frameColumns = new List<string>(frameColumns ?? Array.Empty<string>()).ToArray();
            info.eventColumns = EventColumns;
            WriteSessionInfo();
        }

        StreamWriter Create(string fileName, Encoding encoding, bool autoFlush)
        {
            var stream = new FileStream(Path.Combine(Directory, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            return new StreamWriter(stream, encoding, 1 << 16) { AutoFlush = autoFlush, NewLine = "\n" };
        }

        /// <summary>frames.csv に1行書く。</summary>
        public void WriteFrame(CsvRow row)
        {
            if (frames == null || row == null) return;
            frames.Write(row.ToString());
            frames.Write('\n');
            FrameRows++;
        }

        /// <summary>events.csv に1行書く（すぐにファイルへ書き出す）。</summary>
        public void WriteEvent(double time, int frame, in SessionEvent e)
        {
            if (events == null) return;
            var now = DateTimeOffset.Now;
            eventRow.Clear()
                .Add(time, 4)
                .Add(now.ToUnixTimeMilliseconds())
                .Add(now.LocalDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Add(frame)
                .Add(e.type).Add(e.subject).Add(e.origin).Add(e.key).Add(e.oldValue).Add(e.newValue).Add(e.message);
            events.Write(eventRow.ToString());
            events.Write('\n');
            EventRows++;
        }

        /// <summary>config_history.jsonl に1行書く（改行を含まない JSON）。</summary>
        public void WriteConfig(string json)
        {
            if (configHistory == null || string.IsNullOrEmpty(json)) return;
            configHistory.Write(json.Replace("\r", "").Replace("\n", ""));
            configHistory.Write('\n');
            ConfigRows++;
        }

        /// <summary>セッションのフォルダに JSON を1つ保存して、その絶対パスを返す。同じ名前があれば _2 … を付ける。</summary>
        public string SaveJson(string fileName, string json)
        {
            string stem = Path.GetFileNameWithoutExtension(Sanitize(fileName));
            if (stem.Length == 0) stem = "config";
            string path = Path.Combine(Directory, stem + ".json");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(Directory, $"{stem}_{i}.json");
            File.WriteAllText(path, json ?? "", Utf8);
            return path;
        }

        /// <summary>フレームの書きかけをファイルへ書き出す。</summary>
        public void Flush()
        {
            frames?.Flush();
            events?.Flush();
            configHistory?.Flush();
        }

        /// <summary>閉じる。session.json に終了の時刻と行数を書く。</summary>
        public void Close()
        {
            if (frames == null) return;
            Flush();
            frames.Dispose();
            events.Dispose();
            configHistory.Dispose();
            frames = events = configHistory = null;
            info.endedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            info.frameRows = FrameRows;
            info.eventRows = EventRows;
            info.configRows = ConfigRows;
            WriteSessionInfo();
        }

        public void Dispose() => Close();

        void WriteSessionInfo()
        {
            File.WriteAllText(Path.Combine(Directory, SessionFileName), JsonUtility.ToJson(info, true), Utf8);
        }
    }

    /// <summary>session.json の中身。</summary>
    [Serializable]
    public class SessionInfo
    {
        public string sessionName = "";

        /// <summary>フォルダ名に付けた名前（参加者IDなど）。</summary>
        public string label = "";

        public string startedAt = "";
        public long startedUnixMs;
        public string endedAt = "";
        public string scene = "";
        public string productName = "";
        public string appVersion = "";
        public string unityVersion = "";
        public string platform = "";
        public string machine = "";

        /// <summary>何フレームに1回書いたか。</summary>
        public int frameStride = 1;

        public int frameRows;
        public int eventRows;
        public int configRows;
        public string[] frameColumns = Array.Empty<string>();
        public string[] eventColumns = Array.Empty<string>();

        /// <summary>列の説明（単位や座標）。</summary>
        public string notes = "";
    }
}
