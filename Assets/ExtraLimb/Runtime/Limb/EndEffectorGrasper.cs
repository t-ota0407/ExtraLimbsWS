using System;
using UnityEngine;

namespace ExtraLimb.Limb
{
    /// <summary>
    /// 手先（追加肢の手、本来の手）で物を掴む・放す。Pressed を押した瞬間に、半径の中でいちばん近い Graspable を掴み、
    /// Pressed を離すと放す。掴んでいる間は、掴んだときの手との位置関係を保って物を動かす（物を手の子にはしない。アバターの拡縮を物に移さないため）。
    /// 手の位置は PoseSource（なければこの Transform）から毎フレーム読む。追加肢を解いた後に動くよう、実行順を LimbSession（10010）より後にしてある。
    /// どの入力で Pressed にするかは外（GraspRig）が決める。
    /// </summary>
    [DefaultExecutionOrder(10015)]
    public class EndEffectorGrasper : MonoBehaviour
    {
        [Tooltip("この半径（m）の中にある物を掴める")]
        [SerializeField] float radius = 0.07f;
        [SerializeField] LayerMask layers = ~0;
        [Tooltip("記録や表示に使う名前（例：extra_limb_right、hand_left）")]
        [SerializeField] string effectorName = "";

        static readonly Collider[] Hits = new Collider[32];

        Graspable held;
        Vector3 heldLocalPosition;
        Quaternion heldLocalRotation = Quaternion.identity;
        Vector3 lastPosition, velocity, angularVelocity;
        Quaternion lastRotation = Quaternion.identity;
        bool hasLast, wasPressed;

        /// <summary>掴む操作をしているか（トリガーやグリップを押しているか）。</summary>
        public bool Pressed { get; set; }

        /// <summary>今掴んでいる物。</summary>
        public Graspable Held => held;

        /// <summary>掴んでいないときに、半径の中でいちばん近い物（掴める候補）。</summary>
        public Graspable Hovered { get; private set; }

        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(0.001f, value);
        }

        public string EffectorName
        {
            get => string.IsNullOrEmpty(effectorName) ? name : effectorName;
            set => effectorName = value;
        }

        /// <summary>手の位置と向き（掴む点）。null ならこの Transform。</summary>
        public Func<Pose> PoseSource { get; set; }

        /// <summary>手の速さの推定（m/s）。放したときに物へ渡す。</summary>
        public Vector3 Velocity => velocity;

        public event Action<Graspable> Grabbed;
        public event Action<Graspable> Released;

        void LateUpdate() => Step(Time.deltaTime);

        void OnDisable()
        {
            if (held != null) Release();
        }

        /// <summary>1フレーム進める：手の位置を読み、押した瞬間に掴み、離したら放し、掴んでいる物を動かす。</summary>
        public void Step(float deltaTime)
        {
            var pose = PoseSource != null ? PoseSource() : new Pose(transform.position, transform.rotation);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            UpdateVelocity(pose, deltaTime);

            if (held != null && held.Holder != this) held = null;
            Hovered = held == null ? FindNearest(pose.position) : null;

            if (Pressed && !wasPressed && held == null && Hovered != null) Grab(Hovered, pose);
            else if (!Pressed && wasPressed && held != null) Release();
            wasPressed = Pressed;

            if (held != null)
                held.transform.SetPositionAndRotation(pose.position + pose.rotation * heldLocalPosition, pose.rotation * heldLocalRotation);
        }

        /// <summary>今掴んでいる物を放す。</summary>
        public void Release()
        {
            var target = held;
            if (target == null) return;
            held = null;
            target.OnRelease(this, velocity, angularVelocity);
            Released?.Invoke(target);
        }

        // 別の手に持ち替えられたとき（Graspable から呼ばれる）
        internal void Drop(Graspable target)
        {
            if (held != target) return;
            held = null;
            Released?.Invoke(target);
        }

        void Grab(Graspable target, Pose pose)
        {
            var inverse = Quaternion.Inverse(pose.rotation);
            heldLocalPosition = inverse * (target.transform.position - pose.position);
            heldLocalRotation = inverse * target.transform.rotation;
            held = target;
            target.OnGrab(this);
            Grabbed?.Invoke(target);
        }

        Graspable FindNearest(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, radius, Hits, layers, QueryTriggerInteraction.Collide);
            Graspable nearest = null;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var graspable = Hits[i].GetComponentInParent<Graspable>();
                if (graspable == null || !graspable.isActiveAndEnabled) continue;
                float distance = Vector3.Distance(ClosestPoint(Hits[i], point), point);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = graspable;
            }
            return nearest;
        }

        static Vector3 ClosestPoint(Collider collider, Vector3 point)
        {
            // メッシュ（凸でないもの）は ClosestPoint が使えないので、外接箱で近似する
            if (collider is MeshCollider mesh && !mesh.convex) return collider.bounds.ClosestPoint(point);
            return collider.ClosestPoint(point);
        }

        void UpdateVelocity(Pose pose, float deltaTime)
        {
            if (hasLast && deltaTime > 1e-5f)
            {
                var v = (pose.position - lastPosition) / deltaTime;
                velocity = Vector3.Lerp(velocity, v, 0.5f);
                (pose.rotation * Quaternion.Inverse(lastRotation)).ToAngleAxis(out float angle, out var axis);
                if (angle > 180f) angle -= 360f;
                var w = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad / deltaTime) : Vector3.zero;
                angularVelocity = Vector3.Lerp(angularVelocity, w, 0.5f);
            }
            lastPosition = pose.position;
            lastRotation = pose.rotation;
            hasLast = true;
        }
    }
}
