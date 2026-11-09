using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>Re-centerと、腰トラッカー基準の変位（Step 1-1）。</summary>
    public class NeutralPoseCalibratorTests
    {
        // 腰と右足首の、それらしい姿勢（ワールド座標、Unityの左手系。y=上、z=前）
        static readonly Pose Waist = new Pose(new Vector3(0.1f, 0.95f, -0.2f), Quaternion.Euler(0f, 20f, 0f));
        static readonly Pose Ankle = new Pose(new Vector3(0.25f, 0.1f, 0.05f), Quaternion.Euler(10f, 35f, -5f));

        [Test]
        public void BeforeRecenter_DisplacementIsZero()
        {
            var calibrator = new NeutralPoseCalibrator();

            var delta = calibrator.Compute(Waist, Ankle);

            Assert.That(calibrator.IsCalibrated, Is.False);
            TestMath.AreClose(Vector3.zero, delta.position);
            TestMath.AreClose(Quaternion.identity, delta.rotation);
        }

        [Test]
        public void RightAfterRecenter_DisplacementIsZero()
        {
            var random = new System.Random(1);
            for (int i = 0; i < 100; i++)
            {
                var waist = new Pose(TestMath.RandomPosition(random, 2f), TestMath.RandomRotation(random));
                var input = new Pose(TestMath.RandomPosition(random, 2f), TestMath.RandomRotation(random));
                var calibrator = new NeutralPoseCalibrator();

                calibrator.Recenter(waist, input);
                var delta = calibrator.Compute(waist, input);

                Assert.That(calibrator.IsCalibrated, Is.True);
                TestMath.AreClose(Vector3.zero, delta.position, message: $"case {i}");
                TestMath.AreClose(Quaternion.identity, delta.rotation, message: $"case {i}");
            }
        }

        [Test]
        public void TurningWholeBody_GivesNoDisplacement()
        {
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(Waist, Ankle);

            // 体ごと向きを変えて歩いた状態（腰と足首に同じワールドの回転・移動をかける）
            var random = new System.Random(2);
            for (int i = 0; i < 100; i++)
            {
                var rotation = TestMath.RandomRotation(random);
                var translation = TestMath.RandomPosition(random, 3f);
                var waist = TestMath.TransformWorld(Waist, rotation, translation);
                var ankle = TestMath.TransformWorld(Ankle, rotation, translation);

                var delta = calibrator.Compute(waist, ankle);

                TestMath.AreClose(Vector3.zero, delta.position, message: $"case {i}");
                TestMath.AreClose(Quaternion.identity, delta.rotation, message: $"case {i}");
            }
        }

        [Test]
        public void YawOnly_TurningInPlace_GivesNoDisplacement()
        {
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(Waist, Ankle);

            // 椅子に座ったまま、その場で90度向きを変えた状態（腰の位置を軸に回る）
            var turn = Quaternion.Euler(0f, 90f, 0f);
            var waist = new Pose(Waist.position, turn * Waist.rotation);
            var ankle = new Pose(Waist.position + turn * (Ankle.position - Waist.position), turn * Ankle.rotation);

            var delta = calibrator.Compute(waist, ankle);

            TestMath.AreClose(Vector3.zero, delta.position);
            TestMath.AreClose(Quaternion.identity, delta.rotation);
        }

        [Test]
        public void MovingInputForward_GivesForwardDisplacementInWaistFrame()
        {
            // 腰がワールドの+X（右）を向いているとき、足首をワールドの+Xへ10cm出すと、腰から見て前（+Z）に10cm
            var waist = new Pose(new Vector3(0f, 1f, 0f), Quaternion.Euler(0f, 90f, 0f));
            var ankle = new Pose(new Vector3(0.1f, 0.1f, 0.2f), Quaternion.identity);
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(waist, ankle);

            var moved = new Pose(ankle.position + new Vector3(0.1f, 0f, 0f), ankle.rotation);
            var delta = calibrator.Compute(waist, moved);

            TestMath.AreClose(new Vector3(0f, 0f, 0.1f), delta.position);
            TestMath.AreClose(Quaternion.identity, delta.rotation);
        }

        [Test]
        public void MovingInputThenTurningBody_KeepsSameDisplacement()
        {
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(Waist, Ankle);

            // 腰から見て右に5cm・前に10cm動かした足首
            var localOffset = new Vector3(0.05f, 0f, 0.1f);
            var movedAnkle = new Pose(Ankle.position + Waist.rotation * localOffset, Ankle.rotation);
            var expected = calibrator.Compute(Waist, movedAnkle).position;
            TestMath.AreClose(localOffset, expected);

            // その姿勢のまま体ごと向きを変えても、変位は変わらない
            var rotation = Quaternion.Euler(5f, -120f, 3f);
            var translation = new Vector3(1f, 0f, -2f);
            var delta = calibrator.Compute(
                TestMath.TransformWorld(Waist, rotation, translation),
                TestMath.TransformWorld(movedAnkle, rotation, translation));

            TestMath.AreClose(localOffset, delta.position);
        }

        [Test]
        public void TwistingInput_GivesRotationAboutItsLocalAxis()
        {
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(Waist, Ankle);

            // 足首のトラッカーを、その局所Z軸まわりに30度ひねる
            var twist = Quaternion.AngleAxis(30f, Vector3.forward);
            var twisted = new Pose(Ankle.position, Ankle.rotation * twist);

            var delta = calibrator.Compute(Waist, twisted);

            TestMath.AreClose(Vector3.zero, delta.position);
            TestMath.AreClose(twist, delta.rotation);
        }

        [Test]
        public void RecenterAgain_UsesNewNeutralPose()
        {
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(Waist, Ankle);

            var moved = new Pose(Ankle.position + new Vector3(0f, 0.05f, 0f), Ankle.rotation);
            Assert.That(calibrator.Compute(Waist, moved).position.magnitude, Is.GreaterThan(0.04f));

            calibrator.Recenter(Waist, moved);

            TestMath.AreClose(Vector3.zero, calibrator.Compute(Waist, moved).position);
        }

        [Test]
        public void Clear_ReturnsToUncalibrated()
        {
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(Waist, Ankle);

            calibrator.Clear();

            Assert.That(calibrator.IsCalibrated, Is.False);
            var moved = new Pose(Ankle.position + Vector3.up, Ankle.rotation);
            TestMath.AreClose(Vector3.zero, calibrator.Compute(Waist, moved).position);
        }
    }
}
