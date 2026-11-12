using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ExtraLimb.Core;
using ExtraLimb.Tracking;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 記録の再生（Step 4-4）。ExperimentData の記録1回ぶんを読み、記録した時刻どおりに次を再現する。HMD とトラッカーなしで、同じ追加肢の動きを見られる。
    /// <list type="bullet">
    /// <item>トラッカーの生の姿勢：部位ごとに ReplayPoseSource（空間 Replay）を登録する。再生中は実機や仮想のトラッカーより優先される。
    /// 頭と両手の記録は、ほかにそのソースがなければ ovr:head などの ID で登録し、アバターを動かす（DesktopDev 用）。</item>
    /// <item>設定・2本目の表示・身長と肩幅：config_history.jsonl の時刻どおりに LimbSession.Apply で入れる（出どころ replay）。</item>
    /// <item>Re-center と前の向き：Step 4-4 からの記録は、設定の全体に入っている基準姿勢と前の向きをそのまま使う（LimbChannel.RestoreState）。
    /// それより古い記録は、Re-center の時刻にその場で Re-center する（近似）。</item>
    /// <item>確かめ：再生中、右の追加肢の u（型を通した入力）を記録の値と比べ、差の平均と最大を出す。</item>
    /// </list>
    /// 操作は実験者パネルの「ログの再生」の欄から。止めると再生のソースを外し、実機のトラッカーに戻る（設定は再生の最後のまま）。
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class LogReplayer : MonoBehaviour, ITrackerProviderStatus
    {
        [Tooltip("空なら同じ GameObject かシーンから探す")]
        [SerializeField] LimbSession session;
        [Tooltip("空ならシーンから探す")]
        [SerializeField] TrackerRegistryHost registryHost;
        [Tooltip("空ならシーンから探す（今書いている記録を一覧から外すため）")]
        [SerializeField] ExperimentRecorder recorder;
        [Tooltip("記録のフォルダの親。空なら記録の保存先（ExperimentData）")]
        [SerializeField] string rootDirectory = "";
        [Tooltip("頭・両手の記録でアバターを動かす（ovr:head などのソースがほかにないときだけ）")]
        [SerializeField] bool driveAvatar = true;
        [Tooltip("再生の速さ")]
        [SerializeField] float speed = 1f;
        [Tooltip("読み込み・移動・設定の切り替えの直後、この秒数は記録との差を数えない（フィルタが落ち着くまで）")]
        [SerializeField] float settleSeconds = 0.5f;

        ReplayLog log;
        Task<ReplayLog> loading;
        volatile float loadProgress;
        string loadingDirectory = "";
        string error = "";
        double time;
        bool playing;
        int snapshotIndex = -1;
        int eventIndex;
        double settleUntil;
        TrackerRegistry registry;
        readonly List<string> registered = new List<string>();
        readonly Dictionary<string, string> idMap = new Dictionary<string, string>();
        readonly List<string> folders = new List<string>();
        int folderIndex;
        double errorSum, errorMax;
        int errorCount;

        public string ProviderName => "ログの再生";

        /// <summary>再生の状態は実験者パネルの「ログの再生」の欄と左上の表示に出すので、機器の異常としては知らせない。</summary>
        public bool NeedsAttention => false;

        public string StatusText
        {
            get
            {
                if (IsLoading) return $"読み込み中 {LoadProgress * 100f:F0}%";
                if (error.Length > 0) return error;
                if (log == null) return "再生していません";
                return $"{(playing ? "再生中" : "一時停止")} {Position:F1} / {Duration:F1} 秒（{Path.GetFileName(log.Directory)}）";
            }
        }

        /// <summary>記録のフォルダの一覧（新しい順）。</summary>
        public IReadOnlyList<string> Folders => folders;

        /// <summary>選んでいる記録のフォルダ（なければ null）。</summary>
        public string SelectedFolder => folderIndex >= 0 && folderIndex < folders.Count ? folders[folderIndex] : null;

        public int SelectedFolderIndex => folderIndex;

        /// <summary>記録のフォルダの親。</summary>
        public string Root => !string.IsNullOrEmpty(rootDirectory) ? Path.GetFullPath(rootDirectory)
            : recorder != null ? recorder.RootDirectory : ExperimentLogger.DefaultRoot;

        public bool IsLoading => loading != null;
        public float LoadProgress => loadProgress;
        public string LoadingDirectory => loadingDirectory;

        /// <summary>読み込みに失敗したときの理由（なければ空文字）。</summary>
        public string Error => error;

        /// <summary>読み込んでいる記録（なければ null）。</summary>
        public ReplayLog Log => log;

        public bool IsLoaded => log != null;
        public bool IsPlaying => playing;

        /// <summary>再生している時刻（記録の開始からの秒。frames.csv の time と同じ）。</summary>
        public double CurrentTime => time;

        /// <summary>最初のフレームからの秒。</summary>
        public double Position => log != null ? time - log.StartTime : 0;

        public double Duration => log != null ? log.Duration : 0;

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Clamp(value, 0.1f, 8f);
        }

        /// <summary>右の追加肢の u と記録の値の差（mm）。数えたフレームがなければ NaN。</summary>
        public double MeanErrorMm => errorCount > 0 ? errorSum / errorCount : double.NaN;
        public double MaxErrorMm => errorCount > 0 ? errorMax : double.NaN;
        public int ErrorSamples => errorCount;

        void OnEnable()
        {
            if (session == null) session = GetComponent<LimbSession>();
            if (session == null) session = FindFirstObjectByType<LimbSession>();
            if (recorder == null) recorder = FindFirstObjectByType<ExperimentRecorder>();
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            registry = registryHost != null ? registryHost.Registry : null;
            if (session != null) session.LimbSolved += MeasureError;
            RefreshFolders();
        }

        void OnDisable()
        {
            if (session != null) session.LimbSolved -= MeasureError;
            Stop();
        }

        // ---- 記録の一覧 ----

        /// <summary>記録のフォルダ（frames.csv があるもの）を新しい順に並べ直す。今書いている記録は外す。</summary>
        public void RefreshFolders()
        {
            string selected = SelectedFolder;
            folders.Clear();
            string root = Root;
            if (System.IO.Directory.Exists(root))
            {
                string current = recorder != null && recorder.IsRecording ? Path.GetFullPath(recorder.CurrentDirectory) : null;
                foreach (string directory in System.IO.Directory.GetDirectories(root))
                {
                    string full = Path.GetFullPath(directory);
                    if (full == current || !File.Exists(Path.Combine(full, ExperimentLogger.FramesFileName))) continue;
                    folders.Add(full);
                }
            }
            // フォルダ名は日時で始まるので、名前の逆順が新しい順
            folders.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
            folderIndex = selected != null ? Math.Max(0, folders.IndexOf(selected)) : 0;
            if (folders.Count == 0) folderIndex = -1;
        }

        /// <summary>一覧の中で選ぶ記録を delta だけずらす（+1 で古い方）。</summary>
        public void SelectFolder(int delta)
        {
            if (folders.Count == 0) return;
            folderIndex = Mathf.Clamp(folderIndex + delta, 0, folders.Count - 1);
        }

        // ---- 読み込み ----

        /// <summary>記録を読み込む（別のスレッドで読む）。directory を省くと一覧で選んでいる記録。読み終えたら先頭で一時停止する。</summary>
        public bool BeginLoad(string directory = null)
        {
            if (IsLoading) return false;
            directory ??= SelectedFolder;
            if (string.IsNullOrEmpty(directory))
            {
                error = "記録がありません";
                return false;
            }
            Stop();
            error = "";
            loadProgress = 0f;
            loadingDirectory = directory;
            loading = Task.Run(() => ReplayLog.LoadCsv(directory, p => loadProgress = p));
            return true;
        }

        void FinishLoad()
        {
            var task = loading;
            loading = null;
            if (task.IsFaulted || task.IsCanceled)
            {
                var exception = task.Exception?.GetBaseException();
                error = $"読み込めません：{exception?.Message ?? "取り消されました"}";
                Debug.LogWarning($"[ExtraLimb] ログの再生：{error}（{loadingDirectory}）", this);
                return;
            }
            try
            {
                log = task.Result;
                log.ReadJsonFiles();
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is FormatException)
            {
                log = null;
                error = $"読み込めません：{e.Message}";
                return;
            }
            RegisterSources();
            time = log.StartTime;
            playing = false;
            Jump();
            Debug.Log($"[ExtraLimb] ログの再生：{log.Directory}（{log.FrameCount} フレーム、{log.Duration:F1} 秒、部位 {log.Tracks.Count}、設定 {log.Snapshots.Count}）");
            session?.Report(SessionEventTypes.Replay, "session", "start", "", Path.GetFileName(log.Directory), $"ログの再生を始めました：{log.Directory}");
        }

        void RegisterSources()
        {
            UnregisterSources();
            if (registry == null || log == null) return;
            var room = FixedPoseSource.Room(PoseSpace.Replay);
            registry.Register(room);
            registered.Add(room.Id);
            foreach (var pair in log.Tracks)
            {
                var site = pair.Key;
                string id = "replay:" + ExperimentRecorder.SiteColumnName(site);
                string avatarId = AvatarSourceId(site);
                if (driveAvatar && avatarId != null && !registry.TryGet(avatarId, out _)) id = avatarId;
                registry.Register(new ReplayPoseSource(id, site, pair.Value, () => time));
                registered.Add(id);
                if (log.OriginalIds.TryGetValue(site, out string original)) idMap[original] = id;
            }
        }

        void UnregisterSources()
        {
            if (registry != null)
                foreach (string id in registered)
                    registry.Unregister(id);
            registered.Clear();
            idMap.Clear();
        }

        static string AvatarSourceId(BodySite site)
        {
            switch (site)
            {
                case BodySite.Head: return "ovr:head";
                case BodySite.LeftHand: return "ovr:left_hand";
                case BodySite.RightHand: return "ovr:right_hand";
                default: return null;
            }
        }

        // ---- 再生 ----

        public void Play()
        {
            if (log == null) return;
            if (time >= log.EndTime) Seek(log.StartTime);
            playing = true;
        }

        public void Pause() => playing = false;

        public void TogglePlay()
        {
            if (playing) Pause();
            else Play();
        }

        /// <summary>記録の時刻 t（記録の開始からの秒）へ移る。その時点の設定と基準姿勢に合わせ直す。</summary>
        public void Seek(double t)
        {
            if (log == null) return;
            time = Math.Max(log.StartTime, Math.Min(log.EndTime, t));
            Jump();
        }

        /// <summary>seconds だけ進める（負なら戻す）。</summary>
        public void Skip(double seconds) => Seek(time + seconds);

        /// <summary>再生をやめ、再生のソースを外す（実機のトラッカーに戻る）。</summary>
        public void Stop()
        {
            bool wasLoaded = log != null;
            playing = false;
            UnregisterSources();
            log = null;
            snapshotIndex = -1;
            ResetError();
            if (wasLoaded) session?.Report(SessionEventTypes.Replay, "session", "stop", "", "", "ログの再生をやめました");
        }

        void Update()
        {
            if (loading != null && loading.IsCompleted) FinishLoad();
            if (log == null) return;
            if (playing)
            {
                time += Time.unscaledDeltaTime * speed;
                if (time >= log.EndTime)
                {
                    time = log.EndTime;
                    playing = false;
                }
            }
            Advance();
        }

        // 移った先の時点の状態にする
        void Jump()
        {
            snapshotIndex = -1;
            eventIndex = 0;
            while (eventIndex < log.Events.Count && log.Events[eventIndex].time <= time) eventIndex++;
            ResetError();
            Advance(true);
        }

        void Advance(bool jumped = false)
        {
            int index = log.SnapshotIndexAt(time);
            if (index >= 0 && (index != snapshotIndex || jumped))
            {
                snapshotIndex = index;
                ApplySnapshot(log.Snapshots[index], jumped);
                settleUntil = time + settleSeconds;
            }
            // 基準姿勢が入っていない古い記録は、Re-center の時刻にその場で Re-center する
            if (log.HasSavedState) return;
            while (eventIndex < log.Events.Count && log.Events[eventIndex].time <= time)
            {
                var e = log.Events[eventIndex++];
                if (e.type != SessionEventTypes.Recenter) continue;
                FindChannel(e.subject)?.RequestRecenter();
                settleUntil = time + settleSeconds;
            }
        }

        void ApplySnapshot(SessionSnapshot snapshot, bool jumped)
        {
            if (session == null || snapshot?.limbs == null) return;
            using (session.WithOrigin(ChangeOrigins.Replay))
            {
                bool leftActive = false;
                foreach (var limb in snapshot.limbs)
                    if (limb != null && limb.limb == "left" && limb.active)
                        leftActive = true;
                if (session.HasLeftLimb && session.TwoLimbs != leftActive) session.SetTwoLimbs(leftActive);

                foreach (var limb in snapshot.limbs)
                {
                    if (limb?.config == null) continue;
                    var channel = FindChannel(limb.limb);
                    if (channel == null || !channel.Active) continue;
                    var config = limb.config.Clone();
                    config.inputSourceId = MapSource(config.inputSourceId);
                    config.referenceSourceId = MapSource(config.referenceSourceId);
                    session.Apply(channel, config);
                    if (limb.stateSaved)
                        channel.RestoreState(limb.recentered, new Pose(limb.neutralPosition, SafeRotation(limb.neutralRotation)), limb.hasForward, limb.forward);
                    else if (jumped)
                        channel.RequestRecenter();
                }

                var body = session.Body;
                if (snapshot.heightCm > 0f && snapshot.shoulderWidthCm > 0f && body != null && body.IsReady
                    && (Mathf.Abs(body.Calibration.Height * 100f - snapshot.heightCm) > 0.05f || Mathf.Abs(body.Calibration.ShoulderWidth * 100f - snapshot.shoulderWidthCm) > 0.05f))
                    session.ApplyBodyDimensions(snapshot.heightCm, snapshot.shoulderWidthCm);
            }
        }

        // 記録のトラッカーの ID を、再生のソースの ID に置き換える（部屋は空にする＝再生の部屋を使う）
        string MapSource(string original)
        {
            if (string.IsNullOrEmpty(original) || original.StartsWith("room:", StringComparison.Ordinal)) return "";
            return idMap.TryGetValue(original, out string mapped) ? mapped : "";
        }

        LimbChannel FindChannel(string subject)
        {
            if (session == null) return null;
            foreach (var channel in session.Channels)
                if (channel.Subject == subject) return channel;
            return null;
        }

        static Quaternion SafeRotation(Quaternion q)
        {
            float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return norm > 1e-6f ? Quaternion.Normalize(q) : Quaternion.identity;
        }

        // ---- 記録との差 ----

        void ResetError()
        {
            errorSum = errorMax = 0;
            errorCount = 0;
        }

        // 右の追加肢を解いた直後に、型を通した入力 u を記録の値と比べる
        void MeasureError()
        {
            if (log == null || !playing || time < settleUntil || session == null) return;
            var channel = session.Primary;
            if (channel == null || !channel.Active || !channel.IsInputValid || channel.InputId == null) return;
            if (registry == null || !registry.TryGet(channel.InputId, out var input) || input.Space != PoseSpace.Replay) return;
            if (!log.TryGetU(ExperimentRecorder.LimbPrefixes[0], time, out var recorded)) return;
            double mm = (channel.Pipeline.LastDisplacement - recorded).magnitude * 1000.0;
            errorSum += mm;
            errorMax = Math.Max(errorMax, mm);
            errorCount++;
        }
    }
}
