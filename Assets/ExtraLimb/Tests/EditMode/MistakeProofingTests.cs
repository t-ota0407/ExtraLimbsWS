using System.Collections.Generic;
using ExtraLimb.Core;
using ExtraLimb.Experiment;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>
    /// 付け間違いと操作の誤りを防ぐ仕組み（2026-11-15）：トラッカーの確認（TrackerCheck）、
    /// Re-center のときの位置のチェック（TrackerLayoutCheck）、開始のときに止まるのを待つ（StillnessDetector）。
    /// </summary>
    public class MistakeProofingTests
    {
        // ---- トラッカーの確認 ----

        static List<(string, Vector3)> Positions(params (string id, Vector3 position)[] items) => new List<(string, Vector3)>(items);

        // id のトラッカーだけを、振幅 amplitude で frames フレーム揺らす（ほかは止まっている）
        static BodySite Shake(TrackerCheck check, string id, string[] all, int frames, float amplitude = 0.03f, Dictionary<string, Vector3> others = null)
        {
            var result = BodySite.Unknown;
            for (int f = 0; f < frames && result == BodySite.Unknown; f++)
            {
                var list = new List<(string, Vector3)>();
                for (int i = 0; i < all.Length; i++)
                {
                    var basePosition = new Vector3(i, 0f, 0f);
                    var offset = all[i] == id ? new Vector3(0f, (f % 2 == 0 ? 1f : -1f) * amplitude, 0f) : Vector3.zero;
                    if (others != null && others.TryGetValue(all[i], out var extra)) offset += extra * (f % 2 == 0 ? 1f : -1f);
                    list.Add((all[i], basePosition + offset));
                }
                result = check.Update(list);
            }
            return result;
        }

        [Test]
        public void TrackerCheck_AssignsTheTrackerThatMoved_InOrder()
        {
            string[] all = { "A", "B", "C", "D" };
            var check = new TrackerCheck();
            check.Begin(new[] { BodySite.RightKnee, BodySite.LeftKnee, BodySite.RightShoulder, BodySite.LeftShoulder });
            Assert.That(check.CurrentSite, Is.EqualTo(BodySite.RightKnee));

            Assert.That(Shake(check, "C", all, 20), Is.EqualTo(BodySite.RightKnee), "右膝を動かしたら C が動いた");
            Assert.That(Shake(check, "A", all, 20), Is.EqualTo(BodySite.LeftKnee));
            Assert.That(Shake(check, "C", all, 20), Is.EqualTo(BodySite.Unknown), "もう決まったトラッカーは数えない");
            Assert.That(Shake(check, "D", all, 20), Is.EqualTo(BodySite.RightShoulder));
            Assert.That(Shake(check, "B", all, 20), Is.EqualTo(BodySite.LeftShoulder));

            Assert.That(check.IsComplete, Is.True);
            Assert.That(check.Found[BodySite.RightKnee], Is.EqualTo("C"));
            Assert.That(check.Found[BodySite.LeftKnee], Is.EqualTo("A"));
            Assert.That(check.Found[BodySite.RightShoulder], Is.EqualTo("D"));
            Assert.That(check.Found[BodySite.LeftShoulder], Is.EqualTo("B"));
        }

        [Test]
        public void TrackerCheck_SkipsSitesWithoutTrackers_AndCountsRemainingTrackers()
        {
            // 右だけ2台（左のトラッカーなし）：右を確かめた後、残りのトラッカーは0台
            string[] all = { "A", "B" };
            var check = new TrackerCheck();
            check.Begin(new[] { BodySite.RightShoulder, BodySite.RightElbow, BodySite.LeftShoulder, BodySite.LeftElbow });
            Assert.That(check.UnassignedSeenCount, Is.EqualTo(0), "まだ位置が入っていない");
            Assert.That(Shake(check, "B", all, 20), Is.EqualTo(BodySite.RightShoulder));
            Assert.That(check.UnassignedSeenCount, Is.EqualTo(1));
            Assert.That(Shake(check, "A", all, 20), Is.EqualTo(BodySite.RightElbow));
            Assert.That(check.UnassignedSeenCount, Is.EqualTo(0), "左の部位に使えるトラッカーは残っていない");

            Assert.That(check.Skip(), Is.EqualTo(BodySite.LeftShoulder));
            Assert.That(check.Skip(), Is.EqualTo(BodySite.LeftElbow));
            Assert.That(check.IsComplete, Is.True);
            Assert.That(check.Skip(), Is.EqualTo(BodySite.Unknown), "終わった後は飛ばさない");
            Assert.That(check.IsSkipped(BodySite.LeftShoulder) && check.IsSkipped(BodySite.LeftElbow), Is.True);
            Assert.That(check.Found.Count, Is.EqualTo(2));

            check.Begin(new[] { BodySite.RightKnee });
            Assert.That(check.Skipped.Count, Is.EqualTo(0), "始め直したら飛ばした部位も消える");
        }

        // 体の上の位置に置いたトラッカーのうち id だけを揺らす
        static BodySite ShakeAt(TrackerCheck check, string id, Dictionary<string, Vector3> placed, int frames = 20)
        {
            var result = BodySite.Unknown;
            for (int f = 0; f < frames && result == BodySite.Unknown; f++)
            {
                var list = new List<(string, Vector3)>();
                foreach (var pair in placed)
                    list.Add((pair.Key, pair.Value + (pair.Key == id ? new Vector3(0f, (f % 2 == 0 ? 1f : -1f) * 0.03f, 0f) : Vector3.zero)));
                result = check.Update(list);
            }
            return result;
        }

        static readonly BodySite[] ShoulderElbowRightFirst = { BodySite.RightShoulder, BodySite.RightElbow, BodySite.LeftShoulder, BodySite.LeftElbow };

        [Test]
        public void TrackerCheck_CanCoverRemaining_WhenLeftTrackersAreOnTheLeft()
        {
            // 座った姿勢：肩・肘の左右
            var placed = new Dictionary<string, Vector3>
            {
                { "A", new Vector3(0.18f, 1.05f, 0f) }, { "B", new Vector3(0.25f, 0.75f, 0.05f) },
                { "C", new Vector3(-0.18f, 1.05f, 0f) }, { "D", new Vector3(-0.25f, 0.75f, 0.05f) },
            };
            var check = new TrackerCheck();
            check.Begin(ShoulderElbowRightFirst);
            Assert.That(ShakeAt(check, "A", placed), Is.EqualTo(BodySite.RightShoulder));
            Assert.That(ShakeAt(check, "B", placed), Is.EqualTo(BodySite.RightElbow));
            Assert.That(check.CanCoverRemaining(Vector3.up), Is.True, "左の肩・肘の位置にトラッカーがある");
        }

        [Test]
        public void TrackerCheck_CannotCoverRemaining_WhenSpareTrackersAreOnTheRightLeg()
        {
            // 右だけ4台：右肩・右肘・右膝・右足首（前半の確認では膝・足首のトラッカーが余る）
            var placed = new Dictionary<string, Vector3>
            {
                { "A", new Vector3(0.18f, 1.05f, 0f) }, { "B", new Vector3(0.25f, 0.75f, 0.05f) },
                { "C", new Vector3(0.11f, 0.52f, 0.42f) }, { "D", new Vector3(0.12f, 0.10f, 0.45f) },
            };
            var check = new TrackerCheck();
            check.Begin(ShoulderElbowRightFirst);
            Assert.That(ShakeAt(check, "A", placed), Is.EqualTo(BodySite.RightShoulder));
            Assert.That(ShakeAt(check, "B", placed), Is.EqualTo(BodySite.RightElbow));
            Assert.That(check.UnassignedSeenCount, Is.EqualTo(2));
            Assert.That(check.CanCoverRemaining(Vector3.up), Is.False, "左肩らしい位置にトラッカーがない");

            // 後半（膝・足首）：肩・肘のトラッカーが余る
            check.Begin(new[] { BodySite.RightKnee, BodySite.RightAnkle, BodySite.LeftKnee, BodySite.LeftAnkle });
            Assert.That(ShakeAt(check, "C", placed), Is.EqualTo(BodySite.RightKnee));
            Assert.That(ShakeAt(check, "D", placed), Is.EqualTo(BodySite.RightAnkle));
            Assert.That(check.CanCoverRemaining(Vector3.up), Is.False, "左足首らしい位置にトラッカーがない");
        }

        [Test]
        public void TrackerCheck_WaitsWhenTwoTrackersMoveAlike()
        {
            string[] all = { "A", "B" };
            var check = new TrackerCheck();
            check.Begin(new[] { BodySite.RightShoulder });
            // 肩をすくめると肘も同じくらい動く
            var result = Shake(check, "A", all, 6, 0.03f, new Dictionary<string, Vector3> { { "B", new Vector3(0f, 0.025f, 0f) } });
            Assert.That(result, Is.EqualTo(BodySite.Unknown), "同じくらい動いたら決めない");
            Assert.That(check.Hint, Is.Not.Empty);
            Assert.That(check.IsActive, Is.True);

            Assert.That(Shake(check, "A", all, 40), Is.EqualTo(BodySite.RightShoulder), "そのトラッカーだけを揺らすと決まる");
        }

        [Test]
        public void TrackerCheck_IgnoresJitterAndJumps()
        {
            var check = new TrackerCheck();
            check.Begin(new[] { BodySite.RightAnkle });
            for (int f = 0; f < 500; f++)
                check.Update(Positions(("A", new Vector3(0f, (f % 2) * 0.0003f, 0f)), ("B", new Vector3(1f, 0f, 0f))));
            check.Update(Positions(("A", Vector3.zero), ("B", new Vector3(3f, 0f, 0f))));
            check.Update(Positions(("A", Vector3.zero), ("B", new Vector3(3f, 0f, 0f))));
            Assert.That(check.IsActive, Is.True, "揺れ（0.3 mm）と追跡の飛び（2 m）では決まらない");
        }

        [Test]
        public void TrackerCheck_ObserveDoesNotCount()
        {
            var check = new TrackerCheck();
            check.Begin(new[] { BodySite.RightElbow });
            check.Observe(Positions(("A", Vector3.zero)));
            check.Observe(Positions(("A", new Vector3(0.3f, 0f, 0f))));
            Assert.That(check.Update(Positions(("A", new Vector3(0.3f, 0f, 0f)))), Is.EqualTo(BodySite.Unknown), "一息の間に動いた分は数えない");
        }

        // ---- Re-center のときの位置のチェック ----

        // 座った人の左右の肩・膝（体は yaw を向いている）
        static List<(BodySite, Vector3)> SeatedLayout(Quaternion yaw)
        {
            return new List<(BodySite, Vector3)>
            {
                (BodySite.RightShoulder, yaw * new Vector3(0.18f, 1.05f, 0f)),
                (BodySite.LeftShoulder, yaw * new Vector3(-0.18f, 1.05f, 0f)),
                (BodySite.RightKnee, yaw * new Vector3(0.11f, 0.52f, 0.42f)),
                (BodySite.LeftKnee, yaw * new Vector3(-0.11f, 0.52f, 0.42f)),
            };
        }

        [Test]
        public void LayoutCheck_CorrectLayout_HasNoProblems()
        {
            var yaw = Quaternion.AngleAxis(40f, Vector3.up);
            Assert.That(TrackerLayoutCheck.Find(SeatedLayout(yaw), Vector3.up, yaw * Vector3.right), Is.Empty);
        }

        [Test]
        public void LayoutCheck_FindsSwappedSidesAndKinds()
        {
            var yaw = Quaternion.AngleAxis(40f, Vector3.up);
            var layout = SeatedLayout(yaw);
            // 膝の左右を取り違えた
            layout[2] = (BodySite.LeftKnee, layout[2].Item2);
            layout[3] = (BodySite.RightKnee, layout[3].Item2);
            var problems = TrackerLayoutCheck.Find(layout, Vector3.up, yaw * Vector3.right);
            Assert.That(problems, Has.Count.EqualTo(1));
            StringAssert.Contains("右膝と左膝", problems[0]);

            // 右の肩と右の膝を取り違えた
            layout = SeatedLayout(yaw);
            layout[0] = (BodySite.RightKnee, layout[0].Item2);
            layout[2] = (BodySite.RightShoulder, layout[2].Item2);
            problems = TrackerLayoutCheck.Find(layout, Vector3.up, yaw * Vector3.right);
            Assert.That(problems.Exists(p => p.Contains("右肩のトラッカーが右膝より高くありません")), Is.True, string.Join(" / ", problems));
        }

        // ---- 止まるのを待つ ----

        static List<(string, bool, Pose)> One(Vector3 position, float angleDeg = 0f, bool tracked = true) =>
            new List<(string, bool, Pose)> { ("A", tracked, new Pose(position, Quaternion.AngleAxis(angleDeg, Vector3.up))) };

        [Test]
        public void Stillness_NeedsHoldTimeBelowSpeeds()
        {
            var detector = new StillnessDetector { MaxSpeed = 0.03f, MaxAngularSpeed = 15f, HoldSeconds = 0.5f };
            const float dt = 0.1f;
            Assert.That(detector.Update(One(Vector3.zero), dt), Is.False, "1フレーム目は比べるものがない");
            for (int i = 1; i <= 4; i++) Assert.That(detector.Update(One(Vector3.zero), dt), Is.False);
            Assert.That(detector.Update(One(Vector3.zero), dt), Is.True, "0.5 秒止まった");

            Assert.That(detector.Update(One(new Vector3(0.01f, 0f, 0f)), dt), Is.False, "10 cm/s で動いた");
            Assert.That(detector.StillSeconds, Is.EqualTo(0f));
            detector.Reset();
            detector.Update(One(Vector3.zero), dt);
            Assert.That(detector.Update(One(Vector3.zero, 3f), dt), Is.False, "30°/s で回った");
            Assert.That(detector.Update(One(Vector3.zero, 3f, tracked: false), dt), Is.False, "追跡が切れた");
        }
    }
}
