using System;
using UnityEngine;

namespace ExtraLimb.Limb
{
    /// <summary>
    /// 掴める物。コライダーを持つ GameObject（またはその親）に付ける。Rigidbody があれば、掴んでいる間は運動学的にし、放したら元に戻して手の速さを渡す（投げられる）。
    /// 別の手が掴むと持ち替えになる（前の手は放したことになる）。
    /// </summary>
    [DisallowMultipleComponent]
    public class Graspable : MonoBehaviour
    {
        Rigidbody body;
        bool wasKinematic;

        /// <summary>今掴んでいる手。なければ null。</summary>
        public EndEffectorGrasper Holder { get; private set; }

        public bool IsHeld => Holder != null;

        public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

        /// <summary>掴まれたとき（掴んだ手）。</summary>
        public event Action<EndEffectorGrasper> Grabbed;

        /// <summary>放されたとき（放した手）。持ち替えのときは前の手について呼ばれる。</summary>
        public event Action<EndEffectorGrasper> Released;

        internal void OnGrab(EndEffectorGrasper grasper)
        {
            var previous = Holder;
            if (previous == grasper) return;
            if (previous != null)
            {
                // 持ち替え：運動学的なままにして、前の手からは外す
                Holder = null;
                previous.Drop(this);
                Released?.Invoke(previous);
            }
            else if (Body != null)
            {
                wasKinematic = Body.isKinematic;
                Body.isKinematic = true;
            }
            Holder = grasper;
            Grabbed?.Invoke(grasper);
        }

        internal void OnRelease(EndEffectorGrasper grasper, Vector3 velocity, Vector3 angularVelocity)
        {
            if (Holder != grasper) return;
            Holder = null;
            if (Body != null)
            {
                Body.isKinematic = wasKinematic;
                if (!wasKinematic)
                {
                    Body.linearVelocity = velocity;
                    Body.angularVelocity = angularVelocity;
                }
            }
            Released?.Invoke(grasper);
        }
    }
}
