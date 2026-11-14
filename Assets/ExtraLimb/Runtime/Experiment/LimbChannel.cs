using System;
using ExtraLimb.Body;
using ExtraLimb.Core;
using ExtraLimb.Limb;
using ExtraLimb.Tracking;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 追加肢1本ぶんの状態と計算（Step 3-5 で LimbSession から分けた）。設定（LimbRuntimeConfig）、変位→制御則の計算、入力と基準のソース、
    /// Re-center・前を記録、付け替えの追従を持つ。どの追加肢を動かすか、パネル、ホットキーは LimbSession が受け持つ。
    /// 根元の座標は左右とも体の向きにそろっている（手首の既定位置だけが左右反転）ので、左の追加肢でも入力を右へ動かせば手先は右へ動く。
    /// </summary>
    public class LimbChannel
    {
        readonly LimbSession owner;
        readonly float[] channelMin = new float[6], channelMax = new float[6];
        bool recenterRequested = true, forwardRequested, wasInputValid;
        bool mountCompletedPending, geometryChanged, loadForwardPending;
        Transform targetMarker;

        // ログの再生（Step 4-4）：次の計算で使う、記録の基準姿勢と前の向き
        bool restorePending, restoreRecentered, restoreHasForward;
        Pose restoreNeutral;
        Vector3 restoreForward;

        public LimbSide Side { get; }

        /// <summary>パネルに出す名前（「右」「左」）。</summary>
        public string Label => Side == LimbSide.Left ? "左" : "右";

        /// <summary>記録に使う名前（right / left）。</summary>
        public string Subject => Side == LimbSide.Left ? "left" : "right";

        public ExtraLimbRig Limb { get; }

        /// <summary>付け根を動かす部品（なければ Attach Point は固定）。</summary>
        public LimbMountController Mount { get; }

        public LimbRuntimeConfig Config { get; private set; }
        public LimbControlPipeline Pipeline { get; }
        public string ReferenceId { get; private set; }
        public string InputId { get; private set; }

        /// <summary>基準と入力の両方を追跡できているか。</summary>
        public bool IsInputValid { get; private set; }

        /// <summary>動かしているか。2本目を隠しているときは false。</summary>
        public bool Active { get; private set; } = true;

        public string StatusMessage { get; private set; } = "";
        public string ForwardMessage { get; private set; } = "";

        /// <summary>前の向きをどこから得たか（「左右の膝から自動」「F で記録」「保存した向き」など。未記録なら空）。</summary>
        public string ForwardSource { get; private set; } = "";

        /// <summary>入力の6つの値の、Re-center からの最小・最大があるか。</summary>
        public bool HasChannelRange { get; private set; }

        /// <summary>Re-centerして、基準位置で腕を解いた直後。</summary>
        public event Action Recentered;

        /// <summary>毎フレーム、追加肢を解いた直後。</summary>
        public event Action LimbSolved;

        /// <summary>Attach Point の付け替えが終わったか、体の寸法が変わった直後（腕を解いた後）。</summary>
        public event Action MountChanged;

        internal LimbChannel(LimbSession owner, ExtraLimbRig limb, LimbRuntimeConfig config)
        {
            this.owner = owner;
            Limb = limb;
            Side = limb.Side;
            Mount = limb.Mount;
            Config = (config ?? new LimbRuntimeConfig()).Clone();
            Config.grasp ??= new GraspTriggerConfig();
            Pipeline = new LimbControlPipeline(Config);
            if (Mount != null) Mount.MountCompleted += OnMountCompleted;
        }

        /// <summary>付け根・基準位置・届く範囲を用意する（追加肢の準備ができてから呼ぶ）。</summary>
        internal void Start(Color markerColor)
        {
            if (Mount != null)
            {
                // 設定の Attach Point に付ける（なければ今の位置を設定に書く）
                if (Mount.Registry.TryGet(Config.attachPointId, out _)) Mount.MountTo(Config.attachPointId, immediate: true);
                Config.attachPointId = Mount.CurrentId;
                Mount.UpdateMount();
            }
            Pipeline.Workspace = Limb.CreateWorkspace();
            Pipeline.SetBasePosition(Limb.RestHandPosition);
            targetMarker = owner.CreateMarker($"ExtraLimb_IKTarget_{Label}", markerColor, 0.03f);
        }

        internal void Dispose()
        {
            if (Mount != null) Mount.MountCompleted -= OnMountCompleted;
        }

        void OnMountCompleted(AttachPointDefinition definition)
        {
            mountCompletedPending = true;
            Config.attachPointId = definition.id;
            owner.Raise(SessionEventTypes.Mount, Subject, "attachPointId", "", definition.id,
                $"{Label}の追加肢：「{definition.displayName}」への付け替えが終わりました", ChangeOrigins.System);
        }

        internal void OnGeometryChanged() => geometryChanged = true;

        /// <summary>
        /// 動かすかどうか。止めると腕を隠し、再開すると Re-center する。
        /// 隠している間は骨が縮んでいて腕の長さを測れないので、出したときに届く範囲と基準位置を測り直す。
        /// </summary>
        internal void SetActive(bool active)
        {
            if (Active == active) return;
            Active = active;
            Limb.Visible = active;
            if (targetMarker != null) targetMarker.gameObject.SetActive(false);
            if (!active) return;
            recenterRequested = true;
            geometryChanged = true;
        }

        /// <summary>
        /// 設定を差し替える（複製して持つ）。制御方式が変わったらターゲットを基準位置に戻す。
        /// Attach Point が変わったら付け替えを始める。外からは LimbSession.Apply(channel, config) を使う（記録に残すため）。
        /// </summary>
        internal void Apply(LimbRuntimeConfig newConfig)
        {
            string previousAttachPoint = Config.attachPointId;
            Config = (newConfig ?? new LimbRuntimeConfig()).Clone();
            Config.grasp ??= new GraspTriggerConfig();
            Pipeline.Apply(Config);
            if (Mount != null && Config.attachPointId != previousAttachPoint && !Mount.MountTo(Config.attachPointId))
                Config.attachPointId = Mount.CurrentId;
        }

        public void RequestRecenter() => recenterRequested = true;

        /// <summary>
        /// ログの再生（Step 4-4）：次の計算で、記録した基準姿勢（基準から見た入力の姿勢）と前の向きをそのまま使う。
        /// 今の姿勢で Re-center するのではなく、記録した時点と同じ変位の 0 にする。recentered が false なら今の姿勢で Re-center する。
        /// </summary>
        public void RestoreState(bool recentered, Pose neutralLocal, bool hasForward, Vector3 forward)
        {
            restorePending = true;
            restoreRecentered = recentered;
            restoreNeutral = neutralLocal;
            restoreHasForward = hasForward;
            restoreForward = forward;
        }
        public void RequestRecordForward() => forwardRequested = true;

        /// <summary>入力の6つの値の、Re-center からの最小・最大（表示用の単位：並進 cm、回転 度）。</summary>
        public float ChannelMin(int index) => channelMin[index];
        public float ChannelMax(int index) => channelMax[index];
        public void ResetChannelRange() => HasChannelRange = false;

        /// <summary>1フレーム計算して腕を解く。</summary>
        internal void Tick(TrackerRegistry registry, bool showTargetMarker)
        {
            if (!Active || !Limb.IsReady) return;

            // 付け根を置いてから（補間中ならその途中の位置）、基準位置と届く範囲をそれに合わせる
            if (Mount != null) Mount.UpdateMount();
            if (geometryChanged) Pipeline.Workspace = Limb.CreateWorkspace();
            bool notifyMount = mountCompletedPending || geometryChanged;
            if (notifyMount || (Mount != null && Mount.IsTransitioning)) Pipeline.SetBasePosition(Limb.RestHandPosition);
            mountCompletedPending = geometryChanged = false;

            AutoSelect(registry);
            bool recentered = false;
            IsInputValid = TryGetPoses(registry, out var referencePose, out var inputPose);
            if (IsInputValid)
            {
                if (restorePending)
                {
                    ApplyRestore(referencePose, inputPose);
                    recentered = true;
                }
                else if (recenterRequested || !Pipeline.IsRecentered)
                {
                    Pipeline.Recenter(referencePose, inputPose);
                    recenterRequested = false;
                    recentered = true;
                    Debug.Log($"[ExtraLimb] {Label}の追加肢：Re-center（基準 {ReferenceId}、入力 {InputId}）");
                    owner.Raise(SessionEventTypes.Recenter, Subject, "input", "", InputId,
                        $"{Label}の追加肢：Re-center（基準 {ReferenceId}、入力 {InputId}）", ChangeOrigins.System);
                    if (loadForwardPending) LoadForward();
                    ApplyForwardFromSides(referencePose);
                }
                else if (!wasInputValid)
                {
                    Pipeline.ResetFilter();
                }
                if (forwardRequested) RecordForward(referencePose, inputPose);
                Pipeline.Step(referencePose, inputPose, Time.deltaTime);
                if (recentered) HasChannelRange = false;
                UpdateChannelRange(Pipeline.LastChannels);
            }
            if (IsInputValid != wasInputValid)
                owner.Raise(SessionEventTypes.Tracking, Subject, "valid", wasInputValid ? "1" : "0", IsInputValid ? "1" : "0",
                    $"{Label}の追加肢：{StatusMessage}", ChangeOrigins.System);
            wasInputValid = IsInputValid;

            // 追跡が切れている間は、最後のターゲットのまま保つ
            var targetWorld = Limb.FrameToWorld(Pipeline.Target);
            Limb.Solve(targetWorld);
            if (targetMarker != null)
            {
                targetMarker.gameObject.SetActive(showTargetMarker);
                targetMarker.position = targetWorld;
            }

            if (recentered) Recentered?.Invoke();
            if (notifyMount) MountChanged?.Invoke();
            LimbSolved?.Invoke();
        }

        void ApplyRestore(Pose referencePose, Pose inputPose)
        {
            restorePending = false;
            recenterRequested = false;
            loadForwardPending = false;
            if (restoreRecentered)
            {
                // 基準から見た入力の姿勢が記録のとおりになるように、仮の入力の姿勢を作って Re-center する
                var neutralInput = new Pose(referencePose.position + referencePose.rotation * restoreNeutral.position,
                    referencePose.rotation * restoreNeutral.rotation);
                Pipeline.Recenter(referencePose, neutralInput);
            }
            else
            {
                Pipeline.Recenter(referencePose, inputPose);
            }
            if (!restoreHasForward || !Pipeline.BodyFrame.TrySetForward(restoreForward, 0.01f)) Pipeline.BodyFrame.ClearForward();
            ForwardSource = Pipeline.BodyFrame.HasForward ? "記録から" : "";
            owner.Raise(SessionEventTypes.Recenter, Subject, "input", "", InputId,
                $"{Label}の追加肢：記録の基準姿勢{(restoreRecentered ? "" : "（なし。今の姿勢）")}と前の向き{(restoreHasForward ? "" : "（なし）")}に合わせました", ChangeOrigins.Replay);
        }

        void RecordForward(Pose referencePose, Pose inputPose)
        {
            forwardRequested = false;
            var horizontal = Vector3.ProjectOnPlane(Pipeline.Calibrator.Compute(referencePose, inputPose).position, Pipeline.BodyFrame.Up);
            bool hadForward = Pipeline.BodyFrame.HasForward;
            string previous = hadForward ? VectorText(Pipeline.BodyFrame.Forward) : "";
            if (Pipeline.TryRecordForward(referencePose, inputPose, owner.MinForwardGestureMeters))
            {
                ForwardMessage = $"前の向きを記録しました（前へ出した距離 {horizontal.magnitude * 100f:F1} cm。次の Re-center までこの向きを使います）";
                ForwardSource = "F で記録";
                owner.OnForwardRecorded(this);
                Debug.Log($"[ExtraLimb] {Label}の追加肢：{ForwardMessage}。体の前＝基準の座標で {Pipeline.BodyFrame.Forward.ToString("F2")}");
                owner.Raise(SessionEventTypes.Forward, Subject, ReferenceId, previous, VectorText(Pipeline.BodyFrame.Forward),
                    $"{Label}の追加肢：{ForwardMessage}", ChangeOrigins.System);
            }
            else
            {
                ForwardMessage = $"前の向きを記録できません：前へ出した距離が {horizontal.magnitude * 100f:F1} cm です（{owner.MinForwardGestureMeters * 100f:F0} cm 以上必要）";
                Debug.LogWarning($"[ExtraLimb] {Label}の追加肢：{ForwardMessage}");
                owner.Raise(SessionEventTypes.ForwardFailed, Subject, ReferenceId, "", "", $"{Label}の追加肢：{ForwardMessage}", ChangeOrigins.System);
            }
        }

        // Re-center のときに、左右の同じ部位のトラッカーを結ぶ線から前の向きを求める（2026-11-13）。組がなければ今の前の向きのまま
        void ApplyForwardFromSides(Pose referencePose)
        {
            if (!owner.AutoForwardFromSides) return;
            if (!owner.TryForwardFromSides(ReferenceId, referencePose, Pipeline.BodyFrame.Up, out var forward, out string pair, out float separation))
            {
                ForwardMessage = Pipeline.BodyFrame.HasForward
                    ? "左右の同じ部位のトラッカーがそろっていないため、前の向きはそのまま使っています"
                    : "左右の同じ部位のトラッカーがそろっていません。前の向きを F で記録してください";
                return;
            }
            bool hadForward = Pipeline.BodyFrame.HasForward;
            var before = Pipeline.BodyFrame.Forward;
            if (!Pipeline.BodyFrame.TrySetForward(forward, 0.5f)) return;
            var after = Pipeline.BodyFrame.Forward;
            float change = hadForward ? Vector3.Angle(before, after) : 0f;
            ForwardSource = $"左右の{pair}から自動";
            ForwardMessage = $"左右の{pair}のトラッカーから前の向きを求めました（左右の間隔 {separation * 100f:F0} cm" +
                             (hadForward ? $"、前回から {change:F1}°）" : "）");
            owner.SaveForward(this);
            Debug.Log($"[ExtraLimb] {Label}の追加肢：{ForwardMessage}。体の前＝基準の座標で {after.ToString("F2")}");
            owner.Raise(SessionEventTypes.Forward, Subject, ReferenceId, hadForward ? VectorText(before) : "", VectorText(after),
                $"{Label}の追加肢：{ForwardMessage}", ChangeOrigins.System);
        }

        void AutoSelect(TrackerRegistry registry)
        {
            string input = SourceSelector.SelectInput(registry, Config.inputSourceId, Config.inputSite);
            string reference = SourceSelector.SelectReference(registry, Config.referenceSourceId, input);
            if (reference == ReferenceId && input == InputId) return;
            if (reference != ReferenceId) loadForwardPending = true;
            if (input != InputId)
                owner.Raise(SessionEventTypes.Sources, Subject, "input", InputId, input,
                    $"{Label}の追加肢：入力を {input ?? "なし"} に（{BodySiteNames.Japanese(Config.inputSite)}）", ChangeOrigins.System);
            if (reference != ReferenceId)
                owner.Raise(SessionEventTypes.Sources, Subject, "reference", ReferenceId, reference,
                    $"{Label}の追加肢：基準を {reference ?? "なし"} に", ChangeOrigins.System);
            ReferenceId = reference;
            InputId = input;
            recenterRequested = true;
            Debug.Log($"[ExtraLimb] {Label}の追加肢：基準 {ReferenceId ?? "なし"}、入力 {InputId ?? "なし"}");
        }

        static string VectorText(Vector3 v) =>
            $"{CsvRow.Format(v.x, 4)} {CsvRow.Format(v.y, 4)} {CsvRow.Format(v.z, 4)}";

        /// <summary>今の基準で保存してある前の向きを使う（なければ未記録に戻す）。</summary>
        internal void LoadForward()
        {
            loadForwardPending = false;
            Pipeline.BodyFrame.ClearForward();
            var bindings = owner.Bindings;
            if (bindings != null && bindings.TryGetForward(ReferenceId, out var forward) && Pipeline.BodyFrame.TrySetForward(forward, 0.01f))
            {
                ForwardMessage = "保存した前の向きを使っています";
                ForwardSource = "保存した向き";
            }
            else
            {
                ForwardMessage = "";
                ForwardSource = "";
            }
        }

        bool TryGetPoses(TrackerRegistry registry, out Pose referencePose, out Pose inputPose)
        {
            referencePose = inputPose = default;
            if (!registry.TryGet(InputId, out var input))
            {
                StatusMessage = $"{BodySiteNames.Japanese(Config.inputSite)}のトラッカーが見つかりません";
                return false;
            }
            if (!registry.TryGet(ReferenceId, out var reference))
            {
                StatusMessage = "基準（部屋）が見つかりません";
                return false;
            }
            if (reference.Space != input.Space)
            {
                StatusMessage = "基準と入力の座標系が合っていません";
                return false;
            }
            if (!reference.TryGetPose(out referencePose))
            {
                StatusMessage = "基準の追跡が切れています（手先は止めています）";
                return false;
            }
            if (!input.TryGetPose(out inputPose))
            {
                StatusMessage = $"{BodySiteNames.Japanese(Config.inputSite)}のトラッカーの追跡が切れています（手先は止めています）";
                return false;
            }
            StatusMessage = "動作中";
            return true;
        }

        /// <summary>入力の6つの値の表示用の値（並進は cm、回転は度）。</summary>
        public static float DisplayValue(in InputChannels channels, InputChannel channel) =>
            channel.IsRotation() ? channels.Raw(channel) * Mathf.Rad2Deg : channels.Raw(channel) * 100f;

        void UpdateChannelRange(in InputChannels channels)
        {
            for (int i = 0; i < InputChannelInfo.All.Length; i++)
            {
                float value = DisplayValue(channels, InputChannelInfo.All[i]);
                channelMin[i] = HasChannelRange ? Mathf.Min(channelMin[i], value) : value;
                channelMax[i] = HasChannelRange ? Mathf.Max(channelMax[i], value) : value;
            }
            HasChannelRange = true;
        }
    }
}
