using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>腰トラッカーの付け方によらず、変位を体の軸で表す（Step 2-3）。</summary>
    public class BodyFrameCalibratorTests
    {
        [Test]
        public void UprightTracker_WithoutForward_IsIdentity()
        {
            var frame = new BodyFrameCalibrator();
            frame.SetUp(Quaternion.identity, Vector3.up);

            Assert.That(frame.HasForward, Is.False);
            TestMath.AreClose(Quaternion.identity, frame.Rotation);
            TestMath.AreClose(new Vector3(0.1f, 0.2f, 0.3f), frame.ToBody(new Vector3(0.1f, 0.2f, 0.3f)));
        }

        [Test]
        public void ForwardGesture_RecoversBodyAxes_ForAnyMountingAndHeading()
        {
            // 体は鉛直に立ち、向き yaw を向いている。腰トラッカーは任意の向き mounting で体に付いている
            var random = new System.Random(11);
            for (int i = 0; i < 30; i++)
            {
                var yaw = Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.up);
                var mounting = TestMath.RandomRotation(random);
                var waistRotation = yaw * mounting;
                var inverse = Quaternion.Inverse(waistRotation);

                var frame = new BodyFrameCalibrator();
                frame.SetUp(waistRotation, Vector3.up);
                // 足を前へ12cm、少し持ち上げながら出した（鉛直成分は無視される）
                var gesture = inverse * (yaw * new Vector3(0f, 0.03f, 0.12f));
                Assert.That(frame.TrySetForward(gesture, 0.05f), Is.True, $"case {i}");

                for (int k = 0; k < 5; k++)
                {
                    var bodyDisplacement = TestMath.RandomPosition(random, 0.2f);
                    var trackerLocal = inverse * (yaw * bodyDisplacement);
                    TestMath.AreClose(bodyDisplacement, frame.ToBody(trackerLocal), message: $"case {i}-{k}");
                }
            }
        }

        [Test]
        public void SmallGesture_IsRejected()
        {
            var frame = new BodyFrameCalibrator();
            frame.SetUp(Quaternion.identity, Vector3.up);

            Assert.That(frame.TrySetForward(new Vector3(0.02f, 0f, 0.02f), 0.05f), Is.False);
            Assert.That(frame.TrySetForward(new Vector3(0f, 0.3f, 0f), 0.05f), Is.False, "鉛直だけの動きでは前を決められない");
            Assert.That(frame.HasForward, Is.False);
            TestMath.AreClose(Quaternion.identity, frame.Rotation);
        }

        [Test]
        public void TrackerWithVerticalZ_FallsBackToHorizontalAxis()
        {
            // トラッカーの局所Z軸が真上を向いている付け方
            var waistRotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);
            var frame = new BodyFrameCalibrator();
            frame.SetUp(waistRotation, Vector3.up);

            var rotation = frame.Rotation;
            Assert.That(float.IsNaN(rotation.x) || float.IsNaN(rotation.w), Is.False);
            var localUp = Quaternion.Inverse(waistRotation) * Vector3.up;
            TestMath.AreClose(Vector3.up, frame.ToBody(localUp), message: "上はいつも体の上になる");
            Assert.That(Mathf.Abs(Vector3.Dot(frame.Forward, frame.Up)), Is.LessThan(1e-4f), "前と上は直交する");
        }

        [Test]
        public void SetUpAgain_KeepsRecordedForward()
        {
            var mounting = Quaternion.Euler(20f, 130f, -70f);
            var frame = new BodyFrameCalibrator();
            frame.SetUp(mounting, Vector3.up);
            frame.TrySetForward(Quaternion.Inverse(mounting) * new Vector3(0f, 0f, 0.1f), 0.05f);
            var before = frame.Rotation;

            frame.SetUp(mounting, Vector3.up); // 同じ姿勢でRe-centerし直す

            Assert.That(frame.HasForward, Is.True);
            TestMath.AreClose(before, frame.Rotation);
        }

        [Test]
        public void ForwardFromSides_IsPerpendicularToLeftRightLine_ForAnyHeading()
        {
            // 体は yaw を向いて座っている。左右の膝は体の右・左に 12 cm ずつ（高さは少し違ってもよい）
            var random = new System.Random(5);
            for (int i = 0; i < 20; i++)
            {
                var yaw = Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.up);
                var center = TestMath.RandomPosition(random, 1f);
                var right = center + yaw * new Vector3(0.12f, 0.02f, 0f);
                var left = center + yaw * new Vector3(-0.12f, -0.01f, 0f);

                Assert.That(BodyFrameCalibrator.TryForwardFromSides(right, left, Vector3.up, 0.15f, out var forward, out float separation), Is.True, $"case {i}");
                TestMath.AreClose(yaw * Vector3.forward, forward, 1e-4f, $"case {i}");
                Assert.That(separation, Is.EqualTo(0.24f).Within(1e-4f));

                var frame = new BodyFrameCalibrator();
                frame.SetUp(Quaternion.identity, Vector3.up);
                Assert.That(frame.TrySetForward(forward, 0.5f), Is.True);
                TestMath.AreClose(new Vector3(1f, 0f, 0f), frame.ToBody(yaw * Vector3.right), 1e-4f, "体の右は右");
            }
        }

        [Test]
        public void ForwardFromSides_RejectsCloseOrStackedPoints()
        {
            Assert.That(BodyFrameCalibrator.TryForwardFromSides(new Vector3(0.05f, 0f, 0f), new Vector3(-0.05f, 0f, 0f), Vector3.up, 0.15f, out _, out _),
                Is.False, "左右の間隔が短い");
            Assert.That(BodyFrameCalibrator.TryForwardFromSides(new Vector3(0f, 0.3f, 0f), Vector3.zero, Vector3.up, 0.15f, out _, out _),
                Is.False, "上下に並んでいるだけでは左右が決まらない");
        }

        [Test]
        public void ClearForward_ReturnsToTrackerAxes()
        {
            var frame = new BodyFrameCalibrator();
            frame.SetUp(Quaternion.identity, Vector3.up);
            frame.TrySetForward(new Vector3(0.1f, 0f, 0f), 0.05f);
            TestMath.AreClose(Vector3.right, frame.Forward);

            frame.ClearForward();

            Assert.That(frame.HasForward, Is.False);
            TestMath.AreClose(Vector3.forward, frame.Forward);
        }
    }
}
