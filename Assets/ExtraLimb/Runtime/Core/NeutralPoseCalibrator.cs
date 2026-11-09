using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>Re-center時の姿勢からの変化。</summary>
    public readonly struct PoseDelta
    {
        /// <summary>位置の変化 Δp（m）。腰トラッカーの座標で表す。</summary>
        public readonly Vector3 position;

        /// <summary>回転の変化 Δq。Re-center時の入力トラッカーの向きを基準にした回転（その局所軸まわり）。</summary>
        public readonly Quaternion rotation;

        public PoseDelta(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public static PoseDelta Zero => new PoseDelta(Vector3.zero, Quaternion.identity);

        /// <summary>PoseFilterに通すときなど、Poseとして扱うための変換。</summary>
        public Pose ToPose() => new Pose(position, rotation);

        public static PoseDelta FromPose(Pose pose) => new PoseDelta(pose.position, pose.rotation);

        public override string ToString() => $"Δp={position.ToString("F4")}, Δq={rotation.eulerAngles.ToString("F1")}";
    }

    /// <summary>
    /// Re-center（基準姿勢の記録）と、腰トラッカーを基準にした変位の計算。
    /// トラッカーの空間の中だけで計算するので、Questの空間と合わせる必要がなく、体ごと向きを変えても変位は出ない。
    /// <code>
    /// Δp = Rw⁻¹(p − pw) − Rw0⁻¹(p0 − pw0)
    /// Δq = (Rw0⁻¹ q0)⁻¹ (Rw⁻¹ q)
    /// </code>
    /// 添字 w は腰トラッカー、0 はRe-center時の値。
    /// </summary>
    public class NeutralPoseCalibrator
    {
        Pose neutralLocal = new Pose(Vector3.zero, Quaternion.identity);

        /// <summary>Re-centerが済んでいるか。済むまでは変位を0として返す。</summary>
        public bool IsCalibrated { get; private set; }

        /// <summary>Re-center時の、腰トラッカーから見た入力トラッカーの姿勢。</summary>
        public Pose NeutralLocalPose => neutralLocal;

        /// <summary>入力の姿勢を、基準（腰トラッカー）の座標で表す。</summary>
        public static Pose ToReferenceLocal(Pose reference, Pose input)
        {
            var inverse = Quaternion.Inverse(reference.rotation);
            return new Pose(inverse * (input.position - reference.position), inverse * input.rotation);
        }

        /// <summary>今の姿勢を基準姿勢として記録する。以後の変位はこの姿勢からの変化になる。</summary>
        public void Recenter(Pose reference, Pose input)
        {
            var local = ToReferenceLocal(reference, input);
            neutralLocal = new Pose(local.position, Quaternion.Normalize(local.rotation));
            IsCalibrated = true;
        }

        /// <summary>基準姿勢を消す。</summary>
        public void Clear()
        {
            neutralLocal = new Pose(Vector3.zero, Quaternion.identity);
            IsCalibrated = false;
        }

        /// <summary>基準姿勢からの変位を計算する。Re-center前は0を返す。</summary>
        public PoseDelta Compute(Pose reference, Pose input)
        {
            if (!IsCalibrated) return PoseDelta.Zero;

            var local = ToReferenceLocal(reference, input);
            var deltaPosition = local.position - neutralLocal.position;
            var deltaRotation = Quaternion.Normalize(Quaternion.Inverse(neutralLocal.rotation) * local.rotation);
            return new PoseDelta(deltaPosition, deltaRotation);
        }
    }
}
