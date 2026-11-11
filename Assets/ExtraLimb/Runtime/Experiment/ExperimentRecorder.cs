using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ExtraLimb.Core;
using ExtraLimb.Limb;
using ExtraLimb.Tracking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 実験の記録（Step 4-1）。LimbSession のイベント（設定の変更、Re-center、把持など）を events.csv に、
    /// 毎フレームの状態を frames.csv に、設定の全体を config_history.jsonl に書く（ファイルは ExperimentLogger）。
    /// <list type="bullet">
    /// <item>再生を始めると記録を始める（autoStart）。止める・新しく始める、マーカー、今の設定を JSON に保存は、
    /// 実験者パネル（ExperimenterPanel）とホットキー（HotkeyMap。既定は L、K、Ctrl+S）から呼ぶ。</item>
    /// <item>保存先は既定でプロジェクト（ビルドでは exe）と同じ階層の ExperimentData/日時/（リポジトリでは gitignore 対象）。</item>
    /// <item>frames.csv の列は FrameColumns：追加肢（右 r_、左 l_）の入力の6つの値・u・ターゲット・手先・把持、本来の手、
    /// トラッカーの生の姿勢（部位ごと。後のログ再生に使う）。追加肢を解いて掴んだ後（実行順 10050）に書く。</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(10050)]
    public class ExperimentRecorder : MonoBehaviour
    {
        [Tooltip("空なら同じ GameObject かシーンから探す")]
        [SerializeField] LimbSession session;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] TrackerRegistryHost registryHost;

        [Header("記録")]
        [Tooltip("再生を始めたら記録を始める")]
        [SerializeField] bool autoStart = true;
        [Tooltip("保存先のフォルダ。空ならプロジェクト（ビルドでは exe）と同じ階層の ExperimentData")]
        [SerializeField] string rootDirectory = "";
        [Tooltip("フォルダ名に付ける名前（参加者IDなど）")]
        [SerializeField] string sessionLabel = "";
        [Tooltip("何フレームに1回 frames.csv に書くか（1 で毎フレーム）")]
        [Min(1)]
        [SerializeField] int frameStride = 1;
        [Tooltip("frames.csv をファイルへ書き出す間隔（秒）。落ちたときに失うのはこの間の分")]
        [SerializeField] float flushIntervalSeconds = 1f;
        [Tooltip("frames.csv に生の姿勢を書く部位（同じ部位の入力の選び方：SteamVR → 仮想 → Quest）")]
        [SerializeField] BodySite[] trackerSites =
        {
            BodySite.RightAnkle, BodySite.RightKnee, BodySite.RightShoulder, BodySite.RightElbow,
            BodySite.Waist, BodySite.Head, BodySite.LeftHand, BodySite.RightHand,
        };

        /// <summary>追加肢の列の頭（右、左）。</summary>
        public static readonly string[] LimbPrefixes = { "r_", "l_" };

        static readonly string[] LimbColumns =
        {
            "active", "valid",
            "move_r", "move_u", "move_f", "tilt_f", "tilt_r", "twist_r",
            "u_r", "u_u", "u_f",
            "target_x", "target_y", "target_z",
            "hand_x", "hand_y", "hand_z",
            "edge", "trigger", "grasp", "held",
        };

        static readonly string[] HandColumns = { "hand_l_grip", "hand_l_grasp", "hand_l_held", "hand_r_grip", "hand_r_grasp", "hand_r_held" };
        static readonly string[] TrackerColumns = { "ok", "px", "py", "pz", "qx", "qy", "qz", "qw" };

        const string Notes =
            "time＝記録の開始からの秒、unix_ms＝UNIX時刻（ミリ秒）、dt＝前のフレームからの秒、rev＝設定の版（config_history.jsonl の revision）。" +
            "r_＝右の追加肢、l_＝左の追加肢。active＝動かしているか、valid＝入力と基準を追跡できているか。" +
            "move_r/u/f＝入力の部位の移動（m、体の右・上・前）、tilt_f/tilt_r/twist_r＝前へ傾ける・右へ傾ける・右へひねる（rad）。いずれも Re-center 時を0、フィルタ後。" +
            "u_r/u/f＝マッピングの型を通した入力（m、体の軸）。target＝IKターゲット、hand＝手先（指先）の位置（ワールド＝Questの空間、m）。" +
            "edge＝手先が届く範囲の端、trigger＝把持トリガーの押し込み量、grasp＝掴む操作中か、held＝掴んでいる物。" +
            "hand_l/hand_r＝本来の左手・右手（grip＝グリップの押し込み量）。" +
            "部位の列（right_ankle など）＝トラッカーの生の姿勢（そのトラッカーの空間、m と四元数）。どのトラッカーかは events.csv の tracker_map。";

        ExperimentLogger logger;
        TrackerRegistry registry;
        readonly CsvRow row = new CsvRow();
        string[] trackerIds = Array.Empty<string>();
        double startRealtime;
        float lastFlush;
        int markerCount;
        string message = "", barMessage = "";
        float messageTime = float.NegativeInfinity;

        /// <summary>記録中か。</summary>
        public bool IsRecording => logger != null && logger.IsOpen;

        /// <summary>今のセッションのフォルダ（記録していなければ null）。</summary>
        public string CurrentDirectory => logger?.Directory;

        /// <summary>今のセッションのファイルの書き手（記録していなければ null）。</summary>
        public ExperimentLogger Logger => logger;

        /// <summary>記録の開始からの秒。</summary>
        public double ElapsedSeconds => IsRecording ? Time.realtimeSinceStartupAsDouble - startRealtime : 0.0;

        public int MarkerCount => markerCount;

        /// <summary>最後のメッセージ（記録の開始・停止、マーカー、保存の結果）。</summary>
        public string Message => message;

        /// <summary>最後のメッセージの短い形（パネルの1行用）。</summary>
        public string ShortMessage => barMessage;

        /// <summary>最後のメッセージを出した時刻（Time.unscaledTime）。</summary>
        public float MessageTime => messageTime;

        /// <summary>保存先のフォルダ（絶対パス）。</summary>
        public string RootDirectory => string.IsNullOrEmpty(rootDirectory) ? ExperimentLogger.DefaultRoot : Path.GetFullPath(rootDirectory);

        /// <summary>frames.csv の列。</summary>
        public static List<string> FrameColumns(IReadOnlyList<BodySite> sites)
        {
            var columns = new List<string> { "frame", "time", "unix_ms", "dt", "rev" };
            foreach (var prefix in LimbPrefixes)
                foreach (var column in LimbColumns)
                    columns.Add(prefix + column);
            columns.AddRange(HandColumns);
            if (sites != null)
                foreach (var site in sites)
                {
                    string name = SiteColumnName(site);
                    foreach (var column in TrackerColumns) columns.Add($"{name}_{column}");
                }
            return columns;
        }

        /// <summary>部位の列の名前（例：RightAnkle → right_ankle）。</summary>
        public static string SiteColumnName(BodySite site)
        {
            string name = site.ToString();
            var text = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c) && i > 0) text.Append('_');
                text.Append(char.ToLowerInvariant(c));
            }
            return text.ToString();
        }

        void OnEnable()
        {
            if (session == null) session = GetComponent<LimbSession>();
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            registry = registryHost != null ? registryHost.Registry : null;
            if (session != null)
            {
                session.EventRaised += OnSessionEvent;
                session.Revised += OnRevised;
            }
            else
            {
                Debug.LogWarning("[ExtraLimb] 記録：LimbSession がシーンにないため、追加肢の状態とイベントは記録しません", this);
            }
        }

        void OnDisable()
        {
            StopRecording(ChangeOrigins.System);
            if (session != null)
            {
                session.EventRaised -= OnSessionEvent;
                session.Revised -= OnRevised;
            }
        }

        void Start()
        {
            if (autoStart) StartRecording(ChangeOrigins.System);
        }

        void OnApplicationQuit() => StopRecording(ChangeOrigins.System);

        // ---- 記録の開始・停止・マーカー ----

        /// <summary>新しいセッションのフォルダを作って記録を始める。すでに記録中なら何もしない。</summary>
        public bool StartRecording(string origin = ChangeOrigins.Api, string label = null)
        {
            if (IsRecording) return true;
            string name = label ?? sessionLabel;
            var info = new SessionInfo
            {
                label = ExperimentLogger.Sanitize(name),
                scene = SceneManager.GetActiveScene().name,
                productName = Application.productName,
                appVersion = Application.version,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                machine = Environment.MachineName,
                frameStride = Mathf.Max(1, frameStride),
                notes = Notes,
            };
            try
            {
                logger = new ExperimentLogger(RootDirectory, ExperimentLogger.SessionName(DateTime.Now, name), FrameColumns(trackerSites), info);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                logger = null;
                Notify($"記録を始められません：{e.Message}", "記録を始められません（コンソールを見る）");
                Debug.LogError($"[ExtraLimb] {message}", this);
                return false;
            }
            startRealtime = Time.realtimeSinceStartupAsDouble;
            lastFlush = Time.unscaledTime;
            markerCount = 0;
            trackerIds = new string[trackerSites?.Length ?? 0];
            string folder = Path.GetFileName(logger.Directory);
            Write(new SessionEvent(SessionEventTypes.LogStart, "session", origin, "folder", "", folder, $"記録を始めました：{logger.Directory}"));
            WriteSnapshot(SessionEventTypes.LogStart);
            UpdateTrackerMap();
            Notify($"記録を始めました：{folder}", $"記録開始：{folder}");
            Debug.Log($"[ExtraLimb] 記録を始めました：{logger.Directory}");
            return true;
        }

        /// <summary>記録を止めてファイルを閉じる。</summary>
        public void StopRecording(string origin = ChangeOrigins.Api)
        {
            if (!IsRecording) return;
            Write(new SessionEvent(SessionEventTypes.LogStop, "session", origin, "frames", "", logger.FrameRows.ToString(CultureInfo.InvariantCulture),
                $"記録を止めました（{ElapsedSeconds:F1} 秒、フレーム {logger.FrameRows} 行）"));
            string directory = logger.Directory;
            int frames = logger.FrameRows;
            logger.Close();
            logger = null;
            Notify($"記録を止めました（フレーム {frames} 行）：{Path.GetFileName(directory)}", $"記録停止（フレーム {frames} 行）");
            Debug.Log($"[ExtraLimb] 記録を止めました（フレーム {frames} 行）：{directory}");
        }

        /// <summary>マーカーを入れる（通し番号を返す。記録していなければ 0）。</summary>
        public int InsertMarker(string origin = ChangeOrigins.Api, string note = "")
        {
            if (!IsRecording)
            {
                Notify("記録していないので、マーカーは入りません", "記録していません");
                return 0;
            }
            markerCount++;
            string number = markerCount.ToString(CultureInfo.InvariantCulture);
            Write(new SessionEvent(SessionEventTypes.Marker, "session", origin, number, "", note ?? "", $"マーカー {number}" + (string.IsNullOrEmpty(note) ? "" : $"：{note}")));
            Notify($"マーカー {number} を入れました（{ElapsedSeconds:F1} 秒）", $"マーカー{number} を入れました");
            return markerCount;
        }

        /// <summary>
        /// 今の設定の全体を JSON に保存して、そのパスを返す。記録中はセッションのフォルダに、記録していなければ保存先の configs フォルダに置く。
        /// </summary>
        public string SaveCurrentConfig(string origin = ChangeOrigins.Api)
        {
            if (session == null)
            {
                Notify("LimbSession がないため、設定を保存できません", "設定を保存できません");
                return null;
            }
            var snapshot = session.CreateSnapshot(SessionEventTypes.ConfigSaved);
            snapshot.time = ElapsedSeconds;
            string json = JsonUtility.ToJson(snapshot, true);
            string fileName = "config_" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
            string path;
            try
            {
                if (IsRecording)
                {
                    path = logger.SaveJson(fileName, json);
                }
                else
                {
                    string directory = Path.Combine(RootDirectory, "configs");
                    Directory.CreateDirectory(directory);
                    path = Path.Combine(directory, $"config_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.json");
                    File.WriteAllText(path, json, new UTF8Encoding(false));
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Notify($"設定を保存できません：{e.Message}", "設定を保存できません（コンソールを見る）");
                Debug.LogError($"[ExtraLimb] {message}", this);
                return null;
            }
            Write(new SessionEvent(SessionEventTypes.ConfigSaved, "session", origin, "file", "", Path.GetFileName(path), $"設定を保存しました：{path}"));
            Notify($"設定を保存しました：{Path.GetFileName(path)}", $"保存：{Path.GetFileName(path)}");
            Debug.Log($"[ExtraLimb] 設定を保存しました：{path}");
            return path;
        }

        // パネルのメッセージ（message は全文、行に出すのは短い文）
        void Notify(string full, string brief)
        {
            message = full;
            barMessage = brief;
            messageTime = Time.unscaledTime;
        }

        // ---- イベント ----

        void OnSessionEvent(SessionEvent e) => Write(e);

        void OnRevised(string reason) => WriteSnapshot(reason);

        void Write(in SessionEvent e)
        {
            if (!IsRecording) return;
            logger.WriteEvent(ElapsedSeconds, Time.frameCount, e);
        }

        void WriteSnapshot(string reason)
        {
            if (!IsRecording || session == null) return;
            var snapshot = session.CreateSnapshot(reason);
            snapshot.time = Math.Round(ElapsedSeconds, 4);
            logger.WriteConfig(JsonUtility.ToJson(snapshot));
        }

        // frames.csv の部位の列に入れるトラッカーが変わったら、イベントに残す（ログ再生でどのトラッカーかを知るため）
        void UpdateTrackerMap()
        {
            if (registry == null || trackerSites == null) return;
            for (int i = 0; i < trackerSites.Length && i < trackerIds.Length; i++)
            {
                string id = SourceSelector.SelectInput(registry, "", trackerSites[i]);
                if (id == trackerIds[i]) continue;
                string space = id != null && registry.TryGet(id, out var source) ? source.Space.ToString() : "";
                Write(new SessionEvent(SessionEventTypes.TrackerMap, SiteColumnName(trackerSites[i]), ChangeOrigins.System, trackerSites[i].ToString(),
                    trackerIds[i], id, id != null ? $"{BodySiteNames.Japanese(trackerSites[i])}の列：{id}（{space}）" : $"{BodySiteNames.Japanese(trackerSites[i])}の列：なし"));
                trackerIds[i] = id;
            }
        }

        // ---- 毎フレーム ----

        void LateUpdate()
        {
            if (!IsRecording) return;
            UpdateTrackerMap();
            if (Time.frameCount % Mathf.Max(1, frameStride) == 0) WriteFrame();
            if (Time.unscaledTime - lastFlush >= flushIntervalSeconds)
            {
                logger.Flush();
                lastFlush = Time.unscaledTime;
            }
        }

        void WriteFrame()
        {
            row.Clear()
                .Add(Time.frameCount)
                .Add(ElapsedSeconds, 4)
                .Add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                .Add(Time.unscaledDeltaTime, 4)
                .Add(session != null ? session.Revision : 0);

            var grasp = session != null ? session.Grasp : null;
            AddLimb(FindChannel(LimbSide.Right), grasp);
            AddLimb(FindChannel(LimbSide.Left), grasp);
            AddHand(grasp, grasp != null ? grasp.LeftHand : null, ControllerHand.Left);
            AddHand(grasp, grasp != null ? grasp.RightHand : null, ControllerHand.Right);
            for (int i = 0; i < trackerIds.Length; i++) AddTracker(trackerIds[i]);
            logger.WriteFrame(row);
        }

        LimbChannel FindChannel(LimbSide side)
        {
            if (session == null) return null;
            foreach (var channel in session.Channels)
                if (channel.Side == side) return channel;
            return null;
        }

        void AddLimb(LimbChannel channel, GraspRig grasp)
        {
            if (channel == null || !channel.Active || !channel.Limb.IsReady)
            {
                row.Add(false).AddEmpty(LimbColumns.Length - 1);
                return;
            }
            var pipeline = channel.Pipeline;
            row.Add(true).Add(channel.IsInputValid);
            var values = pipeline.LastChannels;
            foreach (var inputChannel in InputChannelInfo.All) row.Add(values.Raw(inputChannel), 5);
            row.Add(pipeline.LastDisplacement, 5);
            row.Add(channel.Limb.FrameToWorld(pipeline.Target), 4);
            row.Add(channel.Limb.ContactPoint, 4);
            row.Add(pipeline.AtWorkspaceBoundary);
            var grasper = grasp != null ? grasp.GrasperFor(channel) : null;
            if (grasper == null)
            {
                row.AddEmpty(3);
                return;
            }
            row.Add(grasp.TriggerValue(channel), 3).Add(grasper.Pressed).Add(grasper.Held != null ? grasper.Held.name : "");
        }

        void AddHand(GraspRig grasp, EndEffectorGrasper hand, ControllerHand side)
        {
            if (grasp == null || hand == null)
            {
                row.AddEmpty(3);
                return;
            }
            row.Add(grasp.GripValue(side), 3).Add(hand.Pressed).Add(hand.Held != null ? hand.Held.name : "");
        }

        void AddTracker(string id)
        {
            if (registry != null && id != null && registry.TryGet(id, out var source) && source.TryGetPose(out var pose))
            {
                row.Add(true).Add(pose.position, 4).Add(pose.rotation, 5);
                return;
            }
            row.Add(false).AddEmpty(TrackerColumns.Length - 1);
        }
    }
}
