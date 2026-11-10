using ExtraLimb.Core;
using ExtraLimb.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>入力と基準のソースの選び方（Step 3-2）。</summary>
    public class SourceSelectorTests
    {
        sealed class FakeSource : ITrackedPoseSource
        {
            public string Id { get; set; }
            public BodySite Site { get; set; }
            public PoseSpace Space { get; set; }

            public bool TryGetPose(out Pose pose)
            {
                pose = new Pose(Vector3.zero, Quaternion.identity);
                return true;
            }
        }

        static FakeSource SteamVR(string id, BodySite site) => new FakeSource { Id = id, Site = site, Space = PoseSpace.SteamVR };
        static FakeSource Virtual(string id, BodySite site) => new FakeSource { Id = id, Site = site, Space = PoseSpace.Virtual };

        static TrackerRegistry SteamVRRoom()
        {
            var registry = new TrackerRegistry();
            registry.Register(FixedPoseSource.Room(PoseSpace.SteamVR));
            registry.Register(SteamVR("LHR-ANKLE", BodySite.RightAnkle));
            registry.Register(SteamVR("LHR-KNEE", BodySite.RightKnee));
            registry.Register(SteamVR("LHR-WAIST", BodySite.Waist));
            return registry;
        }

        [Test]
        public void Input_FoundBySite()
        {
            var registry = SteamVRRoom();

            Assert.That(SourceSelector.SelectInput(registry, "", BodySite.RightAnkle), Is.EqualTo("LHR-ANKLE"));
            Assert.That(SourceSelector.SelectInput(registry, null, BodySite.RightKnee), Is.EqualTo("LHR-KNEE"));
            Assert.That(SourceSelector.SelectInput(registry, "", BodySite.RightElbow), Is.Null);
            Assert.That(SourceSelector.SelectInput(registry, "", BodySite.Unknown), Is.Null);
        }

        [Test]
        public void Input_ExplicitIdWins_ButNotRoom()
        {
            var registry = SteamVRRoom();

            Assert.That(SourceSelector.SelectInput(registry, "LHR-KNEE", BodySite.RightAnkle), Is.EqualTo("LHR-KNEE"));
            // 部屋は入力にしない（部位で探す）
            Assert.That(SourceSelector.SelectInput(registry, "room:SteamVR", BodySite.RightAnkle), Is.EqualTo("LHR-ANKLE"));
            // 登録されていない ID は部位で探す
            Assert.That(SourceSelector.SelectInput(registry, "LHR-GONE", BodySite.RightAnkle), Is.EqualTo("LHR-ANKLE"));
        }

        [Test]
        public void Input_SteamVRPreferredOverVirtual()
        {
            var registry = new TrackerRegistry();
            registry.Register(Virtual("mock:RightAnkle", BodySite.RightAnkle));
            registry.Register(SteamVR("LHR-ANKLE", BodySite.RightAnkle));

            Assert.That(SourceSelector.SelectInput(registry, "", BodySite.RightAnkle), Is.EqualTo("LHR-ANKLE"));
        }

        [Test]
        public void Reference_DefaultsToRoomOfInputSpace()
        {
            var registry = SteamVRRoom();

            string reference = SourceSelector.SelectReference(registry, "", "LHR-ANKLE");

            Assert.That(reference, Is.EqualTo("room:SteamVR"));
            Assert.That(SourceSelector.IsRoom(registry, reference), Is.True);
            Assert.That(SourceSelector.IsRoom(registry, "LHR-WAIST"), Is.False);
        }

        [Test]
        public void Reference_ExplicitWaistTracker()
        {
            var registry = SteamVRRoom();

            Assert.That(SourceSelector.SelectReference(registry, "LHR-WAIST", "LHR-ANKLE"), Is.EqualTo("LHR-WAIST"));
        }

        [Test]
        public void Reference_SameAsInput_FallsBackToRoom()
        {
            var registry = SteamVRRoom();

            Assert.That(SourceSelector.SelectReference(registry, "LHR-KNEE", "LHR-KNEE"), Is.EqualTo("room:SteamVR"));
        }

        [Test]
        public void Reference_OtherSpace_FallsBackToRoomOfInputSpace()
        {
            var registry = SteamVRRoom();
            registry.Register(FixedPoseSource.Room(PoseSpace.Virtual));
            registry.Register(Virtual("mock:Waist", BodySite.Waist));

            // 仮想の腰は SteamVR の入力の基準にしない
            Assert.That(SourceSelector.SelectReference(registry, "mock:Waist", "LHR-ANKLE"), Is.EqualTo("room:SteamVR"));
        }

        [Test]
        public void Reference_NoInputOrNoRoom_IsNull()
        {
            var registry = new TrackerRegistry();
            registry.Register(SteamVR("LHR-ANKLE", BodySite.RightAnkle));

            Assert.That(SourceSelector.SelectReference(registry, "", null), Is.Null);
            Assert.That(SourceSelector.SelectReference(registry, "", "LHR-GONE"), Is.Null);
            // 部屋が登録されていない
            Assert.That(SourceSelector.SelectReference(registry, "", "LHR-ANKLE"), Is.Null);
        }

        [Test]
        public void Room_IsAlwaysTrackedAtOrigin()
        {
            var room = FixedPoseSource.Room(PoseSpace.SteamVR);

            Assert.That(room.Id, Is.EqualTo("room:SteamVR"));
            Assert.That(room.Site, Is.EqualTo(BodySite.Unknown));
            Assert.That(room.TryGetPose(out var pose), Is.True);
            TestMath.AreClose(Vector3.zero, pose.position);
            TestMath.AreClose(Quaternion.identity, pose.rotation);
        }
    }
}
