using ExtraLimb.Core;
using ExtraLimb.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>キーボードで動かす仮想の体（Step 2-1）。</summary>
    public class MockBodyTests
    {
        static MockBody CreateBody()
        {
            return new MockBody(
                new Pose(new Vector3(0f, 0.95f, 0f), Quaternion.identity),
                new[]
                {
                    new MockTrackerSpec(BodySite.RightAnkle, new Vector3(0.15f, -0.85f, 0.25f)),
                    new MockTrackerSpec(BodySite.LeftShoulder, new Vector3(-0.18f, 0.45f, 0f)),
                });
        }

        [Test]
        public void Create_PlacesTrackersRelativeToWaist()
        {
            var body = CreateBody();

            Assert.That(body.Waist.Site, Is.EqualTo(BodySite.Waist));
            Assert.That(body.Trackers.Count, Is.EqualTo(2));
            Assert.That(body.Trackers[0].Id, Is.EqualTo("mock:RightAnkle"));
            TestMath.AreClose(new Vector3(0.15f, 0.1f, 0.25f), body.Trackers[0].Pose.position);
        }

        [Test]
        public void MoveForward_GivesForwardDisplacement()
        {
            var body = CreateBody();
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(body.Waist.Pose, body.Trackers[0].Pose);

            body.Move(0, new Vector3(0f, 0f, 0.1f));

            TestMath.AreClose(new Vector3(0f, 0f, 0.1f), calibrator.Compute(body.Waist.Pose, body.Trackers[0].Pose).position);
        }

        [Test]
        public void TurningBody_KeepsDisplacementButMovesWorldPose()
        {
            var body = CreateBody();
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(body.Waist.Pose, body.Trackers[0].Pose);
            body.Move(0, new Vector3(0.05f, 0f, 0.1f));
            var before = body.Trackers[0].Pose.position;

            body.Turn(90f);

            Assert.That(Vector3.Distance(before, body.Trackers[0].Pose.position), Is.GreaterThan(0.1f), "ワールドの位置は動く");
            TestMath.AreClose(new Vector3(0.05f, 0f, 0.1f), calibrator.Compute(body.Waist.Pose, body.Trackers[0].Pose).position, message: "変位は変わらない");
        }

        [Test]
        public void MoveAfterTurning_IsInBodyFrame()
        {
            var body = CreateBody();
            var calibrator = new NeutralPoseCalibrator();
            body.Turn(90f);
            calibrator.Recenter(body.Waist.Pose, body.Trackers[0].Pose);

            body.Move(0, new Vector3(0f, 0f, 0.1f)); // 体の前へ

            TestMath.AreClose(new Vector3(0f, 0f, 0.1f), calibrator.Compute(body.Waist.Pose, body.Trackers[0].Pose).position);
            TestMath.AreClose(new Vector3(0.1f, 0f, 0f), body.Trackers[0].Pose.position - new Vector3(0.25f, 0.1f, -0.15f), message: "ワールドでは+Xへ動く");
        }

        [Test]
        public void Twist_RotatesAboutTrackerLocalZ()
        {
            var body = CreateBody();
            var calibrator = new NeutralPoseCalibrator();
            calibrator.Recenter(body.Waist.Pose, body.Trackers[0].Pose);

            body.Twist(0, 30f);

            var delta = calibrator.Compute(body.Waist.Pose, body.Trackers[0].Pose);
            TestMath.AreClose(Vector3.zero, delta.position);
            TestMath.AreClose(Quaternion.AngleAxis(30f, Vector3.forward), delta.rotation);
        }

        [Test]
        public void Reset_RestoresStartPoses()
        {
            var body = CreateBody();
            var start = body.Trackers[1].Pose;
            body.Move(1, Vector3.one);
            body.Turn(45f);
            body.Twist(1, 20f);

            body.Reset();

            TestMath.AreClose(start.position, body.Trackers[1].Pose.position);
            TestMath.AreClose(start.rotation, body.Trackers[1].Pose.rotation);
            Assert.That(body.BodyYaw, Is.EqualTo(0f));
        }
    }
}
