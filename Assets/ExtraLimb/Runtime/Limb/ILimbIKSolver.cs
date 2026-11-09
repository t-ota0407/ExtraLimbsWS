using UnityEngine;

namespace ExtraLimb.Limb
{
    /// <summary>
    /// 追加肢の腕（上腕・前腕・手の3つの骨）を、手首がターゲットに来るように曲げるIK。
    /// 実装を差し替えられるように、ExtraLimbRig はこの形だけを見る。
    /// </summary>
    public interface ILimbIKSolver
    {
        /// <summary>骨を割り当てる。今の姿勢を既定の姿勢として覚える。成功したら true。</summary>
        bool Initiate(Transform root, Transform upperArm, Transform forearm, Transform hand);

        /// <summary>
        /// 既定の姿勢から解き直す（前のフレームの結果に依存しない）。
        /// bendGoal は肘を向けたい点（ワールド座標）。weight が0なら既定の姿勢のまま。
        /// </summary>
        void Solve(Vector3 target, Vector3 bendGoal, float weight);
    }
}
