using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>One Euro Filter（Step 1-1）。90 Hz で値を入れる。</summary>
    public class PoseFilterTests
    {
        const float Dt = 1f / 90f;

        [Test]
        public void FirstSample_PassesThrough()
        {
            var filter = new OneEuroFilterVector3(1f, 0f);

            TestMath.AreClose(new Vector3(1f, 2f, 3f), filter.Filter(new Vector3(1f, 2f, 3f), Dt));
        }

        [Test]
        public void ConstantInput_IsUnchanged()
        {
            var filter = new OneEuroFilterVector3(1f, 5f);
            var value = new Vector3(0.1f, -0.2f, 0.3f);

            for (int i = 0; i < 200; i++)
                TestMath.AreClose(value, filter.Filter(value, Dt));
        }

        [Test]
        public void StepInput_IsSmoothedThenConverges()
        {
            var filter = new OneEuroFilterVector3(1f, 0f);
            filter.Filter(Vector3.zero, Dt);

            var first = filter.Filter(Vector3.right, Dt);
            Assert.That(first.x, Is.GreaterThan(0f).And.LessThan(0.5f), "1サンプル目で一気に飛ばない");

            var output = first;
            for (int i = 0; i < 180; i++) output = filter.Filter(Vector3.right, Dt); // 2秒

            Assert.That(output.x, Is.GreaterThan(0.99f), "2秒後にはほぼ追いつく");
        }

        [Test]
        public void HigherBeta_FollowsFastMotionWithLessLag()
        {
            float LagAfterRamp(float beta)
            {
                var filter = new OneEuroFilterVector3(1f, beta);
                var value = Vector3.zero;
                var output = filter.Filter(value, Dt);
                for (int i = 0; i < 45; i++) // 0.5秒、1 m/s で動かす
                {
                    value += Vector3.forward * Dt;
                    output = filter.Filter(value, Dt);
                }
                return value.z - output.z;
            }

            float lagWithoutBeta = LagAfterRamp(0f);
            float lagWithBeta = LagAfterRamp(10f);

            Assert.That(lagWithBeta, Is.LessThan(lagWithoutBeta * 0.5f));
        }

        [Test]
        public void Jitter_IsReduced()
        {
            // ±1mm の細かい揺れ
            var filter = new OneEuroFilterVector3(1.5f, 5f);
            float maxDeviation = 0f;
            for (int i = 0; i < 360; i++)
            {
                var noisy = new Vector3(0f, (i % 2 == 0 ? 1f : -1f) * 0.001f, 0f);
                var output = filter.Filter(noisy, Dt);
                if (i > 90) maxDeviation = Mathf.Max(maxDeviation, Mathf.Abs(output.y));
            }

            Assert.That(maxDeviation, Is.LessThan(0.0005f));
        }

        [Test]
        public void ZeroDt_ReturnsPreviousValue()
        {
            var filter = new OneEuroFilterVector3(1f, 0f);
            filter.Filter(Vector3.zero, Dt);

            TestMath.AreClose(Vector3.zero, filter.Filter(Vector3.one, 0f));
        }

        [Test]
        public void Reset_NextSamplePassesThrough()
        {
            var filter = new OneEuroFilterVector3(1f, 0f);
            filter.Filter(Vector3.zero, Dt);

            filter.Reset();

            TestMath.AreClose(Vector3.one, filter.Filter(Vector3.one, Dt));
        }

        [Test]
        public void RotationStep_IsSmoothedThenConverges()
        {
            var filter = new OneEuroFilterQuaternion(1f, 0f);
            filter.Filter(Quaternion.identity, Dt);
            var target = Quaternion.Euler(0f, 90f, 0f);

            var first = filter.Filter(target, Dt);
            float firstAngle = Quaternion.Angle(Quaternion.identity, first);
            Assert.That(firstAngle, Is.GreaterThan(0f).And.LessThan(45f));

            var output = first;
            for (int i = 0; i < 180; i++) output = filter.Filter(target, Dt);

            Assert.That(TestMath.AngleBetween(target, output), Is.LessThan(1f));
        }

        [Test]
        public void RotationSignFlip_DoesNotSpin()
        {
            // q と -q は同じ回転。符号が反転しても出力は動かない
            var filter = new OneEuroFilterQuaternion(1f, 0f);
            var q = Quaternion.Euler(20f, 30f, 40f);
            filter.Filter(q, Dt);

            var flipped = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            for (int i = 0; i < 10; i++)
                TestMath.AreClose(q, filter.Filter(flipped, Dt));
        }

        [Test]
        public void PoseFilter_Disabled_PassesThrough()
        {
            var filter = new PoseFilter(new PoseFilterSettings { enabled = false });
            filter.Filter(new Pose(Vector3.zero, Quaternion.identity), Dt);

            var pose = new Pose(Vector3.one, Quaternion.Euler(0f, 90f, 0f));
            var output = filter.Filter(pose, Dt);

            TestMath.AreClose(pose.position, output.position);
            TestMath.AreClose(pose.rotation, output.rotation);
        }

        [Test]
        public void PoseFilter_SmoothsDisplacement()
        {
            var filter = new PoseFilter(new PoseFilterSettings { minCutoff = 1f, beta = 0f });
            filter.Filter(PoseDelta.Zero, Dt);

            var step = new PoseDelta(new Vector3(0f, 0f, 0.1f), Quaternion.Euler(0f, 0f, 30f));
            var first = filter.Filter(step, Dt);

            Assert.That(first.position.z, Is.GreaterThan(0f).And.LessThan(0.05f));
            Assert.That(Quaternion.Angle(Quaternion.identity, first.rotation), Is.GreaterThan(0f).And.LessThan(15f));
        }
    }
}
