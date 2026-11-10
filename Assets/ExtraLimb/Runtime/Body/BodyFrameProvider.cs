using System;
using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Body
{
    /// <summary>
    /// アバターの骨の「体の向きにそろえたフレーム」を返す（Quest側の胴体フレーム）。
    /// 起動時のTポーズで、各骨の向きとアバターの根元の向きの差を覚えておき、
    /// VRIKで体を解いた後も、骨の位置と「体にとっての右・上・前」を返せるようにする。
    /// 骨の拡縮（一人称で頭を隠すための縮小など）には影響されない。
    /// 身長・肩幅を入れると、アバター全体を拡縮し、Attach Point のずれの拡縮（BodyCalibration）も更新する。
    /// トラッカー側の基準（腰トラッカー）は LimbControlPipeline が持つ。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class BodyFrameProvider : MonoBehaviour
    {
        [Tooltip("モデルの身長（m）。頭頂の高さ")]
        [SerializeField] float modelHeight = 1.80f;
        [Tooltip("モデルの肩幅（m）。0なら左右の上腕の付け根の間を測る")]
        [SerializeField] float modelShoulderWidth = 0f;

        readonly Dictionary<HumanBodyBones, Quaternion> alignments = new Dictionary<HumanBodyBones, Quaternion>();
        Animator animator;
        Vector3 baseScale = Vector3.one;
        bool initialized;

        public BodyCalibration Calibration { get; private set; }
        public Animator Animator => animator;
        public bool IsReady => initialized;

        /// <summary>身長・肩幅を入れてアバターの大きさが変わった直後。</summary>
        public event Action CalibrationChanged;

        void Awake() => Initialize();

        /// <summary>Tポーズのうちに呼ぶ（Awake。ほかの部品から先に呼ばれることもある）。2回目以降は何もしない。</summary>
        public bool Initialize()
        {
            if (initialized) return true;
            animator = GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                Debug.LogError("[ExtraLimb] BodyFrameProvider：Humanoid の Animator が必要です", this);
                return false;
            }

            var rootRotation = transform.rotation;
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(bone);
                if (t != null) alignments[bone] = Quaternion.Inverse(t.rotation) * rootRotation;
            }

            float shoulder = modelShoulderWidth;
            if (shoulder <= 0f)
            {
                var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                shoulder = left != null && right != null ? Vector3.Distance(left.position, right.position) / Mathf.Max(transform.lossyScale.x, 1e-6f) : 0.38f;
            }
            Calibration = new BodyCalibration(modelHeight, shoulder);
            baseScale = transform.localScale;
            initialized = true;
            return true;
        }

        /// <summary>骨のフレーム：位置は骨の位置、向きは「Tポーズのときの体の向き」が骨と一緒に回ったもの。</summary>
        public bool TryGetFrame(HumanBodyBones bone, out Pose frame)
        {
            frame = new Pose(transform.position, transform.rotation);
            if (!initialized || !alignments.TryGetValue(bone, out var alignment)) return false;
            var t = animator.GetBoneTransform(bone);
            if (t == null) return false;
            frame = new Pose(t.position, t.rotation * alignment);
            return true;
        }

        /// <summary>骨のフレーム。骨がなければアバターの根元。</summary>
        public Pose GetFrame(HumanBodyBones bone)
        {
            TryGetFrame(bone, out var frame);
            return frame;
        }

        /// <summary>
        /// 参加者の身長・肩幅（m）を入れて、アバターを拡縮する。範囲外なら何もせず false。
        /// 拡縮はアバターの根元にかける（VRIK はそのまま動く）。
        /// </summary>
        public bool ApplyBodyDimensions(float height, float shoulderWidth)
        {
            if (!initialized || !Calibration.TrySet(height, shoulderWidth)) return false;
            transform.localScale = baseScale * Calibration.AvatarScale;
            Debug.Log($"[ExtraLimb] 体の寸法：身長 {height * 100f:F0} cm、肩幅 {shoulderWidth * 100f:F0} cm → アバターの拡縮 {Calibration.AvatarScale:F3}、左右の比 {Calibration.LateralRatio:F3}");
            CalibrationChanged?.Invoke();
            return true;
        }

        /// <summary>モデルの大きさに戻す。</summary>
        public void ResetBodyDimensions()
        {
            if (!initialized) return;
            Calibration.Reset();
            transform.localScale = baseScale;
            CalibrationChanged?.Invoke();
        }
    }
}
