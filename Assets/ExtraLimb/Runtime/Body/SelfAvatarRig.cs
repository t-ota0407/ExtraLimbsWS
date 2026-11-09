using System;
using ExtraLimb.Tracking;
using RootMotion.FinalIK;
using UnityEngine;

namespace ExtraLimb.Body
{
    /// <summary>
    /// 参加者自身のアバター。頭と両手の入力ソース（標準は Quest の "ovr:head" など）から VRIK のターゲットを動かし、
    /// 体を解く。解いた直後に BodySolved を呼ぶので、追加肢はそこで解く（根元が1フレーム遅れない）。
    /// 一人称で頭のメッシュが視界を遮らないよう、頭の骨を小さくする。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class SelfAvatarRig : MonoBehaviour, ITrackerProviderStatus
    {
        [SerializeField] TrackerRegistryHost registryHost;
        [SerializeField] VRIK ik;

        [Header("入力ソースのID")]
        [SerializeField] string headSourceId = "ovr:head";
        [SerializeField] string leftHandSourceId = "ovr:left_hand";
        [SerializeField] string rightHandSourceId = "ovr:right_hand";

        [Header("ターゲットのずれ（入力ソースの座標で）")]
        [Tooltip("目の位置から頭の骨までのずれ（m）")]
        [SerializeField] Vector3 headPositionOffset = new Vector3(0f, -0.09f, -0.08f);
        [Tooltip("コントローラーの位置から手首までのずれ（m）。左右で共通")]
        [SerializeField] Vector3 handPositionOffset = new Vector3(0f, -0.02f, -0.08f);
        [Tooltip("回転のずれを、起動時のTポーズの骨の向きから自動で求める。モデルごとの骨の軸の違いを吸収する")]
        [SerializeField] bool autoRotationOffsets = true;
        [Tooltip("自動にしないときの、頭・左手・右手の回転のずれ（度）")]
        [SerializeField] Vector3 headRotationOffset = Vector3.zero;
        [SerializeField] Vector3 leftHandRotationOffset = new Vector3(0f, 90f, 90f);
        [SerializeField] Vector3 rightHandRotationOffset = new Vector3(0f, 270f, 270f);

        [Header("一人称の見え方")]
        [SerializeField] bool hideHead = true;
        [SerializeField] float hiddenHeadScale = 1e-4f;

        Transform headTarget, leftHandTarget, rightHandTarget;
        TrackerRegistry registry;
        Transform headBone;
        Vector3 headBoneScale = Vector3.one;
        Quaternion headRotation, leftHandRotation, rightHandRotation;
        bool headEverTracked, headTrackedNow, warnedNoHead;
        float enabledTime;
        int preUpdateCount;

        public VRIK IK => ik;
        public Animator Animator { get; private set; }

        /// <summary>頭・両手のターゲットのうち、追跡できているものの数。</summary>
        public int TrackedTargetCount { get; private set; }

        /// <summary>VRIKが体を解き終えた直後（同じフレーム）。追加肢の処理はここで行う。</summary>
        public event Action BodySolved;

        public string ProviderName => "自分のアバター";

        public string StatusText
        {
            get
            {
                if (registry == null) return "TrackerRegistryHost がありません";
                if (!registry.TryGet(headSourceId, out _)) return $"頭の入力ソース {headSourceId} が見つかりません";
                if (preUpdateCount == 0) return "VRIK がまだ動いていません";
                if (!headEverTracked) return $"頭（{headSourceId}）の追跡を待っています。体は動かしません";
                return $"頭 {(headTrackedNow ? "追跡中" : "追跡なし（最後の姿勢）")}、追跡中のターゲット {TrackedTargetCount}/3";
            }
        }

        void Reset()
        {
            // エディタで付けたときに VRIK を用意して骨を自動で割り当てる
            ik = GetComponent<VRIK>();
            if (ik == null) ik = gameObject.AddComponent<VRIK>();
            if (!ik.references.isFilled) ik.AutoDetectReferences();
        }

        void Awake()
        {
            Animator = GetComponent<Animator>();
            if (ik == null) ik = GetComponent<VRIK>();
            if (ik == null) ik = gameObject.AddComponent<VRIK>();
            if (!ik.references.isFilled) ik.AutoDetectReferences();

            headTarget = CreateTarget("SelfAvatar_HeadTarget");
            leftHandTarget = CreateTarget("SelfAvatar_LeftHandTarget");
            rightHandTarget = CreateTarget("SelfAvatar_RightHandTarget");
            ik.solver.spine.headTarget = headTarget;
            ik.solver.leftArm.target = leftHandTarget;
            ik.solver.rightArm.target = rightHandTarget;
            ik.solver.spine.positionWeight = ik.solver.spine.rotationWeight = 1f;
            ik.solver.leftArm.positionWeight = ik.solver.leftArm.rotationWeight = 1f;
            ik.solver.rightArm.positionWeight = ik.solver.rightArm.rotationWeight = 1f;

            headBone = Animator.isHuman ? Animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (headBone != null) headBoneScale = headBone.localScale;
            ComputeRotationOffsets();
        }

        // 入力ソースが「正面を向いた頭」「前に向けて握ったコントローラー」のときに、
        // Tポーズの頭と手の骨の向き（アバターの根元から見た値）になる回転を求める
        void ComputeRotationOffsets()
        {
            headRotation = Quaternion.Euler(headRotationOffset);
            leftHandRotation = Quaternion.Euler(leftHandRotationOffset);
            rightHandRotation = Quaternion.Euler(rightHandRotationOffset);
            if (!autoRotationOffsets || !Animator.isHuman) return;

            var head = Animator.GetBoneTransform(HumanBodyBones.Head);
            var leftHand = Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var rightHand = Animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (head == null || leftHand == null || rightHand == null) return;

            var rootInverse = Quaternion.Inverse(transform.rotation);
            headRotation = rootInverse * head.rotation;
            leftHandRotation = HeldFromTPose(isLeft: true) * (rootInverse * leftHand.rotation);
            rightHandRotation = HeldFromTPose(isLeft: false) * (rootInverse * rightHand.rotation);
        }

        // Tポーズの手（指先が体の外、手のひらが下）を、コントローラーを前に向けて握った手（指先が前、手のひらが内側）に回す回転
        static Quaternion HeldFromTPose(bool isLeft)
        {
            var outward = isLeft ? Vector3.left : Vector3.right;
            var tPose = Quaternion.LookRotation(outward, Vector3.up);             // 指先＝外、手の甲＝上
            var held = Quaternion.LookRotation(Vector3.forward, outward);         // 指先＝前、手の甲＝外
            return held * Quaternion.Inverse(tPose);
        }

        void OnEnable()
        {
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            registry = registryHost != null ? registryHost.Registry : null;
            if (registry == null) Debug.LogError("[ExtraLimb] TrackerRegistryHost がシーンにないため、アバターを動かせません");
            ik.solver.OnPreUpdate += UpdateTargets;
            ik.solver.OnPostUpdate += OnBodySolved;
            SetHeadHidden(hideHead);
            enabledTime = Time.time;
        }

        void Update()
        {
            // 再生が5秒進んでも頭を追跡できなければ、原因を探すための情報を一度だけ出す
            if (headEverTracked || warnedNoHead || Time.time - enabledTime < 5f) return;
            // 頭の入力ソースそのものがないシーン（DesktopDev など）では警告しない
            if (registry == null || !registry.TryGet(headSourceId, out _)) return;
            warnedNoHead = true;
            Debug.LogWarning($"[ExtraLimb] 自分のアバター：5秒たっても頭を追跡できません（{StatusText}）");
        }

        void OnDisable()
        {
            if (ik != null)
            {
                ik.solver.OnPreUpdate -= UpdateTargets;
                ik.solver.OnPostUpdate -= OnBodySolved;
            }
            SetHeadHidden(false);
        }

        /// <summary>頭のメッシュを隠すか（一人称のときは隠す）。</summary>
        public void SetHeadHidden(bool hidden)
        {
            if (headBone != null) headBone.localScale = hidden ? Vector3.one * hiddenHeadScale : headBoneScale;
        }

        // VRIK が解く直前に、入力ソースの今の姿勢をターゲットに写す（同じフレームの値を使うため）
        void UpdateTargets()
        {
            if (registry == null) return;
            int tracked = 0;
            preUpdateCount++;
            bool headTracked = Follow(headSourceId, headTarget, headPositionOffset, headRotation);
            headTrackedNow = headTracked;
            if (headTracked) tracked++;
            if (Follow(leftHandSourceId, leftHandTarget, handPositionOffset, leftHandRotation)) tracked++;
            if (Follow(rightHandSourceId, rightHandTarget, handPositionOffset, rightHandRotation)) tracked++;
            TrackedTargetCount = tracked;

            // 頭が一度も追跡できていないうちは解かない（ターゲットが床にあると体が潰れるため）
            if (headTracked && !headEverTracked)
            {
                headEverTracked = true;
                Debug.Log($"[ExtraLimb] 自分のアバター：頭の追跡を始めました（{headSourceId}）");
            }
            ik.solver.IKPositionWeight = headEverTracked ? 1f : 0f;
        }

        // 追跡が切れたら、ターゲットは最後の姿勢のまま
        bool Follow(string sourceId, Transform target, Vector3 positionOffset, Quaternion rotationOffset)
        {
            if (!registry.TryGet(sourceId, out var source) || !source.TryGetPose(out var pose)) return false;
            target.SetPositionAndRotation(pose.position + pose.rotation * positionOffset, pose.rotation * rotationOffset);
            return true;
        }

        void OnBodySolved() => BodySolved?.Invoke();

        Transform CreateTarget(string targetName)
        {
            var target = new GameObject(targetName).transform;
            target.SetParent(transform.parent, false);
            target.SetPositionAndRotation(transform.position, transform.rotation);
            return target;
        }
    }
}
