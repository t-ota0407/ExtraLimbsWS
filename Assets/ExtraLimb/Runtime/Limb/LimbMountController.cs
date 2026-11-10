using System;
using ExtraLimb.Body;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Limb
{
    /// <summary>
    /// 追加肢1本の付け根を Attach Point に置く。付け替えるときは、短い時間で前の位置から新しい位置へ補間する（腕が跳ねない）。
    /// 付け根は親の骨の子にせず、毎フレーム VRIK が体を解いた後に置き直す（親の骨が拡縮されても縮まない）。
    /// 付け替えの途中は、前の姿勢を「新しい親の骨から見た値」として持つので、補間中に体が動いても一緒に動く。
    /// LimbSession が動かしている肢では LimbSession が毎フレーム UpdateMount() を呼ぶ。そうでない肢は LateUpdate で自分で置く。
    /// </summary>
    [DefaultExecutionOrder(9999)]
    public class LimbMountController : MonoBehaviour
    {
        [SerializeField] ExtraLimbRig rig;
        [Tooltip("空ならアバターから探す")]
        [SerializeField] AttachPointRegistry registry;
        [SerializeField] string initialAttachPointId = "shoulder_top";
        [Tooltip("付け替えの補間にかける時間（秒）。0ならすぐ移る")]
        [SerializeField] float transitionSeconds = 0.35f;

        readonly MountTransition transition = new MountTransition();
        AttachPointDefinition current;
        MountPose fromLocal;
        int lastUpdateFrame = -1;
        bool initialized;

        public ExtraLimbRig Rig => rig;
        public AttachPointRegistry Registry => registry;
        public AttachPointDefinition Current => current;
        public string CurrentId => current != null ? current.id : "";

        /// <summary>付け替えの補間中か。</summary>
        public bool IsTransitioning => transition.IsActive;

        /// <summary>補間の進み具合（0〜1、平滑化済み）。補間中でなければ1。</summary>
        public float Progress => transition.IsActive ? transition.Progress : 1f;

        public float TransitionSeconds
        {
            get => transitionSeconds;
            set => transitionSeconds = Mathf.Max(0f, value);
        }

        /// <summary>付け替えが終わった（補間なしの付け替えでも呼ぶ）。</summary>
        public event Action<AttachPointDefinition> MountCompleted;

        void Awake() => Initialize();

        /// <summary>テストから部品を与えるときに使う。</summary>
        internal void Configure(ExtraLimbRig limbRig, AttachPointRegistry attachPoints, string initialId)
        {
            rig = limbRig;
            registry = attachPoints;
            initialAttachPointId = initialId;
            initialized = false;
        }

        /// <summary>部品を探し、最初の Attach Point に付ける（Tポーズのうちに呼ぶ）。</summary>
        internal bool Initialize()
        {
            if (initialized) return true;
            if (rig == null) rig = GetComponent<ExtraLimbRig>();
            if (registry == null) registry = GetComponentInParent<AttachPointRegistry>();
            if (rig == null || registry == null)
            {
                Debug.LogError("[ExtraLimb] LimbMountController：ExtraLimbRig か AttachPointRegistry がありません", this);
                return false;
            }
            if (!rig.Initialize() || !registry.Initialize()) return false;
            rig.Mount = this;
            initialized = true;
            if (!MountTo(initialAttachPointId, immediate: true) && registry.Count > 0) MountTo(registry.Get(0).id, immediate: true);
            return true;
        }

        /// <summary>Attach Point を付け替える。immediate なら補間しない。IDが見つからなければ false。</summary>
        public bool MountTo(string id, bool immediate = false)
        {
            if (!initialized) return false;
            if (!registry.TryGet(id, out var definition))
            {
                Debug.LogWarning($"[ExtraLimb] Attach Point「{id}」がありません", this);
                return false;
            }
            if (definition == current && !transition.IsActive) return true;

            bool hadPose = current != null && rig.IsMounted;
            var previous = rig.CurrentMountPose;
            current = definition;

            if (immediate || !hadPose || transitionSeconds <= 0f)
            {
                transition.Start(0f);
                Apply(1f);
                MountCompleted?.Invoke(current);
                return true;
            }

            // 前の姿勢を、新しい親の骨から見た値として持つ
            fromLocal = previous.ToLocal(registry.Body.GetFrame(current.parentBone));
            transition.Start(transitionSeconds);
            return true;
        }

        /// <summary>このフレームの付け根を置く（VRIK が体を解いた後、腕を解く前に呼ぶ）。1フレームに1回だけ効く。</summary>
        public void UpdateMount()
        {
            if (lastUpdateFrame == Time.frameCount) return;
            lastUpdateFrame = Time.frameCount;
            Step(Time.deltaTime);
        }

        /// <summary>時間を deltaTime だけ進めて付け根を置く（テストでも使う）。</summary>
        internal void Step(float deltaTime)
        {
            if (!initialized || current == null) return;
            bool wasActive = transition.IsActive;
            float progress = wasActive ? transition.Advance(deltaTime) : 1f;
            Apply(progress);
            if (wasActive && !transition.IsActive) MountCompleted?.Invoke(current);
        }

        void Apply(float progress)
        {
            if (!registry.TryComputeMount(current, rig.Side == LimbSide.Left, out var target)) return;
            var pose = target;
            if (progress < 1f)
            {
                var from = fromLocal.ToWorld(registry.Body.GetFrame(current.parentBone));
                pose = MountPose.Lerp(from, target, progress);
            }
            rig.SetMountPose(pose);
        }

        void LateUpdate() => UpdateMount();
    }
}
