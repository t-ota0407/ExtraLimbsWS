using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// One Euro Filter（Casiez, Roussel and Vogel, CHI 2012）の共通の計算。
    /// ゆっくりした動きでは強く平滑化して揺れを抑え、速い動きでは遮断周波数を上げて遅れを小さくする。
    /// </summary>
    public static class OneEuro
    {
        /// <summary>遮断周波数 cutoffHz の1次ローパスフィルタの係数（0〜1）。</summary>
        public static float Alpha(float cutoffHz, float dt)
        {
            if (cutoffHz <= 0f) return 0f;
            float tau = 1f / (2f * Mathf.PI * cutoffHz);
            return 1f / (1f + tau / dt);
        }
    }

    /// <summary>位置（Vector3）のOne Euro Filter。速さは m/s で評価する。</summary>
    public class OneEuroFilterVector3
    {
        public float minCutoff;
        public float beta;
        public float derivativeCutoff;

        bool initialized;
        Vector3 previousValue;
        Vector3 previousDerivative;

        public OneEuroFilterVector3(float minCutoff = 1f, float beta = 0f, float derivativeCutoff = 1f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.derivativeCutoff = derivativeCutoff;
        }

        /// <summary>内部状態を消す。次の値はそのまま通る。</summary>
        public void Reset() => initialized = false;

        public Vector3 Filter(Vector3 value, float dt)
        {
            if (!initialized)
            {
                initialized = true;
                previousValue = value;
                previousDerivative = Vector3.zero;
                return value;
            }
            if (dt <= 0f) return previousValue;

            var rawDerivative = (value - previousValue) / dt;
            var derivative = Vector3.LerpUnclamped(previousDerivative, rawDerivative, OneEuro.Alpha(derivativeCutoff, dt));
            float cutoff = minCutoff + beta * derivative.magnitude;
            var result = Vector3.LerpUnclamped(previousValue, value, OneEuro.Alpha(cutoff, dt));

            previousValue = result;
            previousDerivative = derivative;
            return result;
        }
    }

    /// <summary>回転（Quaternion）のOne Euro Filter。速さは rad/s で評価する。</summary>
    public class OneEuroFilterQuaternion
    {
        public float minCutoff;
        public float beta;
        public float derivativeCutoff;

        bool initialized;
        Quaternion previousValue = Quaternion.identity;
        float previousSpeed;

        public OneEuroFilterQuaternion(float minCutoff = 1f, float beta = 0f, float derivativeCutoff = 1f)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.derivativeCutoff = derivativeCutoff;
        }

        /// <summary>内部状態を消す。次の値はそのまま通る。</summary>
        public void Reset() => initialized = false;

        public Quaternion Filter(Quaternion value, float dt)
        {
            value = Quaternion.Normalize(value);
            if (!initialized)
            {
                initialized = true;
                previousValue = value;
                previousSpeed = 0f;
                return value;
            }
            if (dt <= 0f) return previousValue;

            // q と -q は同じ回転なので、近い側にそろえてから補間する
            if (Quaternion.Dot(previousValue, value) < 0f)
                value = new Quaternion(-value.x, -value.y, -value.z, -value.w);

            float rawSpeed = Quaternion.Angle(previousValue, value) * Mathf.Deg2Rad / dt;
            float speed = Mathf.LerpUnclamped(previousSpeed, rawSpeed, OneEuro.Alpha(derivativeCutoff, dt));
            float cutoff = minCutoff + beta * speed;
            var result = Quaternion.Normalize(Quaternion.Slerp(previousValue, value, OneEuro.Alpha(cutoff, dt)));

            previousValue = result;
            previousSpeed = speed;
            return result;
        }
    }

    /// <summary>
    /// 姿勢（位置と回転）をまとめて平滑化する。トラッカーの微小な揺れを抑えるために、変位（Δp, Δq）に掛ける。
    /// Re-centerの直後やトラッキングが切れた後は Reset() する。
    /// </summary>
    public class PoseFilter
    {
        readonly OneEuroFilterVector3 position = new OneEuroFilterVector3();
        readonly OneEuroFilterQuaternion rotation = new OneEuroFilterQuaternion();

        /// <summary>false のときは平滑化せずにそのまま返す。</summary>
        public bool Enabled { get; set; } = true;

        public PoseFilter()
            : this(new PoseFilterSettings())
        {
        }

        public PoseFilter(PoseFilterSettings settings) => Configure(settings);

        /// <summary>設定を反映する。内部状態は消さない。</summary>
        public void Configure(PoseFilterSettings settings)
        {
            Enabled = settings.enabled;
            position.minCutoff = settings.minCutoff;
            position.beta = settings.beta;
            position.derivativeCutoff = settings.derivativeCutoff;
            rotation.minCutoff = settings.minCutoff;
            rotation.beta = settings.beta;
            rotation.derivativeCutoff = settings.derivativeCutoff;
        }

        public void Reset()
        {
            position.Reset();
            rotation.Reset();
        }

        public Pose Filter(Pose pose, float dt)
        {
            if (!Enabled) return pose;
            return new Pose(position.Filter(pose.position, dt), rotation.Filter(pose.rotation, dt));
        }

        public PoseDelta Filter(PoseDelta delta, float dt) => PoseDelta.FromPose(Filter(delta.ToPose(), dt));
    }
}
