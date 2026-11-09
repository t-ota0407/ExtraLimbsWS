using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>入力の姿勢から手先のターゲットまでの計算全体（Step 2-3）。</summary>
    public class LimbControlPipelineTests
    {
        static readonly Vector3 WaistPosition = new Vector3(0.3f, 0.95f, -0.2f);
        static readonly Vector3 AnkleNeutral = new Vector3(0.15f, -0.85f, 0.25f); // 腰から見た足首（体の軸）
        static readonly Vector3 BasePosition = new Vector3(0.12f, -0.06f, 0.30f);

        /// <summary>体が heading を向いて立ち、腰トラッカーが mounting の向きで付いているときの腰の姿勢。</summary>
        static Pose Waist(Quaternion heading, Quaternion mounting) => new Pose(WaistPosition, heading * mounting);

        /// <summary>足首を体の軸で displacement だけ動かしたときの姿勢。</summary>
        static Pose Ankle(Quaternion heading, Vector3 displacement)
        {
            return new Pose(WaistPosition + heading * (AnkleNeutral + displacement), heading * Quaternion.Euler(10f, 20f, 30f));
        }

        static LimbControlPipeline CreatePipeline(bool filter = false)
        {
            var config = new LimbRuntimeConfig { positionGain = 2f };
            config.filter.enabled = filter;
            var pipeline = new LimbControlPipeline(config);
            pipeline.SetBasePosition(BasePosition);
            return pipeline;
        }

        [Test]
        public void FootForward_MovesTargetForward_WithGain()
        {
            var pipeline = CreatePipeline();
            var heading = Quaternion.identity;
            pipeline.Recenter(Waist(heading, Quaternion.identity), Ankle(heading, Vector3.zero));

            var target = pipeline.Step(Waist(heading, Quaternion.identity), Ankle(heading, new Vector3(0f, 0f, 0.05f)), 1f / 90f);

            TestMath.AreClose(BasePosition + new Vector3(0f, 0f, 0.10f), target);
            TestMath.AreClose(new Vector3(0f, 0f, 0.05f), pipeline.LastDisplacement);
        }

        [Test]
        public void TurningWholeBody_DoesNotMoveTarget()
        {
            var pipeline = CreatePipeline();
            var displacement = new Vector3(0.03f, 0.02f, 0.04f);
            pipeline.Recenter(Waist(Quaternion.identity, Quaternion.identity), Ankle(Quaternion.identity, Vector3.zero));
            var before = pipeline.Step(Waist(Quaternion.identity, Quaternion.identity), Ankle(Quaternion.identity, displacement), 0.01f);

            var turned = Quaternion.AngleAxis(120f, Vector3.up);
            var after = pipeline.Step(Waist(turned, Quaternion.identity), Ankle(turned, displacement), 0.01f);

            TestMath.AreClose(before, after);
        }

        [Test]
        public void RotatedWaistTracker_AfterForwardGesture_MovesInBodyAxes()
        {
            var random = new System.Random(3);
            for (int i = 0; i < 20; i++)
            {
                var pipeline = CreatePipeline();
                var heading = Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.up);
                var mounting = TestMath.RandomRotation(random);
                var waist = Waist(heading, mounting);
                pipeline.Recenter(waist, Ankle(heading, Vector3.zero));
                Assert.That(pipeline.TryRecordForward(waist, Ankle(heading, new Vector3(0f, 0.02f, 0.10f)), 0.05f), Is.True);

                var displacement = TestMath.RandomPosition(random, 0.08f);
                var target = pipeline.Step(waist, Ankle(heading, displacement), 0.01f);

                TestMath.AreClose(BasePosition + 2f * displacement, target, message: $"case {i}");
            }
        }

        [Test]
        public void ForwardGesture_BeforeRecenter_IsRejected()
        {
            var pipeline = CreatePipeline();
            var waist = Waist(Quaternion.identity, Quaternion.identity);

            Assert.That(pipeline.TryRecordForward(waist, Ankle(Quaternion.identity, new Vector3(0f, 0f, 0.1f)), 0.05f), Is.False);
        }

        [Test]
        public void Workspace_ClampsTarget()
        {
            var pipeline = CreatePipeline();
            pipeline.Workspace = new WorkspaceLimiter(Vector3.zero, 0.12f, 0.5f);
            pipeline.SetBasePosition(BasePosition);
            var waist = Waist(Quaternion.identity, Quaternion.identity);
            pipeline.Recenter(waist, Ankle(Quaternion.identity, Vector3.zero));
            Assert.That(pipeline.AtWorkspaceBoundary, Is.False);

            var target = pipeline.Step(waist, Ankle(Quaternion.identity, new Vector3(0f, 0f, 0.4f)), 0.01f);

            Assert.That(target.magnitude, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(pipeline.AtWorkspaceBoundary, Is.True);
        }

        [Test]
        public void Recenter_ReturnsTargetToBase()
        {
            var pipeline = CreatePipeline();
            var waist = Waist(Quaternion.identity, Quaternion.identity);
            var displaced = Ankle(Quaternion.identity, new Vector3(0.05f, 0f, 0.05f));
            pipeline.Recenter(waist, Ankle(Quaternion.identity, Vector3.zero));
            pipeline.Step(waist, displaced, 0.01f);

            pipeline.Recenter(waist, displaced);
            var target = pipeline.Step(waist, displaced, 0.01f);

            TestMath.AreClose(BasePosition, target);
        }

        [Test]
        public void StepBeforeRecenter_RecentersAtCurrentPose()
        {
            var pipeline = CreatePipeline();
            var waist = Waist(Quaternion.identity, Quaternion.identity);

            var target = pipeline.Step(waist, Ankle(Quaternion.identity, new Vector3(0.1f, 0.1f, 0.1f)), 0.01f);

            Assert.That(pipeline.IsRecentered, Is.True);
            TestMath.AreClose(BasePosition, target);
        }

        [Test]
        public void Filter_ConvergesToTarget()
        {
            var pipeline = CreatePipeline(filter: true);
            var waist = Waist(Quaternion.identity, Quaternion.identity);
            var displaced = Ankle(Quaternion.identity, new Vector3(0f, 0.03f, 0.06f));
            var neutral = Ankle(Quaternion.identity, Vector3.zero);
            pipeline.Recenter(waist, neutral);
            pipeline.Step(waist, neutral, 1f / 90f);

            var first = pipeline.Step(waist, displaced, 1f / 90f);
            Vector3 target = first;
            for (int i = 0; i < 180; i++) target = pipeline.Step(waist, displaced, 1f / 90f);

            Assert.That(Vector3.Distance(first, BasePosition + new Vector3(0f, 0.06f, 0.12f)), Is.GreaterThan(1e-3f), "最初のフレームは平滑化で遅れる");
            TestMath.AreClose(BasePosition + new Vector3(0f, 0.06f, 0.12f), target, 1e-3f, "2秒後には追いつく");
        }

        [Test]
        public void Apply_NewMode_ReplacesLawAndKeepsBase()
        {
            var pipeline = CreatePipeline();
            var config = pipeline.Config.Clone();
            config.mode = ControlModeKind.Rate;

            pipeline.Apply(config);

            Assert.That(pipeline.Law.Mode, Is.EqualTo(ControlModeKind.Rate));
            TestMath.AreClose(BasePosition, pipeline.Law.BasePosition);
            TestMath.AreClose(BasePosition, pipeline.Target);
        }
    }
}
