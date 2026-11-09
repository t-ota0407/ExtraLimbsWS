using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Limb
{
    /// <summary>追加肢を生やす側。</summary>
    public enum LimbSide
    {
        Left = 0,
        Right = 1,
    }

    /// <summary>追加肢のターゲットを表す座標の向きを、どの骨に合わせるか。</summary>
    public enum LimbFrameBone
    {
        /// <summary>追加肢が付いている骨（肩の上なら Spine2）。追加肢の根元と一緒に動く。</summary>
        AttachBone = 0,

        /// <summary>腰（Hips）。胸が回っても手先の位置は動かない。</summary>
        Hips = 1,
    }

    /// <summary>
    /// アバターに付いた追加肢1本（上腕・前腕・手）。骨は名前（例：sub_RightArm）で探す。
    /// ターゲットは「根元の座標」で受け取る：原点は上腕の付け根、軸の向きは体と同じ（x=右、y=上、z=前）。
    /// Tポーズのときに体の向きとそろうように、起動時に骨の向きとの差を覚える。
    /// 手先の既定位置と肘の向きは、x を「外向きが正」で書き、左の肢では左右を反転して使う。
    /// どこからも動かされないフレームは、手首を既定位置に置く（モデルの初期姿勢のまま腕が突き出ないように）。
    /// 実行順は VRIK（Final IK のインスペクタが 9998 に設定する）より後、LimbSession より前。
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class ExtraLimbRig : MonoBehaviour
    {
        [Tooltip("アバターの根元（Animator がある Transform）。空ならこの GameObject から上へ探す")]
        [SerializeField] Transform avatarRoot;
        [SerializeField] LimbSide side = LimbSide.Right;
        [Tooltip("表示するか。使わない肢は隠す（鎖骨の骨を小さくする）")]
        [SerializeField] bool visible = true;

        [Header("骨（空なら名前で探す：接頭辞＋Left/Right＋Arm・ForeArm・Hand）")]
        [SerializeField] string bonePrefix = "sub_";
        [SerializeField] Transform upperArm;
        [SerializeField] Transform forearm;
        [SerializeField] Transform hand;
        [Tooltip("ボタンなどに触れる点。空なら中指の先（〜HandMiddle4_end）、それもなければ手首から前腕の延長に8cm")]
        [SerializeField] Transform contactTip;

        [Header("根元の座標（x は外向きが正、m）")]
        [SerializeField] LimbFrameBone frameBone = LimbFrameBone.AttachBone;
        [Tooltip("手首の既定位置（制御則の基準位置 p_b）")]
        [SerializeField] Vector3 restHandPosition = new Vector3(0.12f, -0.06f, 0.30f);
        [Tooltip("肘を向けたい点")]
        [SerializeField] Vector3 bendHint = new Vector3(0.35f, -0.10f, -0.05f);

        [Header("届く範囲（根元を中心とする球殻）")]
        [Tooltip("腕の長さ（上腕＋前腕）に対する最大距離の割合。伸び切る手前で止める")]
        [Range(0.5f, 1f)]
        [SerializeField] float maxReachRatio = 0.95f;
        [SerializeField] float minReach = 0.12f;

        const float FallbackTipDistance = 0.08f;
        const float HiddenScale = 1e-4f;

        ILimbIKSolver solver;
        Transform frameSource;
        Quaternion frameOffset = Quaternion.identity;
        Vector3 tipLocal;
        Transform visibilityBone;
        Vector3 visibilityBoneScale = Vector3.one;
        int lastSolveFrame = -1;
        bool initialized;

        public LimbSide Side => side;
        public bool IsReady => initialized;

        /// <summary>表示するか。false にすると鎖骨から先を小さくして隠す。</summary>
        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
                ApplyVisibility();
            }
        }
        public Transform UpperArm => upperArm;
        public Transform Forearm => forearm;
        public Transform Hand => hand;
        public float UpperLength { get; private set; }
        public float LowerLength { get; private set; }

        /// <summary>腕の長さ（上腕＋前腕、m）。</summary>
        public float Reach => UpperLength + LowerLength;

        /// <summary>手首の既定位置（根元の座標、左右反転済み）。</summary>
        public Vector3 RestHandPosition => Mirror(restHandPosition);

        /// <summary>肘を向けたい点（根元の座標、左右反転済み）。</summary>
        public Vector3 BendHint => Mirror(bendHint);

        /// <summary>根元の座標の、ワールドでの姿勢。</summary>
        public Pose Frame
        {
            get
            {
                if (!initialized) return new Pose(transform.position, transform.rotation);
                return new Pose(upperArm.position, frameSource.rotation * frameOffset);
            }
        }

        /// <summary>触れる点（ワールド座標）。</summary>
        public Vector3 ContactPoint => initialized ? hand.TransformPoint(tipLocal) : transform.position;

        /// <summary>「外向きが正」の x を、この肢の左右に合わせる。</summary>
        public Vector3 Mirror(Vector3 v) => side == LimbSide.Left ? new Vector3(-v.x, v.y, v.z) : v;

        public Vector3 FrameToWorld(Vector3 local)
        {
            var frame = Frame;
            return frame.position + frame.rotation * local;
        }

        public Vector3 WorldToFrame(Vector3 world)
        {
            var frame = Frame;
            return Quaternion.Inverse(frame.rotation) * (world - frame.position);
        }

        /// <summary>届く範囲（根元の座標）。</summary>
        public WorkspaceLimiter CreateWorkspace()
        {
            float maxRadius = Mathf.Max(Reach * maxReachRatio, minReach + 0.01f);
            return new WorkspaceLimiter(Vector3.zero, minReach, maxRadius) { FallbackDirection = Vector3.forward };
        }

        void Awake() => Initialize();

        void OnValidate()
        {
            if (initialized) ApplyVisibility();
        }

        void OnDisable()
        {
            if (initialized && visibilityBone != null) visibilityBone.localScale = visibilityBoneScale;
        }

        void OnEnable()
        {
            if (initialized) ApplyVisibility();
        }

        // 誰も動かさなかったフレームは、手首を既定位置に置く
        void LateUpdate()
        {
            if (initialized && lastSolveFrame != Time.frameCount) SolveLocal(RestHandPosition);
        }

        void ApplyVisibility()
        {
            if (visibilityBone != null) visibilityBone.localScale = visible ? visibilityBoneScale : Vector3.one * HiddenScale;
        }

        /// <summary>テストから骨の場所を与えるときに使う。</summary>
        internal void Configure(Transform root, LimbSide limbSide)
        {
            avatarRoot = root;
            side = limbSide;
            initialized = false;
        }

        /// <summary>骨を探し、Tポーズでの向きを覚え、IKを用意する。アバターがTポーズのうちに呼ぶ（Awake）。</summary>
        internal bool Initialize()
        {
            if (initialized) return true;
            if (avatarRoot == null)
            {
                var animator = GetComponentInParent<Animator>();
                avatarRoot = animator != null ? animator.transform : transform;
            }

            string sideName = side == LimbSide.Left ? "Left" : "Right";
            if (upperArm == null) upperArm = FindBone(bonePrefix + sideName + "Arm");
            if (forearm == null) forearm = FindBone(bonePrefix + sideName + "ForeArm");
            if (hand == null) hand = FindBone(bonePrefix + sideName + "Hand");
            if (contactTip == null) contactTip = FindBone(bonePrefix + sideName + "HandMiddle4_end");
            if (upperArm == null || forearm == null || hand == null)
            {
                Debug.LogError($"[ExtraLimb] 追加肢の骨が見つかりません（{bonePrefix}{sideName}Arm・ForeArm・Hand）。ExtraLimbRig に骨を割り当ててください", this);
                return false;
            }

            frameSource = ResolveFrameSource();
            frameOffset = Quaternion.Inverse(frameSource.rotation) * avatarRoot.rotation;
            UpperLength = Vector3.Distance(upperArm.position, forearm.position);
            LowerLength = Vector3.Distance(forearm.position, hand.position);

            if (contactTip != null)
            {
                tipLocal = hand.InverseTransformPoint(contactTip.position);
            }
            else
            {
                var direction = (hand.position - forearm.position).normalized;
                tipLocal = hand.InverseTransformPoint(hand.position + direction * FallbackTipDistance);
            }

            solver = new FinalIKLimbSolver();
            if (!solver.Initiate(avatarRoot, upperArm, forearm, hand))
            {
                Debug.LogError("[ExtraLimb] 追加肢のIKを初期化できませんでした（骨の位置が重なっていないか確認してください）", this);
                return false;
            }

            visibilityBone = upperArm.parent != null ? upperArm.parent : upperArm;
            visibilityBoneScale = visibilityBone.localScale;
            initialized = true;
            ApplyVisibility();
            return true;
        }

        Transform ResolveFrameSource()
        {
            if (frameBone == LimbFrameBone.Hips)
            {
                var animator = avatarRoot.GetComponent<Animator>();
                var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                if (hips != null) return hips;
                Debug.LogWarning("[ExtraLimb] Hips が見つからないため、追加肢の付いている骨を根元の向きに使います", this);
            }
            // 鎖骨（上腕の親）がつながっている骨
            var clavicle = upperArm.parent;
            if (clavicle != null && clavicle.parent != null) return clavicle.parent;
            return clavicle != null ? clavicle : upperArm;
        }

        Transform FindBone(string boneName)
        {
            foreach (var child in avatarRoot.GetComponentsInChildren<Transform>(true))
                if (child.name == boneName) return child;
            return null;
        }

        /// <summary>手首をターゲット（ワールド座標）へ動かす。届かないときは、その向きへ腕を伸ばす。</summary>
        public void Solve(Vector3 targetWorld, float weight = 1f)
        {
            if (!initialized) return;
            lastSolveFrame = Time.frameCount;
            solver.Solve(targetWorld, FrameToWorld(BendHint), weight);
        }

        /// <summary>手首を根元の座標の点へ動かす。</summary>
        public void SolveLocal(Vector3 targetLocal, float weight = 1f) => Solve(FrameToWorld(targetLocal), weight);
    }
}
