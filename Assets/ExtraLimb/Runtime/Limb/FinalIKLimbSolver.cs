using RootMotion.FinalIK;
using UnityEngine;

namespace ExtraLimb.Limb
{
    /// <summary>
    /// Final IK の IKSolverLimb（余弦定理で解く2関節のIK）を使う実装。コンポーネント（LimbIK）は使わず、
    /// 体（VRIK）を解き終えた直後に Solve() を呼んで、同じフレームのうちに解く。
    /// 手の向きはIKで決めず、前腕の延長のままにする（IKRotationWeight = 0）。
    /// 肘の向きは毎フレーム bendGoal から決める（BendModifierは使わない）。
    /// </summary>
    public sealed class FinalIKLimbSolver : ILimbIKSolver
    {
        readonly IKSolverLimb solver = new IKSolverLimb();

        public bool IsInitiated => solver.initiated;

        public bool Initiate(Transform root, Transform upperArm, Transform forearm, Transform hand)
        {
            solver.IKPositionWeight = 0f;
            solver.IKRotationWeight = 0f;
            solver.bendModifier = IKSolverLimb.BendModifier.Animation;
            solver.bendModifierWeight = 0f;
            solver.maintainRotationWeight = 0f;
            return solver.SetChain(upperArm, forearm, hand, root);
        }

        public void Solve(Vector3 target, Vector3 bendGoal, float weight)
        {
            if (!solver.initiated) return;
            // 毎フレーム既定の姿勢に戻してから解く（ひねりなどが積み重ならないように）
            solver.FixTransforms();
            solver.IKPosition = target;
            solver.IKPositionWeight = Mathf.Clamp01(weight);
            solver.SetBendGoalPosition(bendGoal, 1f);
            solver.Update();
        }
    }
}
