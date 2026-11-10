using ExtraLimb.Core;
using ExtraLimb.Tracking.OpenVR;
using NUnit.Framework;
using UnityEngine;
using Valve.VR;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>OpenVR の値を Unity の形に直す処理（Step 2-1）。</summary>
    public class OpenVRPoseUtilityTests
    {
        // 回転行列 R（3x3）と平行移動 t から OpenVR の3x4行列を作る
        static HmdMatrix34_t Matrix(Matrix4x4 r, Vector3 t)
        {
            return new HmdMatrix34_t
            {
                m0 = r.m00, m1 = r.m01, m2 = r.m02, m3 = t.x,
                m4 = r.m10, m5 = r.m11, m6 = r.m12, m7 = t.y,
                m8 = r.m20, m9 = r.m21, m10 = r.m22, m11 = t.z,
            };
        }

        [Test]
        public void Identity_IsIdentityPose()
        {
            var pose = OpenVRPoseUtility.ToUnityPose(Matrix(Matrix4x4.identity, Vector3.zero));

            TestMath.AreClose(Vector3.zero, pose.position);
            TestMath.AreClose(Quaternion.identity, pose.rotation);
        }

        [Test]
        public void Translation_FlipsZ()
        {
            var pose = OpenVRPoseUtility.ToUnityPose(Matrix(Matrix4x4.identity, new Vector3(1f, 2f, 3f)));

            TestMath.AreClose(new Vector3(1f, 2f, -3f), pose.position);
        }

        [Test]
        public void ConvertedPose_TransformsPointsLikeOpenVR()
        {
            // OpenVR で点 p を (R, t) で動かした結果と、変換後の Unity の姿勢で動かした結果が一致する
            var random = new System.Random(5);
            for (int i = 0; i < 50; i++)
            {
                var r = Matrix4x4.Rotate(TestMath.RandomRotation(random));
                var t = TestMath.RandomPosition(random, 3f);
                var p = TestMath.RandomPosition(random, 1f);
                var pose = OpenVRPoseUtility.ToUnityPose(Matrix(r, t));

                var expected = OpenVRPoseUtility.ToUnityPoint(r.MultiplyVector(p) + t);
                var actual = pose.rotation * OpenVRPoseUtility.ToUnityPoint(p) + pose.position;

                TestMath.AreClose(expected, actual, message: $"case {i}");
            }
        }

        [TestCase("TrackerRole_Waist", BodySite.Waist)]
        [TestCase("TrackerRole_LeftFoot", BodySite.LeftAnkle)]
        [TestCase("TrackerRole_RightFoot", BodySite.RightAnkle)]
        [TestCase("TrackerRole_RightKnee", BodySite.RightKnee)]
        [TestCase("TrackerRole_LeftShoulder", BodySite.LeftShoulder)]
        [TestCase("TrackerRole_Chest", BodySite.Chest)]
        [TestCase("TrackerRole_LeftElbow", BodySite.LeftElbow)]
        [TestCase("TrackerRole_RightElbow", BodySite.RightElbow)]
        [TestCase("TrackerRole_Camera", BodySite.Unknown)]
        [TestCase("TrackerRole_None", BodySite.Unknown)]
        [TestCase(null, BodySite.Unknown)]
        public void TrackerRole_MapsToBodySite(string role, BodySite expected)
        {
            Assert.That(OpenVRPoseUtility.SiteFromTrackerRole(role), Is.EqualTo(expected));
        }
    }
}
