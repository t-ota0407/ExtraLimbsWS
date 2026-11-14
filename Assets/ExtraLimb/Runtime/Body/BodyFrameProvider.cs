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
    /// 手の長さ（BodyCalibration.HandLength）に合わせて、本来の左右の手の骨を拡縮する（追加肢の手は LimbSession が ExtraLimbRig に入れる）。
    /// 手の長さは手首（手の骨の位置）から見た目の中指の先（中指の第3関節の子。HandMiddle4 の根元）までをまっすぐ測る。
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
        Transform leftHand, rightHand;
        Vector3 leftHandScale = Vector3.one, rightHandScale = Vector3.one;
        // 手首から中指の先まで（アバターの根元の座標。手の骨は拡縮しないとき）。測れなければ 0
        float leftHandLocalLength, rightHandLocalLength;
        bool initialized;

        public BodyCalibration Calibration { get; private set; }
        public Animator Animator => animator;
        public bool IsReady => initialized;

        /// <summary>手の骨を拡縮しないときの手の長さ（m。今のアバターの大きさで。右手）。測れなければ 0。</summary>
        public float UnscaledHandLength => rightHandLocalLength * RootScale;

        /// <summary>今の本来の手の長さ（m。手首から見た目の中指の先まで、右手）。</summary>
        public float HandLength => UnscaledHandLength * (rightHand != null ? rightHand.localScale.x / Mathf.Max(rightHandScale.x, 1e-6f) : 1f);

        float RootScale => Mathf.Max(transform.lossyScale.x, 1e-6f);

        /// <summary>身長・肩幅・手の長さを入れてアバターの大きさが変わった直後。</summary>
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
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (leftHand != null) leftHandScale = leftHand.localScale;
            if (rightHand != null) rightHandScale = rightHand.localScale;
            leftHandLocalLength = MeasureHand(leftHand, HumanBodyBones.LeftMiddleDistal);
            rightHandLocalLength = MeasureHand(rightHand, HumanBodyBones.RightMiddleDistal);
            initialized = true;
            ApplyHandScale();
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
            if (!initialized) return false;
            return ApplyBodyDimensions(height, shoulderWidth, Calibration.HandLength);
        }

        /// <summary>
        /// 参加者の身長・肩幅・手の長さ（m）を入れて、アバターと本来の手を拡縮する。どれかが範囲外なら何もせず false。
        /// 手の長さは身長とは別に合わせる（身長だけ変えても、手の長さは入れた長さのまま）。
        /// </summary>
        public bool ApplyBodyDimensions(float height, float shoulderWidth, float handLength)
        {
            if (!initialized || !Calibration.TrySet(height, shoulderWidth, handLength)) return false;
            transform.localScale = baseScale * Calibration.AvatarScale;
            ApplyHandScale();
            Debug.Log($"[ExtraLimb] 体の寸法：身長 {height * 100f:F0} cm、肩幅 {shoulderWidth * 100f:F0} cm、手の長さ {handLength * 100f:F1} cm → アバターの拡縮 {Calibration.AvatarScale:F3}、左右の比 {Calibration.LateralRatio:F3}");
            CalibrationChanged?.Invoke();
            return true;
        }

        /// <summary>手の長さ（m）を入れて、本来の左右の手を拡縮する。範囲外なら何もせず false。</summary>
        public bool SetHandLength(float handLength)
        {
            if (!initialized || !Calibration.TrySetHandLength(handLength)) return false;
            ApplyHandScale();
            CalibrationChanged?.Invoke();
            return true;
        }

        /// <summary>モデルの大きさに戻す（手の長さは初期値に戻す）。</summary>
        public void ResetBodyDimensions()
        {
            if (!initialized) return;
            Calibration.Reset();
            transform.localScale = baseScale;
            ApplyHandScale();
            CalibrationChanged?.Invoke();
        }

        // 手首の骨を拡縮する（指は子なので一緒に縮む。手首の位置は VRIK がコントローラーに合わせる）。アバターの拡縮を変えたら呼び直す
        void ApplyHandScale()
        {
            if (leftHand != null) leftHand.localScale = leftHandScale * Calibration.HandBoneScale(leftHandLocalLength * RootScale);
            if (rightHand != null) rightHand.localScale = rightHandScale * Calibration.HandBoneScale(rightHandLocalLength * RootScale);
        }

        // 手首から見た目の中指の先（第3関節の子）まで、アバターの根元の座標で測る。Tポーズで指を伸ばしているうちに呼ぶ
        float MeasureHand(Transform hand, HumanBodyBones middleDistal)
        {
            var distal = animator.GetBoneTransform(middleDistal);
            if (hand == null || distal == null) return 0f;
            var tip = distal.childCount > 0 ? distal.GetChild(0) : distal;
            return transform.InverseTransformVector(tip.position - hand.position).magnitude;
        }
    }
}
