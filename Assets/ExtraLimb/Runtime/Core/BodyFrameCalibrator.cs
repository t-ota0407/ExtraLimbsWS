using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// 体の軸（x=右、y=上、z=前）を、基準（腰トラッカー）の座標で表す。
    /// 腰トラッカーの付け方（向き）は人や日によって違うので、変位 Δp（腰トラッカーの座標）をそのまま使うと
    /// 「足を前に出したのに手先が横へ動く」ことが起きる。これを次の2つで直す。
    /// <list type="bullet">
    /// <item>上：Re-center時の重力の向き。SteamVR（Raw）・Quest・仮想トラッカーの空間はどれも +Y が上。</item>
    /// <item>前：「前を記録」で、入力の部位を前へ出したときの変位の水平成分。記録するまでは、トラッカーの軸で代用する。</item>
    /// </list>
    /// 腰トラッカーは体に固定されているので、一度求めた軸は、体の向きを変えても前かがみになってもそのまま使える。
    /// </summary>
    public class BodyFrameCalibrator
    {
        /// <summary>前を記録していないときに、前として使うトラッカーの軸の候補（順に、水平に近いものを使う）。</summary>
        static readonly Vector3[] DefaultForwardCandidates = { Vector3.forward, Vector3.down, Vector3.right };

        /// <summary>水平成分がこの長さ（単位ベクトルに対して）より短い軸は、前として使わない（鉛直から約17°以内）。</summary>
        const float MinHorizontalRatio = 0.3f;

        Vector3 up = Vector3.up;
        Vector3 recordedForward = Vector3.forward;

        /// <summary>前を記録済みか。false のときはトラッカーの軸で代用している。</summary>
        public bool HasForward { get; private set; }

        /// <summary>体の軸から基準の座標への回転。体の前 (0,0,1) は基準の座標で Rotation*(0,0,1)。</summary>
        public Quaternion Rotation { get; private set; } = Quaternion.identity;

        /// <summary>体の上の向き（基準の座標）。</summary>
        public Vector3 Up => up;

        /// <summary>体の前の向き（基準の座標）。</summary>
        public Vector3 Forward => Rotation * Vector3.forward;

        /// <summary>体の右の向き（基準の座標）。</summary>
        public Vector3 Right => Rotation * Vector3.right;

        /// <summary>
        /// 上の向きを決める。Re-centerのたびに、そのときの基準の向きで呼ぶ。
        /// 記録済みの前の向きは保ち、新しい上に直交するように直す。
        /// </summary>
        public void SetUp(Quaternion referenceRotation, Vector3 worldUp)
        {
            var local = Quaternion.Inverse(referenceRotation) * worldUp;
            if (local.sqrMagnitude < 1e-12f) return;
            up = local.normalized;
            Rebuild();
        }

        /// <summary>
        /// 前の向きを記録する。direction は基準の座標での変位（例：入力の部位を前へ出したときの Δp）。
        /// 水平成分が minHorizontal（m）より短いときは記録せずに false を返す。
        /// </summary>
        public bool TrySetForward(Vector3 direction, float minHorizontal)
        {
            var horizontal = Vector3.ProjectOnPlane(direction, up);
            if (horizontal.magnitude < Mathf.Max(minHorizontal, 1e-6f)) return false;
            recordedForward = horizontal.normalized;
            HasForward = true;
            Rebuild();
            return true;
        }

        /// <summary>記録した前の向きを消して、トラッカーの軸で代用する状態に戻す。</summary>
        public void ClearForward()
        {
            recordedForward = Vector3.forward;
            HasForward = false;
            Rebuild();
        }

        /// <summary>上の向きも含めて初期状態に戻す。</summary>
        public void Reset()
        {
            up = Vector3.up;
            ClearForward();
        }

        /// <summary>基準の座標のベクトルを、体の軸で表す。</summary>
        public Vector3 ToBody(Vector3 referenceLocal) => Quaternion.Inverse(Rotation) * referenceLocal;

        /// <summary>体の軸のベクトルを、基準の座標で表す。</summary>
        public Vector3 FromBody(Vector3 body) => Rotation * body;

        void Rebuild()
        {
            var forward = Vector3.zero;
            if (HasForward)
            {
                forward = Vector3.ProjectOnPlane(recordedForward, up);
                if (forward.magnitude < MinHorizontalRatio)
                {
                    // 上の向きが大きく変わって、記録した前がほぼ鉛直になった（トラッカーを付け直したなど）
                    HasForward = false;
                    forward = Vector3.zero;
                }
            }
            if (forward == Vector3.zero)
            {
                foreach (var candidate in DefaultForwardCandidates)
                {
                    var projected = Vector3.ProjectOnPlane(candidate, up);
                    if (projected.magnitude < MinHorizontalRatio) continue;
                    forward = projected;
                    break;
                }
            }
            Rotation = Quaternion.LookRotation(forward.normalized, up);
        }
    }
}
