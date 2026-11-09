using ExtraLimb.Core;
using ExtraLimb.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>入力ソースの一覧（Step 2-1）。</summary>
    public class TrackerRegistryTests
    {
        static MockPoseSource Mock(string id, BodySite site) => new MockPoseSource(id, site, new Pose(Vector3.zero, Quaternion.identity));

        sealed class FakeSource : ITrackedPoseSource
        {
            public string Id { get; set; }
            public BodySite Site { get; set; }
            public PoseSpace Space { get; set; }

            public bool TryGetPose(out Pose pose)
            {
                pose = default;
                return false;
            }
        }

        [Test]
        public void Register_AddsSourceAndRaisesChanged()
        {
            var registry = new TrackerRegistry();
            int changed = 0;
            registry.Changed += () => changed++;

            registry.Register(Mock("a", BodySite.Waist));

            Assert.That(registry.Sources.Count, Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(registry.TryGet("a", out var found), Is.True);
            Assert.That(found.Site, Is.EqualTo(BodySite.Waist));
        }

        [Test]
        public void Register_SameIdReplacesSource()
        {
            var registry = new TrackerRegistry();
            registry.Register(Mock("a", BodySite.Waist));

            var replacement = Mock("a", BodySite.RightAnkle);
            registry.Register(replacement);

            Assert.That(registry.Sources.Count, Is.EqualTo(1));
            Assert.That(registry.TryGet("a", out var found), Is.True);
            Assert.That(found, Is.SameAs(replacement));
        }

        [Test]
        public void Register_SameInstanceTwice_DoesNothing()
        {
            var registry = new TrackerRegistry();
            var source = Mock("a", BodySite.Waist);
            registry.Register(source);
            int changed = 0;
            registry.Changed += () => changed++;

            registry.Register(source);

            Assert.That(registry.Sources.Count, Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(0));
        }

        [Test]
        public void Unregister_RemovesSource()
        {
            var registry = new TrackerRegistry();
            registry.Register(Mock("a", BodySite.Waist));
            registry.Register(Mock("b", BodySite.RightAnkle));

            Assert.That(registry.Unregister("a"), Is.True);
            Assert.That(registry.Unregister("a"), Is.False);
            Assert.That(registry.TryGet("a", out _), Is.False);
            Assert.That(registry.Sources.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryGet_NullOrUnknownId_ReturnsFalse()
        {
            var registry = new TrackerRegistry();
            registry.Register(Mock("a", BodySite.Waist));

            Assert.That(registry.TryGet(null, out _), Is.False);
            Assert.That(registry.TryGet("x", out _), Is.False);
        }

        [Test]
        public void FindBySite_CanFilterBySpace()
        {
            var registry = new TrackerRegistry();
            registry.Register(Mock("mock:Waist", BodySite.Waist));
            registry.Register(new FakeSource { Id = "LHR-1", Site = BodySite.Waist, Space = PoseSpace.SteamVR });

            Assert.That(registry.FindBySite(BodySite.Waist).Id, Is.EqualTo("mock:Waist"));
            Assert.That(registry.FindBySite(BodySite.Waist, PoseSpace.SteamVR).Id, Is.EqualTo("LHR-1"));
            Assert.That(registry.FindBySite(BodySite.Chest), Is.Null);
        }

        [Test]
        public void MockSource_ReportsTrackingState()
        {
            var source = Mock("a", BodySite.RightAnkle);
            source.Pose = new Pose(Vector3.one, Quaternion.identity);

            Assert.That(source.TryGetPose(out var pose), Is.True);
            TestMath.AreClose(Vector3.one, pose.position);

            source.IsTracking = false;
            Assert.That(source.TryGetPose(out _), Is.False);
            Assert.That(source.Space, Is.EqualTo(PoseSpace.Virtual));
        }
    }
}
