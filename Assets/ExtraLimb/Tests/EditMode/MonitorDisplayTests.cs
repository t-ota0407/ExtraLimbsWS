using ExtraLimb.Experiment;
using ExtraLimb.UI;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>モニタと HMD の表示の振り分け、第三者視点、HMD の通知（Step 4-2b）。</summary>
    public class MonitorDisplayTests
    {
        // ---- レイヤー ----

        [Test]
        public void MonitorCamera_DrawsOverlay_ButNotHmdNotices()
        {
            int mask = DisplayLayers.MonitorMask(~0);

            Assert.That(mask & DisplayLayers.MonitorOnlyMask, Is.Not.Zero);
            Assert.That(mask & DisplayLayers.HmdOnlyMask, Is.Zero);
            Assert.That(mask & 1, Is.Not.Zero, "Default のレイヤーは描く");
        }

        [Test]
        public void HmdCamera_DrawsNotices_AndOverlayOnlyWhenShown()
        {
            int hidden = DisplayLayers.HmdMask(~0, false);
            int shown = DisplayLayers.HmdMask(1, true);

            Assert.That(hidden & DisplayLayers.MonitorOnlyMask, Is.Zero);
            Assert.That(hidden & DisplayLayers.HmdOnlyMask, Is.Not.Zero);
            Assert.That(shown & DisplayLayers.MonitorOnlyMask, Is.Not.Zero);
            Assert.That(shown & DisplayLayers.HmdOnlyMask, Is.Not.Zero);
        }

        [Test]
        public void Layers_AreUserLayers()
        {
            Assert.That(DisplayLayers.MonitorOnly, Is.InRange(8, 31));
            Assert.That(DisplayLayers.HmdOnly, Is.InRange(8, 31));
            Assert.That(DisplayLayers.HmdOnly, Is.Not.EqualTo(DisplayLayers.MonitorOnly));
        }

        // ---- 第三者視点 ----

        [Test]
        public void Viewpoint_IsRelativeToBody()
        {
            // 体が右（+x）を向いているとき、体の後ろ（-z）は世界の -x
            var anchor = new Pose(new Vector3(1f, 0f, 2f), Quaternion.Euler(0f, 90f, 0f));
            var viewpoint = new SpectatorCamera.Viewpoint("後ろ", new Vector3(0f, 1.5f, -2f), new Vector3(0f, 1.5f, 0f));

            var pose = SpectatorCamera.ComputePose(anchor, viewpoint);

            TestMath.AreClose(new Vector3(-1f, 1.5f, 2f), pose.position);
            TestMath.AreClose(Vector3.right, pose.rotation * Vector3.forward);
        }

        [Test]
        public void Anchor_IsOnFloorAndHorizontal()
        {
            var hips = new Pose(new Vector3(0.2f, 1.0f, -0.3f), Quaternion.Euler(20f, -90f, 10f));

            var anchor = SpectatorCamera.AnchorFrom(hips);

            TestMath.AreClose(new Vector3(0.2f, 0f, -0.3f), anchor.position);
            var forward = anchor.rotation * Vector3.forward;
            Assert.That(forward.y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(Vector3.Dot(forward, Vector3.left), Is.GreaterThan(0.99f));
        }

        // ---- HMD の通知 ----

        [Test]
        public void Notice_IsShownForGivenSeconds()
        {
            var schedule = new NoticeSchedule();
            schedule.Show(10.0, "条件が変わります", 2.5);

            Assert.That(schedule.TextAt(10.0), Is.EqualTo("条件が変わります"));
            Assert.That(schedule.TextAt(12.4), Is.EqualTo("条件が変わります"));
            Assert.That(schedule.TextAt(12.6), Is.Empty);
            Assert.That(schedule.IsActive(12.6), Is.False);
        }

        [Test]
        public void Countdown_ShowsNumbersThenDoneText()
        {
            var schedule = new NoticeSchedule();
            schedule.Countdown(0.0, 3, "はじめ");

            Assert.That(schedule.TextAt(0.1), Is.EqualTo("3"));
            Assert.That(schedule.TextAt(1.1), Is.EqualTo("2"));
            Assert.That(schedule.TextAt(2.1), Is.EqualTo("1"));
            Assert.That(schedule.TextAt(3.1), Is.EqualTo("はじめ"));
            Assert.That(schedule.TextAt(4.1), Is.Empty);
        }

        [Test]
        public void NewNotice_ReplacesCurrentOne()
        {
            var schedule = new NoticeSchedule();
            schedule.Countdown(0.0, 3, "はじめ");
            schedule.Show(0.5, "やめ", 1.0);

            Assert.That(schedule.TextAt(1.2), Is.EqualTo("やめ"));
            Assert.That(schedule.TextAt(2.1), Is.Empty);
        }

        [Test]
        public void Clear_RemovesNotice()
        {
            var schedule = new NoticeSchedule();
            schedule.Show(0.0, "a", 5.0);
            schedule.Clear();

            Assert.That(schedule.TextAt(1.0), Is.Empty);
        }
    }
}
