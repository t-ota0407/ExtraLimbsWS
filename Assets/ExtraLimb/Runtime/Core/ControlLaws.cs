using System;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>制御則への入力。</summary>
    public readonly struct ControlInput
    {
        /// <summary>軸の割り当て後の変位 u = MΔp（m、胴体フレーム）。</summary>
        public readonly Vector3 displacement;

        public ControlInput(Vector3 displacement) => this.displacement = displacement;

        public static ControlInput Zero => new ControlInput(Vector3.zero);
    }

    /// <summary>制御則のパラメータ。長さはすべてメートル。</summary>
    public readonly struct ControlParams
    {
        /// <summary>位置ゲイン G_p（無次元）。</summary>
        public readonly float positionGain;

        /// <summary>速度ゲイン G_v（1/s）。</summary>
        public readonly float velocityGain;

        /// <summary>速度制御の不感帯の半径 d（m）。</summary>
        public readonly float deadzone;

        /// <summary>ハイブリッド制御の切替半径 r（m）。</summary>
        public readonly float hybridRadius;

        public ControlParams(float positionGain, float velocityGain, float deadzone, float hybridRadius)
        {
            this.positionGain = positionGain;
            this.velocityGain = velocityGain;
            this.deadzone = deadzone;
            this.hybridRadius = hybridRadius;
        }

        /// <summary>設定から作る（センチメートルをメートルに直す）。</summary>
        public static ControlParams From(LimbRuntimeConfig config)
        {
            return new ControlParams(config.positionGain, config.velocityGain, config.DeadzoneMeters, config.HybridRadiusMeters);
        }
    }

    /// <summary>
    /// 変位からIKターゲットの位置（胴体フレーム）を計算する制御則。
    /// 基準位置 p_b は、Attach Pointごとの既定の手先位置。Attach Pointを切り替えたら Reset() で戻す。
    /// </summary>
    public interface IControlLaw
    {
        ControlModeKind Mode { get; }

        /// <summary>基準位置 p_b。</summary>
        Vector3 BasePosition { get; }

        /// <summary>直近のターゲット p_t。</summary>
        Vector3 Target { get; }

        /// <summary>届く範囲。null なら制限しない。</summary>
        WorkspaceLimiter Workspace { get; set; }

        /// <summary>基準位置を設定し、ターゲットをそこに戻す。</summary>
        void Reset(Vector3 basePosition);

        /// <summary>1フレーム進めて、新しいターゲットを返す。</summary>
        Vector3 Step(in ControlInput input, in ControlParams parameters, float dt);
    }

    public abstract class ControlLawBase : IControlLaw
    {
        public abstract ControlModeKind Mode { get; }
        public Vector3 BasePosition { get; protected set; }
        public Vector3 Target { get; protected set; }
        public WorkspaceLimiter Workspace { get; set; }

        public virtual void Reset(Vector3 basePosition)
        {
            BasePosition = Limit(basePosition);
            Target = BasePosition;
        }

        public abstract Vector3 Step(in ControlInput input, in ControlParams parameters, float dt);

        protected Vector3 Limit(Vector3 point) => Workspace != null ? Workspace.Clamp(point) : point;

        /// <summary>u の向きと、半径 threshold を超えた分の長さ。超えていなければ0。</summary>
        protected static Vector3 Excess(Vector3 u, float threshold)
        {
            float magnitude = u.magnitude;
            if (magnitude <= threshold || magnitude < 1e-9f) return Vector3.zero;
            return u * ((magnitude - threshold) / magnitude);
        }
    }

    /// <summary>位置制御（0次）：p_t = p_b + G_p u。</summary>
    public sealed class PositionControlLaw : ControlLawBase
    {
        public override ControlModeKind Mode => ControlModeKind.Position;

        public override Vector3 Step(in ControlInput input, in ControlParams parameters, float dt)
        {
            Target = Limit(BasePosition + parameters.positionGain * input.displacement);
            return Target;
        }
    }

    /// <summary>
    /// 速度制御（1次、不感帯つき）：p_t ← p_t + G_v max(0, |u| − d) u/|u| Δt。
    /// 入力を不感帯に戻すと、手先はその場で止まって保持される。
    /// </summary>
    public sealed class RateControlLaw : ControlLawBase
    {
        public override ControlModeKind Mode => ControlModeKind.Rate;

        public override Vector3 Step(in ControlInput input, in ControlParams parameters, float dt)
        {
            if (dt <= 0f) return Target;
            var velocity = parameters.velocityGain * Excess(input.displacement, parameters.deadzone);
            Target = Limit(Target + velocity * dt);
            return Target;
        }
    }

    /// <summary>
    /// ハイブリッド制御：半径 r の内側は位置制御、外側では基準位置そのものを動かす（Bubble技法と同じ考え方）。
    /// <code>
    /// |u| ≤ r : p_t = p_b + G_p u
    /// |u| > r : p_b ← p_b + G_v (|u| − r) u/|u| Δt,  p_t = p_b + G_p u
    /// </code>
    /// 境界で基準位置の速さが0から連続に増えるので、手先は飛ばない。
    /// 基準位置を動かしている間に手先が範囲の端に当たったら、端で止まる位置まで基準位置を戻す。
    /// こうすると端で入力を続けても基準位置が範囲の外へ溜まらず、入力を戻し始めた瞬間に手先が戻り始める。
    /// </summary>
    public sealed class HybridControlLaw : ControlLawBase
    {
        public override ControlModeKind Mode => ControlModeKind.Hybrid;

        public override Vector3 Step(in ControlInput input, in ControlParams parameters, float dt)
        {
            var offset = parameters.positionGain * input.displacement;
            var excess = Excess(input.displacement, parameters.hybridRadius);
            bool drifting = dt > 0f && excess.sqrMagnitude > 0f;

            if (drifting) BasePosition += parameters.velocityGain * excess * dt;
            Target = Limit(BasePosition + offset);
            if (drifting) BasePosition = Target - offset;
            return Target;
        }
    }

    /// <summary>設定の制御方式から制御則を作る。</summary>
    public static class ControlLaws
    {
        public static IControlLaw Create(ControlModeKind mode)
        {
            switch (mode)
            {
                case ControlModeKind.Position: return new PositionControlLaw();
                case ControlModeKind.Rate: return new RateControlLaw();
                case ControlModeKind.Hybrid: return new HybridControlLaw();
                default: throw new ArgumentOutOfRangeException(nameof(mode), mode, "未対応の制御方式");
            }
        }
    }
}
