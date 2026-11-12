using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ExtraLimb.Core;
using ExtraLimb.Tracking;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 記録1回ぶん（ExperimentData/日時_名前/）を、再生できる形で読む（Step 4-4）。
    /// <list type="bullet">
    /// <item>frames.csv：部位ごとのトラッカーの生の姿勢（ReplayTrack）と、左右の追加肢の u（記録との差を見るため）。</item>
    /// <item>config_history.jsonl：設定の全体（時刻の順）。Step 4-4 からは Re-center 時の基準姿勢と前の向きも入っている。</item>
    /// <item>events.csv：部位の列のトラッカーのID（tracker_map）と、Re-center の時刻（古い記録の再生に使う）。</item>
    /// </list>
    /// MonoBehaviour から切り離してあるので、別のスレッドで読み、EditMode でテストできる。
    /// </summary>
    public class ReplayLog
    {
        /// <summary>記録の中のできごと（再生に使うものだけ）。</summary>
        public readonly struct LoggedEvent
        {
            public readonly double time;
            public readonly string type;
            public readonly string subject;
            public readonly string key;
            public readonly string newValue;

            public LoggedEvent(double time, string type, string subject, string key, string newValue)
            {
                this.time = time;
                this.type = type;
                this.subject = subject;
                this.key = key;
                this.newValue = newValue;
            }
        }

        public string Directory { get; private set; }

        /// <summary>フォルダ名に付けた名前（参加者IDなど）。</summary>
        public string Label { get; private set; } = "";

        public int FrameCount { get; private set; }

        /// <summary>最初と最後のフレームの時刻（記録の開始からの秒）。</summary>
        public double StartTime { get; private set; }
        public double EndTime { get; private set; }

        /// <summary>部位ごとの姿勢（一度も追跡できなかった部位は入れない）。</summary>
        public Dictionary<BodySite, ReplayTrack> Tracks { get; } = new Dictionary<BodySite, ReplayTrack>();

        /// <summary>部位の列に入っていたトラッカーのID（記録の最初のもの）。</summary>
        public Dictionary<BodySite, string> OriginalIds { get; } = new Dictionary<BodySite, string>();

        /// <summary>設定の全体（時刻の順）。</summary>
        public List<SessionSnapshot> Snapshots { get; } = new List<SessionSnapshot>();

        /// <summary>Re-center と前の記録（時刻の順）。</summary>
        public List<LoggedEvent> Events { get; } = new List<LoggedEvent>();

        /// <summary>設定の全体に基準姿勢と前の向きが入っているか（Step 4-4 以降の記録か）。</summary>
        public bool HasSavedState { get; private set; }

        double[] times = Array.Empty<double>();
        readonly Dictionary<string, (bool[] valid, Vector3[] u)> limbU = new Dictionary<string, (bool[], Vector3[])>();

        /// <summary>記録の長さ（秒）。</summary>
        public double Duration => Math.Max(0, EndTime - StartTime);

        /// <summary>
        /// フォルダを読む。progress には読んだ割合（0〜1）を入れる（別のスレッドから見てよい）。
        /// frames.csv がないときは FileNotFoundException、列がそろっていないときは FormatException。
        /// </summary>
        public static ReplayLog Load(string directory, Action<float> progress = null)
        {
            var log = LoadCsv(directory, progress);
            log.ReadJsonFiles();
            return log;
        }

        /// <summary>CSV（frames.csv と events.csv）だけを読む。別のスレッドで呼んでよい。続けてメインのスレッドで ReadJsonFiles を呼ぶ。</summary>
        public static ReplayLog LoadCsv(string directory, Action<float> progress = null)
        {
            var log = new ReplayLog { Directory = Path.GetFullPath(directory) };
            string framesPath = Path.Combine(log.Directory, ExperimentLogger.FramesFileName);
            if (!File.Exists(framesPath)) throw new FileNotFoundException($"{ExperimentLogger.FramesFileName} がありません", framesPath);
            log.ReadFrames(framesPath, progress);
            log.ReadEvents();
            return log;
        }

        /// <summary>JSON（config_history.jsonl と session.json）を読む（JsonUtility を使うので、メインのスレッドで呼ぶ）。</summary>
        public void ReadJsonFiles()
        {
            Snapshots.Clear();
            ReadSessionInfo();
            ReadSnapshots();
        }

        void ReadSessionInfo()
        {
            string path = Path.Combine(Directory, ExperimentLogger.SessionFileName);
            if (!File.Exists(path)) return;
            try
            {
                var info = JsonUtility.FromJson<SessionInfo>(File.ReadAllText(path, Encoding.UTF8));
                Label = info?.label ?? "";
            }
            catch (ArgumentException)
            {
                Label = "";
            }
        }

        void ReadFrames(string path, Action<float> progress)
        {
            var fields = new List<string>();
            var buffer = new StringBuilder(64);
            long length = new FileInfo(path).Length;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16))
            {
                if (!CsvReader.ReadRecord(reader, fields, buffer)) throw new FormatException("frames.csv が空です");
                var header = new List<string>(fields);
                int timeColumn = header.IndexOf("time");
                if (timeColumn < 0) throw new FormatException("frames.csv に time の列がありません");

                // 部位の列（right_ankle_ok, _px … _qw）
                var sites = new List<(BodySite site, int column)>();
                foreach (BodySite site in Enum.GetValues(typeof(BodySite)))
                {
                    if (site == BodySite.Unknown) continue;
                    int column = header.IndexOf(ExperimentRecorder.SiteColumnName(site) + "_ok");
                    if (column >= 0 && column + 7 < header.Count) sites.Add((site, column));
                }
                // 追加肢の u（r_u_r …）と、記録との差を見てよいか（active と valid）
                var limbs = new List<(string prefix, int active, int valid, int u)>();
                foreach (string prefix in ExperimentRecorder.LimbPrefixes)
                {
                    int u = header.IndexOf(prefix + "u_r");
                    if (u >= 0) limbs.Add((prefix, header.IndexOf(prefix + "active"), header.IndexOf(prefix + "valid"), u));
                }

                var timeList = new List<double>(4096);
                var okLists = new List<bool>[sites.Count];
                var positionLists = new List<Vector3>[sites.Count];
                var rotationLists = new List<Quaternion>[sites.Count];
                var anyTracked = new bool[sites.Count];
                for (int s = 0; s < sites.Count; s++)
                {
                    okLists[s] = new List<bool>(4096);
                    positionLists[s] = new List<Vector3>(4096);
                    rotationLists[s] = new List<Quaternion>(4096);
                }
                var uValid = new List<bool>[limbs.Count];
                var uValues = new List<Vector3>[limbs.Count];
                for (int l = 0; l < limbs.Count; l++)
                {
                    uValid[l] = new List<bool>(4096);
                    uValues[l] = new List<Vector3>(4096);
                }

                int row = 0;
                while (CsvReader.ReadRecord(reader, fields, buffer))
                {
                    if (fields.Count <= timeColumn) continue;
                    double time = CsvReader.ParseDouble(fields[timeColumn]);
                    if (double.IsNaN(time)) continue;
                    timeList.Add(time);
                    for (int s = 0; s < sites.Count; s++)
                    {
                        int c = sites[s].column;
                        bool ok = c + 7 < fields.Count && CsvReader.ParseBool(fields[c]);
                        var position = Vector3.zero;
                        var rotation = Quaternion.identity;
                        if (ok)
                        {
                            position = new Vector3(F(fields[c + 1]), F(fields[c + 2]), F(fields[c + 3]));
                            var q = new Quaternion(F(fields[c + 4]), F(fields[c + 5]), F(fields[c + 6]), F(fields[c + 7]));
                            ok = IsFinite(position) && IsFinite(q) && (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) > 1e-6f;
                            if (ok) rotation = Quaternion.Normalize(q);
                        }
                        okLists[s].Add(ok);
                        positionLists[s].Add(position);
                        rotationLists[s].Add(rotation);
                        anyTracked[s] |= ok;
                    }
                    for (int l = 0; l < limbs.Count; l++)
                    {
                        var (_, active, valid, u) = limbs[l];
                        bool usable = active >= 0 && valid >= 0 && u + 2 < fields.Count
                                      && CsvReader.ParseBool(fields[active]) && CsvReader.ParseBool(fields[valid]);
                        var value = usable ? new Vector3(F(fields[u]), F(fields[u + 1]), F(fields[u + 2])) : Vector3.zero;
                        usable &= IsFinite(value);
                        uValid[l].Add(usable);
                        uValues[l].Add(value);
                    }
                    if (progress != null && (++row & 1023) == 0 && length > 0) progress(Mathf.Clamp01((float)stream.Position / length));
                }

                times = timeList.ToArray();
                FrameCount = times.Length;
                if (FrameCount == 0) throw new FormatException("frames.csv にフレームがありません");
                StartTime = times[0];
                EndTime = times[times.Length - 1];
                for (int s = 0; s < sites.Count; s++)
                    if (anyTracked[s]) Tracks[sites[s].site] = new ReplayTrack(timeList, okLists[s], positionLists[s], rotationLists[s]);
                for (int l = 0; l < limbs.Count; l++)
                    limbU[limbs[l].prefix] = (uValid[l].ToArray(), uValues[l].ToArray());
            }
            progress?.Invoke(1f);
        }

        void ReadSnapshots()
        {
            string path = Path.Combine(Directory, ExperimentLogger.ConfigHistoryFileName);
            if (!File.Exists(path)) return;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var snapshot = JsonUtility.FromJson<SessionSnapshot>(line);
                        if (snapshot != null) Snapshots.Add(snapshot);
                    }
                    catch (ArgumentException)
                    {
                        // 書きかけの行などは飛ばす
                    }
                }
            }
            // 時刻の順（同じ時刻なら書いた順）
            var ordered = new List<(SessionSnapshot snapshot, int index)>();
            for (int i = 0; i < Snapshots.Count; i++) ordered.Add((Snapshots[i], i));
            ordered.Sort((a, b) => a.snapshot.time != b.snapshot.time ? a.snapshot.time.CompareTo(b.snapshot.time) : a.index.CompareTo(b.index));
            Snapshots.Clear();
            foreach (var item in ordered) Snapshots.Add(item.snapshot);
            foreach (var snapshot in Snapshots)
                if (snapshot.limbs != null)
                    foreach (var limb in snapshot.limbs)
                        if (limb != null && limb.stateSaved)
                            HasSavedState = true;
        }

        void ReadEvents()
        {
            string path = Path.Combine(Directory, ExperimentLogger.EventsFileName);
            if (!File.Exists(path)) return;
            var fields = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                if (!CsvReader.ReadRecord(reader, fields)) return;
                var header = new List<string>(fields);
                int time = header.IndexOf("time"), type = header.IndexOf("type"), subject = header.IndexOf("subject");
                int key = header.IndexOf("key"), newValue = header.IndexOf("new");
                if (time < 0 || type < 0 || subject < 0 || key < 0 || newValue < 0) return;
                int last = Math.Max(Math.Max(time, type), Math.Max(Math.Max(subject, key), newValue));
                while (CsvReader.ReadRecord(reader, fields))
                {
                    if (fields.Count <= last) continue;
                    string eventType = fields[type];
                    if (eventType == SessionEventTypes.TrackerMap)
                    {
                        if (Enum.TryParse(fields[key], out BodySite site) && !OriginalIds.ContainsKey(site) && fields[newValue].Length > 0)
                            OriginalIds[site] = fields[newValue];
                    }
                    else if (eventType == SessionEventTypes.Recenter || eventType == SessionEventTypes.Forward)
                    {
                        Events.Add(new LoggedEvent(CsvReader.ParseDouble(fields[time]), eventType, fields[subject], fields[key], fields[newValue]));
                    }
                }
            }
            Events.Sort((a, b) => a.time.CompareTo(b.time));
        }

        /// <summary>time の時点の設定の全体（time 以前でいちばん後のもの。なければ最初のもの、それもなければ null）。</summary>
        public int SnapshotIndexAt(double time)
        {
            if (Snapshots.Count == 0) return -1;
            int found = 0;
            for (int i = 0; i < Snapshots.Count; i++)
            {
                if (Snapshots[i].time <= time) found = i;
                else break;
            }
            return found;
        }

        /// <summary>
        /// 記録した追加肢の u（m、体の軸）。time 以前でいちばん後のフレームの値。prefix は r_ / l_。
        /// その時刻に追加肢を動かしていて追跡できていなければ false。次のフレームまでが maxGapSeconds より空いているところ
        /// （記録の途中でエディタが止まったなど。その間の動きは記録にない）も false。
        /// </summary>
        public bool TryGetU(string prefix, double time, out Vector3 u, double maxGapSeconds = 0.05)
        {
            u = Vector3.zero;
            if (!limbU.TryGetValue(prefix, out var data) || times.Length == 0) return false;
            int low = 0, high = times.Length - 1, i = -1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (times[mid] <= time)
                {
                    i = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            // 補間しない（Re-center の前後で u が飛ぶので、間の値は記録のどのフレームとも合わない）
            if (i < 0 || !data.valid[i] || i + 1 >= times.Length || times[i + 1] - times[i] > maxGapSeconds) return false;
            u = data.u[i];
            return true;
        }

        static float F(string text) => (float)CsvReader.ParseDouble(text);

        static bool IsFinite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        static bool IsFinite(Quaternion q) => !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w));
    }
}
