using System;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// 追加肢を付ける姿勢。上腕の付け根の位置、手先のターゲットを表す座標（根元の座標）の向き、
    /// 手首の既定位置と肘を向けたい点（根元の座標、x は外向きが正）。
    /// </summary>
    public readonly struct MountPose
    {
        /// <summary>上腕の付け根（ワールド座標）。</summary>
        public readonly Vector3 joint;

        /// <summary>根元の座標の向き（ワールド）。</summary>
        public readonly Quaternion frameRotation;

        /// <summary>手首の既定位置（根元の座標、x は外向きが正）。</summary>
        public readonly Vector3 restHandPosition;

        /// <summary>肘を向けたい点（根元の座標、x は外向きが正）。</summary>
        public readonly Vector3 bendHint;

        public MountPose(Vector3 joint, Quaternion frameRotation, Vector3 restHandPosition, Vector3 bendHint)
        {
            this.joint = joint;
            this.frameRotation = frameRotation;
            this.restHandPosition = restHandPosition;
            this.bendHint = bendHint;
        }

        /// <summary>2つの姿勢の間。t は 0〜1。</summary>
        public static MountPose Lerp(in MountPose from, in MountPose to, float t)
        {
            t = Mathf.Clamp01(t);
            return new MountPose(
                Vector3.Lerp(from.joint, to.joint, t),
                Quaternion.Slerp(from.frameRotation, to.frameRotation, t),
                Vector3.Lerp(from.restHandPosition, to.restHandPosition, t),
                Vector3.Lerp(from.bendHint, to.bendHint, t));
        }

        /// <summary>姿勢を、ある座標（frame）から見た値にする。付け根の位置と向きだけを変え、手首と肘の値はそのまま。</summary>
        public MountPose ToLocal(Pose frame)
        {
            var inverse = Quaternion.Inverse(frame.rotation);
            return new MountPose(inverse * (joint - frame.position), inverse * frameRotation, restHandPosition, bendHint);
        }

        /// <summary>ある座標（frame）から見た値を、ワールドに戻す。</summary>
        public MountPose ToWorld(Pose frame)
        {
            return new MountPose(frame.position + frame.rotation * joint, frame.rotation * frameRotation, restHandPosition, bendHint);
        }

        public override string ToString() => $"joint={joint.ToString("F3")}, frame={frameRotation.eulerAngles.ToString("F1")}, rest={restHandPosition.ToString("F3")}";
    }

    /// <summary>
    /// 付け替えの補間の進み具合。始めと終わりで速さが0になる（smoothstep）ので、腕が跳ねたり急に止まったりしない。
    /// </summary>
    public sealed class MountTransition
    {
        float elapsed;

        /// <summary>補間にかける時間（秒）。</summary>
        public float Duration { get; private set; }

        /// <summary>補間中か。</summary>
        public bool IsActive { get; private set; }

        /// <summary>経過時間の割合（0〜1、平滑化前）。</summary>
        public float LinearProgress => Duration <= 0f ? 1f : Mathf.Clamp01(elapsed / Duration);

        /// <summary>平滑化した進み具合（0〜1）。</summary>
        public float Progress => Smooth(LinearProgress);

        /// <summary>補間を始める。duration が0以下なら、すぐ終わる。</summary>
        public void Start(float duration)
        {
            if (float.IsNaN(duration)) throw new ArgumentException("duration が NaN", nameof(duration));
            Duration = Mathf.Max(0f, duration);
            elapsed = 0f;
            IsActive = Duration > 0f;
        }

        /// <summary>時間を進めて、平滑化した進み具合を返す。終わったら IsActive が false になる。</summary>
        public float Advance(float deltaTime)
        {
            if (!IsActive) return 1f;
            elapsed += Mathf.Max(0f, deltaTime);
            if (elapsed >= Duration) IsActive = false;
            return Progress;
        }

        /// <summary>smoothstep：3t² − 2t³。</summary>
        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
