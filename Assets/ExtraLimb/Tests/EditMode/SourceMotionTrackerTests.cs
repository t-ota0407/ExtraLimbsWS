using ExtraLimb.Core;
using ExtraLimb.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>振ったトラッカーを見つける（Step 3-2）。</summary>
    public class SourceMotionTrackerTests
    {
        const float Dt = 1f / 90f;

        static MockPoseSource Source(string id) => new MockPoseSource(id, BodySite.Unknown, new Pose(Vector3.zero, Quaternion.identity));

        // a を速さ speedA、b を速さ speedB（m/s）で x 方向に frames フレーム動かす
        static void Run(SourceMotionTracker motion, TrackerRegistry registry, MockPoseSource a, float speedA, MockPoseSource b, float speedB, int frames, ref float time)
        {
            for (int i = 0; i < frames; i++)
            {
                a.Pose = new Pose(a.Pose.position + Vector3.right * speedA * Dt, Quaternion.identity);
                b.Pose = new Pose(b.Pose.position + Vector3.right * speedB * Dt, Quaternion.identity);
                time += Dt;
                motion.Update(registry, Dt, time);
            }
        }

        [Test]
        public void SpeedApproachesActualSpeed()
        {
            var registry = new TrackerRegistry();
            var a = Source("a");
            var b = Source("b");
            registry.Register(a);
            registry.Register(b);
            var motion = new SourceMotionTracker();
            float time = 0f;

            Run(motion, registry, a, 0.5f, b, 0f, 60, ref time);

            Assert.That(motion.GetSpeed("a"), Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(motion.GetSpeed("b"), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(motion.GetSpeed("unknown"), Is.EqualTo(0f));
        }

        [Test]
        public void FastestMovingSourceIsRemembered()
        {
            var registry = new TrackerRegistry();
            var a = Source("a");
            var b = Source("b");
            registry.Register(a);
            registry.Register(b);
            var motion = new SourceMotionTracker { ThresholdMetersPerSecond = 0.15f };
            float time = 0f;

            // 体のわずかな揺れ（5 cm/s）は数えない
            Run(motion, registry, a, 0.05f, b, 0.05f, 60, ref time);
            Assert.That(motion.RecentlyMoved(time, 4f), Is.Null);

            // b を振る（a も少し動く）
            Run(motion, registry, a, 0.2f, b, 0.8f, 30, ref time);
            Assert.That(motion.RecentlyMoved(time, 4f), Is.EqualTo("b"));

            // 止めても、4秒の間は覚えている
            Run(motion, registry, a, 0f, b, 0f, 270, ref time);
            Assert.That(motion.RecentlyMoved(time, 4f), Is.EqualTo("b"));
            Run(motion, registry, a, 0f, b, 0f, 180, ref time);
            Assert.That(motion.RecentlyMoved(time, 4f), Is.Null);
        }

        [Test]
        public void CandidateFilterAndClear()
        {
            var registry = new TrackerRegistry();
            var a = Source("a");
            var b = Source("b");
            registry.Register(a);
            registry.Register(b);
            var motion = new SourceMotionTracker();
            float time = 0f;

            // b だけを候補にすると、a のほうが速くても b
            for (int i = 0; i < 30; i++)
            {
                a.Pose = new Pose(a.Pose.position + Vector3.right * 1f * Dt, Quaternion.identity);
                b.Pose = new Pose(b.Pose.position + Vector3.right * 0.5f * Dt, Quaternion.identity);
                time += Dt;
                motion.Update(registry, Dt, time, source => source.Id == "b");
            }
            Assert.That(motion.RecentlyMoved(time, 4f), Is.EqualTo("b"));

            motion.ClearRecent();
            Assert.That(motion.RecentlyMoved(time, 4f), Is.Null);
        }

        [Test]
        public void LostTracking_ResetsSpeed()
        {
            var registry = new TrackerRegistry();
            var a = Source("a");
            var b = Source("b");
            registry.Register(a);
            registry.Register(b);
            var motion = new SourceMotionTracker();
            float time = 0f;
            Run(motion, registry, a, 0.5f, b, 0f, 30, ref time);

            a.IsTracking = false;
            motion.Update(registry, Dt, time + Dt);

            Assert.That(motion.GetSpeed("a"), Is.EqualTo(0f));
        }
    }
}
