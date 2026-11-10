using System;
using System.Collections.Generic;
using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>トラッカーと部位の対応、基準ごとの前の向き（Step 3-2）。</summary>
    public class TrackerBindingProfileTests
    {
        [Test]
        public void Assign_ThenTryGetSite()
        {
            var profile = new TrackerBindingProfile();
            profile.Assign("LHR-AAA", BodySite.RightKnee);

            Assert.That(profile.TryGetSite("LHR-AAA", out var site), Is.True);
            Assert.That(site, Is.EqualTo(BodySite.RightKnee));
            Assert.That(profile.FindSerial(BodySite.RightKnee), Is.EqualTo("LHR-AAA"));
            Assert.That(profile.TryGetSite("LHR-BBB", out _), Is.False);
        }

        [Test]
        public void Assign_SameSerialAgain_ReplacesSite()
        {
            var profile = new TrackerBindingProfile();
            profile.Assign("LHR-AAA", BodySite.RightKnee);
            profile.Assign("LHR-AAA", BodySite.RightElbow);

            Assert.That(profile.bindings.Count, Is.EqualTo(1));
            Assert.That(profile.FindSerial(BodySite.RightKnee), Is.Null);
            Assert.That(profile.FindSerial(BodySite.RightElbow), Is.EqualTo("LHR-AAA"));
        }

        [Test]
        public void Assign_SiteTakenByAnotherTracker_MovesSite()
        {
            // 1つの部位には1台だけ
            var profile = new TrackerBindingProfile();
            profile.Assign("LHR-AAA", BodySite.RightAnkle);
            profile.Assign("LHR-BBB", BodySite.RightAnkle);

            Assert.That(profile.bindings.Count, Is.EqualTo(1));
            Assert.That(profile.FindSerial(BodySite.RightAnkle), Is.EqualTo("LHR-BBB"));
            Assert.That(profile.TryGetSite("LHR-AAA", out _), Is.False);
        }

        [Test]
        public void Assign_Unknown_RemovesBinding()
        {
            var profile = new TrackerBindingProfile();
            profile.Assign("LHR-AAA", BodySite.RightShoulder);
            profile.Assign("LHR-AAA", BodySite.Unknown);

            Assert.That(profile.bindings, Is.Empty);
            Assert.That(profile.FindSerial(BodySite.Unknown), Is.Null);
        }

        [Test]
        public void Assign_EmptySerial_Throws()
        {
            var profile = new TrackerBindingProfile();
            Assert.Throws<ArgumentException>(() => profile.Assign("", BodySite.RightKnee));
        }

        [Test]
        public void ResolveSite_UsesBindingThenDefault()
        {
            var profile = new TrackerBindingProfile();
            profile.Assign("LHR-AAA", BodySite.RightElbow);

            // 割り当てがあればそれ（SteamVR の役割より優先）
            Assert.That(profile.ResolveSite("LHR-AAA", BodySite.RightAnkle), Is.EqualTo(BodySite.RightElbow));
            // 割り当てがなければ役割の部位
            Assert.That(profile.ResolveSite("LHR-BBB", BodySite.RightKnee), Is.EqualTo(BodySite.RightKnee));
            // 役割の部位がほかのトラッカーに割り当て済みなら未割り当て
            Assert.That(profile.ResolveSite("LHR-CCC", BodySite.RightElbow), Is.EqualTo(BodySite.Unknown));
            Assert.That(profile.ResolveSite("LHR-DDD", BodySite.Unknown), Is.EqualTo(BodySite.Unknown));
        }

        static readonly BodySite[] Order =
        {
            BodySite.Unknown, BodySite.RightAnkle, BodySite.RightKnee, BodySite.RightShoulder, BodySite.RightElbow, BodySite.Waist,
        };

        [Test]
        public void NextFreeSite_SkipsSitesInUse()
        {
            // トラッカー1が右膝のとき、トラッカー2を右足首から進めると右膝を飛ばして右肩へ
            var taken = new HashSet<BodySite> { BodySite.RightKnee };

            Assert.That(TrackerBindingProfile.NextFreeSite(BodySite.Unknown, Order, taken), Is.EqualTo(BodySite.RightAnkle));
            Assert.That(TrackerBindingProfile.NextFreeSite(BodySite.RightAnkle, Order, taken), Is.EqualTo(BodySite.RightShoulder));
        }

        [Test]
        public void NextFreeSite_WrapsToUnknown()
        {
            var taken = new HashSet<BodySite>();

            Assert.That(TrackerBindingProfile.NextFreeSite(BodySite.Waist, Order, taken), Is.EqualTo(BodySite.Unknown));
            // 一覧にない部位（役割から決めた胸など）からは先頭へ
            Assert.That(TrackerBindingProfile.NextFreeSite(BodySite.Chest, Order, taken), Is.EqualTo(BodySite.Unknown));
            Assert.That(TrackerBindingProfile.NextFreeSite(BodySite.Unknown, Order, null), Is.EqualTo(BodySite.RightAnkle));
        }

        [Test]
        public void NextFreeSite_AllTaken_ReturnsUnknown()
        {
            var taken = new HashSet<BodySite> { BodySite.RightAnkle, BodySite.RightKnee, BodySite.RightShoulder, BodySite.RightElbow, BodySite.Waist };

            Assert.That(TrackerBindingProfile.NextFreeSite(BodySite.Unknown, Order, taken), Is.EqualTo(BodySite.Unknown));
            Assert.That(TrackerBindingProfile.NextFreeSite(BodySite.RightKnee, Order, taken), Is.EqualTo(BodySite.Unknown));
        }

        [Test]
        public void SetForward_StoresNormalizedPerReference()
        {
            var profile = new TrackerBindingProfile();
            profile.SetForward("room:SteamVR", new Vector3(0f, 0f, -3f));
            profile.SetForward("LHR-WAIST", new Vector3(2f, 0f, 0f));
            profile.SetForward("room:SteamVR", new Vector3(0f, 0f, 5f));

            Assert.That(profile.forwards.Count, Is.EqualTo(2));
            Assert.That(profile.TryGetForward("room:SteamVR", out var room), Is.True);
            TestMath.AreClose(Vector3.forward, room);
            Assert.That(profile.TryGetForward("LHR-WAIST", out var waist), Is.True);
            TestMath.AreClose(Vector3.right, waist);
            Assert.That(profile.TryGetForward("room:Virtual", out _), Is.False);
        }

        [Test]
        public void SetForward_InvalidArguments_Throw()
        {
            var profile = new TrackerBindingProfile();
            Assert.Throws<ArgumentException>(() => profile.SetForward("", Vector3.forward));
            Assert.Throws<ArgumentException>(() => profile.SetForward("room:SteamVR", Vector3.zero));
        }

        [Test]
        public void Json_RoundTrip()
        {
            var profile = new TrackerBindingProfile();
            profile.Assign("LHR-AAA", BodySite.RightAnkle);
            profile.Assign("LHR-BBB", BodySite.RightElbow);
            profile.SetForward("room:SteamVR", new Vector3(0.6f, 0f, 0.8f));

            var loaded = TrackerBindingProfile.FromJson(profile.ToJson());

            Assert.That(loaded.FindSerial(BodySite.RightAnkle), Is.EqualTo("LHR-AAA"));
            Assert.That(loaded.FindSerial(BodySite.RightElbow), Is.EqualTo("LHR-BBB"));
            Assert.That(loaded.TryGetForward("room:SteamVR", out var forward), Is.True);
            TestMath.AreClose(new Vector3(0.6f, 0f, 0.8f), forward);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{ this is not json")]
        public void FromJson_EmptyOrBroken_ReturnsEmptyProfile(string json)
        {
            var profile = TrackerBindingProfile.FromJson(json);

            Assert.That(profile.bindings, Is.Not.Null.And.Empty);
            Assert.That(profile.forwards, Is.Not.Null.And.Empty);
        }

        [Test]
        public void BodySiteNames_EverySiteHasJapaneseName()
        {
            foreach (BodySite site in Enum.GetValues(typeof(BodySite)))
            {
                string name = BodySiteNames.Japanese(site);
                Assert.That(name, Is.Not.Empty, site.ToString());
                if (site != BodySite.Unknown) Assert.That(name, Is.Not.EqualTo("未割り当て"), site.ToString());
            }
            Assert.That(BodySiteNames.Japanese(BodySite.RightElbow), Is.EqualTo("右肘"));
        }
    }
}
