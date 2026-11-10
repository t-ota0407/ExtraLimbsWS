using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// 入力の姿勢から、追加肢の手先のターゲット（根元の座標）までの計算をまとめたもの。
    /// <code>
    /// Δp, Δq = Re-center時からの変位（基準の座標）
    ///   → One Euro Filter → 体の軸に直す（並進3つ・回転3つ＝入力の6つの値）
    ///   → マッピングの型（AxisMapping。回転はてこの長さで長さに直す）→ u
    ///   → 位置制御と届く範囲の制限 → p_t
    /// </code>
    /// 座標の扱い：u は体の軸（x=右、y=上、z=前）。p_t と基準位置 p_b は追加肢の根元の座標で、軸の向きは体と同じ。
    /// MonoBehaviour に依存しないので、EditModeテストで確かめられる。
    /// </summary>
    public class LimbControlPipeline
    {
        WorkspaceLimiter workspace;

        public LimbRuntimeConfig Config { get; private set; }
        public NeutralPoseCalibrator Calibrator { get; } = new NeutralPoseCalibrator();
        public BodyFrameCalibrator BodyFrame { get; } = new BodyFrameCalibrator();
        public PoseFilter Filter { get; } = new PoseFilter();
        public IControlLaw Law { get; private set; }

        /// <summary>重力の上向き（入力の空間の座標）。SteamVR（Raw）・Quest・仮想トラッカーはどれも +Y。</summary>
        public Vector3 WorldUp { get; set; } = Vector3.up;

        /// <summary>届く範囲。null なら制限しない。</summary>
        public WorkspaceLimiter Workspace
        {
            get => workspace;
            set
            {
                workspace = value;
                Law.Workspace = value;
            }
        }

        /// <summary>基準位置 p_b（Attach Pointの既定の手先位置）。ハイブリッド制御では Law.BasePosition が動く。</summary>
        public Vector3 BasePosition { get; private set; }

        /// <summary>Re-centerが済んでいるか。</summary>
        public bool IsRecentered => Calibrator.IsCalibrated;

        /// <summary>直近の変位（フィルタ前、腰トラッカーの座標）。</summary>
        public PoseDelta LastDelta { get; private set; } = PoseDelta.Zero;

        /// <summary>直近の u（フィルタ後、体の軸に直してマッピングの型を通した後、m）。</summary>
        public Vector3 LastDisplacement { get; private set; }

        /// <summary>直近の入力の6つの値（フィルタ後、体の軸）。</summary>
        public InputChannels LastChannels { get; private set; } = InputChannels.Zero;

        /// <summary>直近のターゲット p_t（根元の座標）。</summary>
        public Vector3 Target => Law.Target;

        /// <summary>ターゲットが届く範囲の端にあるか。</summary>
        public bool AtWorkspaceBoundary
        {
            get
            {
                if (workspace == null) return false;
                float distance = Vector3.Distance(Law.Target, workspace.Center);
                return distance >= workspace.MaxRadius - 1e-3f || distance <= workspace.MinRadius + 1e-3f;
            }
        }

        public LimbControlPipeline(LimbRuntimeConfig config)
        {
            Config = config ?? new LimbRuntimeConfig();
            Law = ControlLaws.Create(Config.mode);
            Filter.Configure(Config.filter);
        }

        /// <summary>
        /// 設定を反映する。制御方式が変わったら制御則を作り直してターゲットを基準位置に戻す。
        /// ゲインや不感帯は次の Step から効く。
        /// </summary>
        public void Apply(LimbRuntimeConfig config)
        {
            Config = config ?? new LimbRuntimeConfig();
            Filter.Configure(Config.filter);
            if (Law.Mode == Config.mode) return;
            Law = ControlLaws.Create(Config.mode);
            Law.Workspace = workspace;
            Law.Reset(BasePosition);
        }

        /// <summary>基準位置を設定し、ターゲットをそこに戻す。</summary>
        public void SetBasePosition(Vector3 basePosition)
        {
            BasePosition = basePosition;
            Law.Reset(basePosition);
        }

        /// <summary>今の姿勢を変位0にし、上の向きを測り直し、ターゲットを基準位置に戻す。</summary>
        public void Recenter(Pose reference, Pose input)
        {
            Calibrator.Recenter(reference, input);
            BodyFrame.SetUp(reference.rotation, WorldUp);
            Filter.Reset();
            Law.Reset(BasePosition);
            LastDelta = PoseDelta.Zero;
            LastDisplacement = Vector3.zero;
            LastChannels = InputChannels.Zero;
        }

        /// <summary>
        /// 前の向きを記録する。入力の部位を前へ出した姿勢で呼ぶ（Re-center済みであること）。
        /// 水平の変位が minHorizontal（m）より短いときは記録せずに false を返す。
        /// </summary>
        public bool TryRecordForward(Pose reference, Pose input, float minHorizontal)
        {
            if (!Calibrator.IsCalibrated) return false;
            return BodyFrame.TrySetForward(Calibrator.Compute(reference, input).position, minHorizontal);
        }

        /// <summary>トラッキングが切れて戻ったときなど、フィルタの内部状態だけを消す。</summary>
        public void ResetFilter() => Filter.Reset();

        /// <summary>
        /// 変位を体の軸の6つの値にする。回転は、Re-center時の入力の向きを基準にした Δq を基準の座標の回転ベクトルに直してから、体の軸で表す。
        /// そのため、トラッカーの付け方（向き）によらず「前へ傾ける」「右へひねる」の向きがそろう。
        /// </summary>
        public InputChannels ToChannels(PoseDelta delta)
        {
            var translation = BodyFrame.ToBody(delta.position);
            var rotationInReference = Calibrator.NeutralLocalPose.rotation * InputChannels.RotationVector(delta.rotation);
            return new InputChannels(translation, BodyFrame.ToBody(rotationInReference));
        }

        /// <summary>1フレーム進めて、ターゲット p_t（根元の座標）を返す。Re-center前なら、今の姿勢でRe-centerする。</summary>
        public Vector3 Step(Pose reference, Pose input, float dt)
        {
            if (!Calibrator.IsCalibrated) Recenter(reference, input);

            LastDelta = Calibrator.Compute(reference, input);
            var filtered = dt > 0f ? Filter.Filter(LastDelta, dt) : LastDelta;
            LastChannels = ToChannels(filtered);
            LastDisplacement = Config.axisMapping.Apply(LastChannels);
            return Law.Step(new ControlInput(LastDisplacement), ControlParams.From(Config), dt);
        }
    }
}
