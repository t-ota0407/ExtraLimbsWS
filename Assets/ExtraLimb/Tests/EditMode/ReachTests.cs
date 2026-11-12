using System.Collections.Generic;
using System.Linq;
using ExtraLimb.Experiment;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>リーチング課題（Step 5-1）：ターゲットの円、触れたかの判定、ターゲットの順の乱数。</summary>
    public class ReachTests
    {
        const float Tol = 1e-4f;

        static ReachRing MakeRing(Pose chest, float height = 1.7f, float shoulder = 0.4f, int count = 8) =>
            ReachRing.Create(chest, height, shoulder, 0.2f, 0.1f, 0.25f, 0.25f, count);

        // ---- 円 ----

        [Test]
        public void Ring_CenterIsChestPlusRatiosOfBody()
        {
            var ring = MakeRing(new Pose(new Vector3(0f, 1.3f, 0f), Quaternion.identity));
            // 前 1.7×0.2、上 1.7×0.1、右 0.4×0.25、半径 1.7×0.25
            Assert.That(Vector3.Distance(ring.center, new Vector3(0.1f, 1.3f + 0.17f, 0.34f)), Is.LessThan(Tol));
            Assert.That(ring.radius, Is.EqualTo(0.425f).Within(Tol));
        }

        [Test]
        public void Ring_TargetsOnCircleFacingParticipant()
        {
            var ring = MakeRing(new Pose(new Vector3(0.3f, 1.2f, -0.5f), Quaternion.Euler(0f, 30f, 0f)));
            Assert.That(ring.Count, Is.EqualTo(8));
            foreach (var target in ring.targets)
            {
                Assert.That(Vector3.Distance(target, ring.center), Is.EqualTo(ring.radius).Within(Tol));
                // 円の面は体の前に垂直
                Assert.That(Vector3.Dot(target - ring.center, ring.forward), Is.EqualTo(0f).Within(Tol));
            }
        }

        [Test]
        public void Ring_FirstTargetIsTopThenClockwiseAsParticipantSees()
        {
            var ring = MakeRing(new Pose(Vector3.zero, Quaternion.identity));
            // 体の向きが +z なので、参加者の右は +x
            Assert.That(Vector3.Distance(ring.targets[0], ring.center + Vector3.up * ring.radius), Is.LessThan(Tol), "1つ目は真上");
            Assert.That(Vector3.Distance(ring.targets[2], ring.center + Vector3.right * ring.radius), Is.LessThan(Tol), "3つ目は右");
            Assert.That(Vector3.Distance(ring.targets[4], ring.center + Vector3.down * ring.radius), Is.LessThan(Tol), "5つ目は真下");
            Assert.That(Vector3.Distance(ring.targets[6], ring.center + Vector3.left * ring.radius), Is.LessThan(Tol), "7つ目は左");
            Assert.That(ring.targets[1].x, Is.GreaterThan(ring.center.x));
            Assert.That(ring.targets[1].y, Is.GreaterThan(ring.center.y));
        }

        [Test]
        public void Ring_FollowsBodyYaw()
        {
            // 体が -x を向いているとき、参加者の右は +z
            var ring = MakeRing(new Pose(Vector3.zero, Quaternion.Euler(0f, -90f, 0f)));
            Assert.That(Vector3.Distance(ring.forward, Vector3.left), Is.LessThan(Tol));
            Assert.That(Vector3.Distance(ring.right, Vector3.forward), Is.LessThan(Tol));
            Assert.That(ring.center.x, Is.EqualTo(-0.34f).Within(Tol));
            Assert.That(ring.center.z, Is.EqualTo(0.1f).Within(Tol));
        }

        [Test]
        public void Ring_StaysUprightWhenChestLeans()
        {
            // 胸が前に傾いていても、円は鉛直に立てる（前は水平にする）
            var upright = MakeRing(new Pose(Vector3.zero, Quaternion.Euler(0f, 20f, 0f)));
            var leaning = MakeRing(new Pose(Vector3.zero, Quaternion.Euler(25f, 20f, 0f)));
            Assert.That(Vector3.Distance(upright.center, leaning.center), Is.LessThan(Tol));
            Assert.That(leaning.up, Is.EqualTo(Vector3.up));
            Assert.That(leaning.forward.y, Is.EqualTo(0f).Within(Tol));
        }

        [Test]
        public void Ring_ScalesWithBody()
        {
            var small = MakeRing(new Pose(Vector3.zero, Quaternion.identity), 1.5f, 0.35f);
            var large = MakeRing(new Pose(Vector3.zero, Quaternion.identity), 1.9f, 0.45f);
            // 半径は身長に比例し、肩幅にはよらない
            Assert.That(small.radius, Is.EqualTo(0.375f).Within(Tol));
            Assert.That(large.radius, Is.EqualTo(0.475f).Within(Tol));
            var wideShoulders = MakeRing(new Pose(Vector3.zero, Quaternion.identity), 1.5f, 0.45f);
            Assert.That(wideShoulders.radius, Is.EqualTo(small.radius).Within(Tol));
            Assert.That(large.center.z, Is.GreaterThan(small.center.z));
        }

        [Test]
        public void Ring_PositionNames()
        {
            var names = Enumerable.Range(0, 8).Select(k => ReachRing.PositionName(k, 8)).ToArray();
            CollectionAssert.AreEqual(new[] { "上", "右上", "右", "右下", "下", "左下", "左", "左上" }, names);
            Assert.That(ReachRing.PositionName(3, 6), Is.EqualTo("4"));
        }

        [Test]
        public void Layout_ComputeUsesAssetRatios()
        {
            var layout = ScriptableObject.CreateInstance<TaskLayout>();
            try
            {
                layout.forwardRatio = 0.2f;
                layout.upRatio = 0.1f;
                layout.rightRatio = 0.25f;
                layout.radiusRatio = 0.25f;
                layout.targetCount = 6;
                var chest = new Pose(new Vector3(0f, 1.3f, 0f), Quaternion.identity);
                var ring = layout.Compute(chest, 1.7f, 0.4f);
                var expected = MakeRing(chest, 1.7f, 0.4f, 6);
                Assert.That(ring.Count, Is.EqualTo(6));
                Assert.That(Vector3.Distance(ring.center, expected.center), Is.LessThan(Tol));
                Assert.That(ring.radius, Is.EqualTo(expected.radius).Within(Tol));
            }
            finally
            {
                Object.DestroyImmediate(layout);
            }
        }

        // ---- 触れたかの判定 ----

        static readonly Vector3 Target = new Vector3(0f, 1.4f, 0.4f);
        const float TargetRadius = 0.03f;

        static EffectorProbe Probe(EffectorKind kind, Vector3 point, float radius = 0.015f, bool tracked = true) =>
            new EffectorProbe(kind, tracked, point, radius);

        static Vector3 Far => Target + Vector3.right * 0.3f;

        [Test]
        public void Judge_InsideWhenWithinTargetPlusProbeRadius()
        {
            Assert.That(ReachJudge.IsInside(Probe(EffectorKind.ExtraRight, Target + Vector3.right * 0.044f), Target, TargetRadius), Is.True);
            Assert.That(ReachJudge.IsInside(Probe(EffectorKind.ExtraRight, Target + Vector3.right * 0.046f), Target, TargetRadius), Is.False);
            Assert.That(ReachJudge.IsInside(Probe(EffectorKind.ExtraRight, Target, tracked: false), Target, TargetRadius), Is.False, "追跡していない効果器は触れない");
        }

        [Test]
        public void Judge_CountsExtraLimbEntry()
        {
            var judge = new ReachJudge();
            judge.Reset(Target, TargetRadius, new[] { Probe(EffectorKind.ExtraRight, Far) });
            Assert.That(judge.Update(Target, TargetRadius, new[] { Probe(EffectorKind.ExtraRight, Far) }, false, out _), Is.EqualTo(EffectorKind.None));
            Assert.That(judge.Update(Target, TargetRadius, new[] { Probe(EffectorKind.ExtraRight, Target) }, false, out int ignored), Is.EqualTo(EffectorKind.ExtraRight));
            Assert.That(ignored, Is.EqualTo(0));
            Assert.That(judge.Update(Target, TargetRadius, new[] { Probe(EffectorKind.ExtraRight, Target) }, false, out _), Is.EqualTo(EffectorKind.None), "入ったままは数えない");
        }

        [Test]
        public void Judge_InsideAtLightingMustLeaveAndReenter()
        {
            var judge = new ReachJudge();
            judge.Reset(Target, TargetRadius, new[] { Probe(EffectorKind.ExtraRight, Target) });
            Assert.That(judge.Update(Target, TargetRadius, new[] { Probe(EffectorKind.ExtraRight, Target) }, true, out _), Is.EqualTo(EffectorKind.None));
            Assert.That(judge.Update(Target, TargetRadius, new[] { Probe(EffectorKind.ExtraRight, Far) }, true, out _), Is.EqualTo(EffectorKind.None));
            Assert.That(judge.Update(Target, TargetRadius, new[] { Probe(EffectorKind.ExtraRight, Target) }, true, out _), Is.EqualTo(EffectorKind.ExtraRight));
        }

        [Test]
        public void Judge_FirstSegmentIgnoresNaturalHandsButCountsThem()
        {
            var judge = new ReachJudge();
            var away = new[] { Probe(EffectorKind.ExtraRight, Far), Probe(EffectorKind.HandRight, Far, 0.04f) };
            var handIn = new[] { Probe(EffectorKind.ExtraRight, Far), Probe(EffectorKind.HandRight, Target, 0.04f) };
            judge.Reset(Target, TargetRadius, away);
            Assert.That(judge.Update(Target, TargetRadius, handIn, false, out int ignored), Is.EqualTo(EffectorKind.None));
            Assert.That(ignored, Is.EqualTo(1));
            Assert.That(judge.Update(Target, TargetRadius, handIn, false, out ignored), Is.EqualTo(EffectorKind.None));
            Assert.That(ignored, Is.EqualTo(0), "入ったままは1回");
            judge.Update(Target, TargetRadius, away, false, out _);
            judge.Update(Target, TargetRadius, handIn, false, out ignored);
            Assert.That(ignored, Is.EqualTo(1), "入り直すともう1回");
        }

        [Test]
        public void Judge_SecondSegmentCountsNaturalHands()
        {
            var judge = new ReachJudge();
            judge.Reset(Target, TargetRadius, new[] { Probe(EffectorKind.HandLeft, Far, 0.04f) });
            Assert.That(judge.Update(Target, TargetRadius, new[] { Probe(EffectorKind.HandLeft, Target, 0.04f) }, true, out int ignored), Is.EqualTo(EffectorKind.HandLeft));
            Assert.That(ignored, Is.EqualTo(0));
        }

        [Test]
        public void Judge_SameFrameExtraLimbFirst()
        {
            var judge = new ReachJudge();
            var away = new List<EffectorProbe> { Probe(EffectorKind.ExtraRight, Far), Probe(EffectorKind.HandRight, Far, 0.04f) };
            var both = new List<EffectorProbe> { Probe(EffectorKind.ExtraRight, Target), Probe(EffectorKind.HandRight, Target, 0.04f) };
            judge.Reset(Target, TargetRadius, away);
            Assert.That(judge.Update(Target, TargetRadius, both, true, out _), Is.EqualTo(EffectorKind.ExtraRight));
        }

        [Test]
        public void EffectorKeys()
        {
            Assert.That(EffectorKinds.Key(EffectorKind.ExtraRight), Is.EqualTo("extra_right"));
            Assert.That(EffectorKinds.Key(EffectorKind.ExtraLeft), Is.EqualTo("extra_left"));
            Assert.That(EffectorKinds.Key(EffectorKind.HandLeft), Is.EqualTo("hand_left"));
            Assert.That(EffectorKinds.Key(EffectorKind.HandRight), Is.EqualTo("hand_right"));
            Assert.That(EffectorKinds.Key(EffectorKind.None), Is.EqualTo(""));
            Assert.That(EffectorKinds.IsExtraLimb(EffectorKind.ExtraLeft), Is.True);
            Assert.That(EffectorKinds.IsExtraLimb(EffectorKind.HandRight), Is.False);
        }

        // ---- ターゲットの順 ----

        [Test]
        public void Random_SequenceIsPinned()
        {
            // 実行環境や版によらず同じ並びになること（記録を後から再現できるように）
            var random = new StableRandom(12345u);
            CollectionAssert.AreEqual(new[] { 944, 597, 405, 450, 363 }, Enumerable.Range(0, 5).Select(_ => random.Next(1000)).ToArray());
        }

        [Test]
        public void Random_TargetOrderFromSeedTextIsPinned()
        {
            var random = new StableRandom("P01|1|1|1");
            int current = -1;
            var order = new List<int>();
            for (int i = 0; i < 10; i++)
            {
                current = random.NextOther(8, current);
                order.Add(current);
            }
            CollectionAssert.AreEqual(new[] { 4, 7, 4, 0, 4, 5, 1, 7, 0, 6 }, order);
        }

        [Test]
        public void Random_NextOtherNeverRepeatsAndCoversAll()
        {
            var random = new StableRandom("test");
            int current = random.NextOther(8, -1);
            var seen = new HashSet<int> { current };
            for (int i = 0; i < 400; i++)
            {
                int next = random.NextOther(8, current);
                Assert.That(next, Is.Not.EqualTo(current));
                Assert.That(next, Is.InRange(0, 7));
                seen.Add(next);
                current = next;
            }
            Assert.That(seen.Count, Is.EqualTo(8));
            Assert.That(random.NextOther(1, 0), Is.EqualTo(0));
        }

        [Test]
        public void ReachColumnsAreUnique()
        {
            Assert.That(ReachTask.ReachColumns.Distinct().Count(), Is.EqualTo(ReachTask.ReachColumns.Length));
            CollectionAssert.IsSubsetOf(new[] { "segment", "position", "result", "effector", "reach_time", "hand_touches_ignored" }, ReachTask.ReachColumns);
        }
    }
}
