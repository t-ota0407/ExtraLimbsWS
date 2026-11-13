using ExtraLimb.Limb;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>追加肢の骨の検出・根元の座標・IK（Step 2-3）。骨の配置は肩の上版モデルと同じ（腕は伸ばして斜め上へ）。</summary>
    public class ExtraLimbRigTests
    {
        GameObject root;
        Transform spine2, upperArm, forearm, hand;

        ExtraLimbRig CreateRig(LimbSide side, bool withFingerBones = false)
        {
            float s = side == LimbSide.Right ? 1f : -1f;
            string name = side == LimbSide.Right ? "Right" : "Left";
            root = new GameObject("TestAvatar");
            spine2 = Bone("mixamorig:Spine2", root.transform, new Vector3(0f, 1.347f, -0.043f));
            var clavicle = Bone($"sub_{name}Shoulder", spine2, new Vector3(s * 0.046f, 1.349f, -0.032f));
            upperArm = Bone($"sub_{name}Arm", clavicle, new Vector3(s * 0.149f, 1.530f, -0.046f));
            forearm = Bone($"sub_{name}ForeArm", upperArm, new Vector3(s * 0.382f, 1.639f, -0.140f));
            hand = Bone($"sub_{name}Hand", forearm, new Vector3(s * 0.617f, 1.749f, -0.234f));
            var direction = (hand.position - forearm.position).normalized;
            if (withFingerBones)
            {
                // 実際のモデルと同じく、見た目の指先は HandMiddle4 の根元、HandMiddle4_end はその約4cm先
                var middle3 = Bone($"sub_{name}HandMiddle3", hand, hand.position + direction * 0.11f);
                var middle4 = Bone($"sub_{name}HandMiddle4", middle3, hand.position + direction * 0.14f);
                Bone($"sub_{name}HandMiddle4_end", middle4, hand.position + direction * 0.18f);
            }
            else
            {
                Bone($"sub_{name}HandMiddle4_end", hand, hand.position + direction * 0.18f);
            }

            var rig = root.AddComponent<ExtraLimbRig>();
            rig.Configure(root.transform, side);
            Assert.That(rig.Initialize(), Is.True);
            return rig;
        }

        static Transform Bone(string name, Transform parent, Vector3 worldPosition)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.position = worldPosition;
            return bone;
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void Initialize_FindsBonesAndArmLength()
        {
            var rig = CreateRig(LimbSide.Right);

            Assert.That(rig.UpperArm, Is.SameAs(upperArm));
            Assert.That(rig.Hand, Is.SameAs(hand));
            Assert.That(rig.Reach, Is.EqualTo(0.55f).Within(0.005f));
            TestMath.AreClose(upperArm.position, rig.Frame.position);
            TestMath.AreClose(Quaternion.identity, rig.Frame.rotation, message: "Tポーズでは体の向きとそろう");
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(-0.15f, 0f, 0.05f)]
        [TestCase(0f, 0.13f, 0.05f)]
        [TestCase(0.15f, 0f, 0.05f)]
        [TestCase(0f, 0f, 0.16f)]
        [TestCase(0.05f, -0.2f, -0.1f)]
        public void Solve_PutsWristOnReachableTarget(float x, float y, float z)
        {
            var rig = CreateRig(LimbSide.Right);
            var local = rig.RestHandPosition + new Vector3(x, y, z);
            Assert.That(rig.CreateWorkspace().Contains(local), Is.True, "テストの点は届く範囲の中");

            rig.SolveLocal(local);

            TestMath.AreClose(rig.FrameToWorld(local), hand.position, 1e-3f);
        }

        [Test]
        public void Solve_BendsElbowTowardHint()
        {
            var rig = CreateRig(LimbSide.Right);
            var target = rig.FrameToWorld(rig.RestHandPosition);

            rig.Solve(target);

            var shoulder = upperArm.position;
            var line = (target - shoulder).normalized;
            var elbowOffset = Vector3.ProjectOnPlane(forearm.position - shoulder, line);
            var hintOffset = Vector3.ProjectOnPlane(rig.FrameToWorld(rig.BendHint) - shoulder, line);
            Assert.That(elbowOffset.magnitude, Is.GreaterThan(0.05f), "肘が曲がっている");
            Assert.That(Vector3.Dot(elbowOffset.normalized, hintOffset.normalized), Is.GreaterThan(0.99f), "肘はヒントの側");
        }

        [Test]
        public void Solve_UnreachableTarget_ExtendsTowardIt()
        {
            var rig = CreateRig(LimbSide.Right);
            var direction = new Vector3(0.3f, -0.2f, 1f).normalized;
            var target = rig.FrameToWorld(direction * 1.0f);

            rig.Solve(target);

            var reach = hand.position - upperArm.position;
            Assert.That(reach.magnitude, Is.EqualTo(rig.Reach).Within(2e-3f), "伸び切る");
            Assert.That(Vector3.Angle(reach, target - upperArm.position), Is.LessThan(0.5f), "ターゲットの方向へ");
        }

        [Test]
        public void Solve_IsRepeatable()
        {
            var rig = CreateRig(LimbSide.Right);
            var a = rig.RestHandPosition;
            var b = rig.RestHandPosition + new Vector3(-0.15f, 0.1f, 0.05f);

            rig.SolveLocal(a);
            var elbowFirst = forearm.position;
            var handRotationFirst = hand.rotation;
            for (int i = 0; i < 20; i++)
            {
                rig.SolveLocal(b);
                rig.SolveLocal(a);
            }

            TestMath.AreClose(elbowFirst, forearm.position);
            TestMath.AreClose(handRotationFirst, hand.rotation, 0.1f, "ひねりが積み重ならない");
        }

        [Test]
        public void Frame_FollowsAttachBone()
        {
            var rig = CreateRig(LimbSide.Right);
            var local = new Vector3(0.1f, 0.05f, 0.3f);

            var turn = Quaternion.AngleAxis(30f, Vector3.up);
            spine2.rotation = turn * spine2.rotation;

            TestMath.AreClose(turn, rig.Frame.rotation);
            TestMath.AreClose(upperArm.position + turn * local, rig.FrameToWorld(local));
            TestMath.AreClose(local, rig.WorldToFrame(rig.FrameToWorld(local)));
        }

        [Test]
        public void LeftLimb_MirrorsRestPositionAndReaches()
        {
            var rig = CreateRig(LimbSide.Left);

            Assert.That(rig.RestHandPosition.x, Is.LessThan(0f), "左の肢では外向き＝-x");
            Assert.That(rig.BendHint.x, Is.LessThan(0f));
            rig.SolveLocal(rig.RestHandPosition);
            TestMath.AreClose(rig.FrameToWorld(rig.RestHandPosition), hand.position, 1e-3f);
        }

        [Test]
        public void Visible_False_ShrinksFromClavicle_AndRestores()
        {
            var rig = CreateRig(LimbSide.Right);
            var clavicle = upperArm.parent;

            rig.Visible = false;
            Assert.That(clavicle.localScale.x, Is.LessThan(1e-3f));

            rig.Visible = true;
            TestMath.AreClose(Vector3.one, clavicle.localScale);
        }

        [Test]
        public void ContactPoint_IsAtFingertip()
        {
            var rig = CreateRig(LimbSide.Right);
            rig.SolveLocal(rig.RestHandPosition);

            var forearmDirection = (hand.position - forearm.position).normalized;
            TestMath.AreClose(hand.position + forearmDirection * 0.18f, rig.ContactPoint, 1e-3f, "手の向きは前腕の延長");
        }

        [TestCase(LimbSide.Right)]
        [TestCase(LimbSide.Left)]
        public void ContactPoint_IsMiddle4Root_NotBoneEnd(LimbSide side)
        {
            var rig = CreateRig(side, withFingerBones: true);
            rig.SolveLocal(rig.RestHandPosition);

            var forearmDirection = (hand.position - forearm.position).normalized;
            TestMath.AreClose(hand.position + forearmDirection * 0.14f, rig.ContactPoint, 1e-3f, "見た目の指先（HandMiddle4 の根元）");
            TestMath.AreClose(forearmDirection, rig.ContactDirection, 1e-3f, "指の付け根から指先への向き");
        }
    }
}
