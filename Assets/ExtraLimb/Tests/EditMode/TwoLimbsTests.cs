using System.Collections.Generic;
using ExtraLimb.Core;
using ExtraLimb.Experiment;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>
    /// 追加肢を左右2本にする（2026-11-13）：左の追加肢は左の同じ部位で、右の型を鏡に写して動かす。
    /// あわせて、リーチング課題で追加肢が触れたかを見る球の位置。
    /// </summary>
    public class TwoLimbsTests
    {
        readonly List<MappingPreset> created = new List<MappingPreset>();

        MappingPreset Preset(string id, BodySite site, AxisMapping mapping)
        {
            var preset = ScriptableObject.CreateInstance<MappingPreset>();
            preset.id = id;
            preset.displayName = id;
            preset.site = site;
            preset.mapping = mapping;
            created.Add(preset);
            return preset;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var preset in created) Object.DestroyImmediate(preset);
            created.Clear();
        }

        // ---- 部位の左右 ----

        [Test]
        public void BodySites_MirrorAndOnSide()
        {
            Assert.That(BodySites.Mirror(BodySite.RightKnee), Is.EqualTo(BodySite.LeftKnee));
            Assert.That(BodySites.Mirror(BodySite.LeftElbow), Is.EqualTo(BodySite.RightElbow));
            Assert.That(BodySites.Mirror(BodySite.Waist), Is.EqualTo(BodySite.Waist), "左右のない部位はそのまま");

            Assert.That(BodySites.OnSide(BodySite.RightAnkle, left: true), Is.EqualTo(BodySite.LeftAnkle));
            Assert.That(BodySites.OnSide(BodySite.LeftAnkle, left: true), Is.EqualTo(BodySite.LeftAnkle));
            Assert.That(BodySites.OnSide(BodySite.LeftShoulder, left: false), Is.EqualTo(BodySite.RightShoulder));
            Assert.That(BodySites.OnSide(BodySite.Waist, left: true), Is.EqualTo(BodySite.Waist));

            Assert.That(BodySites.IsLeft(BodySite.LeftKnee) && !BodySites.IsRight(BodySite.LeftKnee), Is.True);
            Assert.That(BodySites.HasSide(BodySite.Head), Is.False);
        }

        [Test]
        public void Kind_DropsSide()
        {
            Assert.That(BodySiteNames.Kind(BodySite.RightAnkle), Is.EqualTo("足首"));
            Assert.That(BodySiteNames.Kind(BodySite.LeftElbow), Is.EqualTo("肘"));
            Assert.That(BodySiteNames.Kind(BodySite.Waist), Is.EqualTo("腰"));
        }

        // ---- 型を鏡に写す ----

        [Test]
        public void Mirrored_KeepsSameDirectionMappings()
        {
            Assert.That(AxisMapping.Identity.Mirrored(), Is.EqualTo(AxisMapping.Identity), "並進そのままは鏡に写しても同じ");

            var twist = new AxisMapping(new ChannelAssignment(InputChannel.TwistRight), new ChannelAssignment(InputChannel.MoveUp),
                new ChannelAssignment(InputChannel.TiltForward, 0.5f));
            Assert.That(twist.Mirrored(), Is.EqualTo(twist), "右へひねる→右、前へ傾ける→前 は左右どちらでも同じ向き");
        }

        [Test]
        public void Mirrored_FlipsMixedParity()
        {
            var mapping = new AxisMapping(new ChannelAssignment(InputChannel.TiltForward, 2f), new ChannelAssignment(InputChannel.TiltRight),
                new ChannelAssignment(InputChannel.None));
            var mirrored = mapping.Mirrored();

            Assert.That(mirrored.right.scale, Is.EqualTo(-2f), "手先の右 ← 前へ傾ける：手先の右だけが逆になる");
            Assert.That(mirrored.up.scale, Is.EqualTo(-1f), "手先の上 ← 右へ傾ける：入力だけが逆になる");
            Assert.That(mirrored.forward.channel, Is.EqualTo(InputChannel.None));
            Assert.That(mirrored.leverLength, Is.EqualTo(mapping.leverLength));
            Assert.That(mirrored.Mirrored(), Is.EqualTo(mapping), "2回写すと元に戻る");
        }

        [Test]
        public void Mirrored_MovesLimbAsMirrorImage()
        {
            // 左の部位を、右の部位の動きを鏡に写したように動かすと、左の追加肢は右の追加肢の鏡像の向きに動く
            var mapping = new AxisMapping(new ChannelAssignment(InputChannel.TiltForward), new ChannelAssignment(InputChannel.TwistRight, -1f),
                new ChannelAssignment(InputChannel.TiltRight, 0.7f));
            var input = new InputChannels(new Vector3(0.03f, -0.02f, 0.05f), new Vector3(0.2f, -0.3f, 0.4f));
            var mirroredInput = new InputChannels(new Vector3(-0.03f, -0.02f, 0.05f), new Vector3(0.2f, 0.3f, -0.4f));

            var right = mapping.Apply(input);
            var left = mapping.Mirrored().Apply(mirroredInput);

            TestMath.AreClose(new Vector3(-right.x, right.y, right.z), left, 1e-6f);
        }

        // ---- 型の一覧 ----

        [Test]
        public void ForSite_RightSitePresetsAlsoServeLeftSite()
        {
            var shin = Preset("knee_shin", BodySite.RightKnee,
                new AxisMapping(new ChannelAssignment(InputChannel.TiltRight), new ChannelAssignment(InputChannel.MoveUp),
                    new ChannelAssignment(InputChannel.TiltForward)));
            var translate = Preset("translate", BodySite.Unknown, AxisMapping.Identity);
            var presets = new[] { translate, shin };

            Assert.That(MappingPresets.ForSite(presets, BodySite.LeftKnee), Is.EqualTo(new[] { shin, translate }));
            Assert.That(MappingPresets.ForSite(presets, BodySite.LeftAnkle), Is.EqualTo(new[] { translate }));
            Assert.That(MappingPresets.MappingFor(shin, BodySite.RightKnee), Is.EqualTo(shin.mapping));
            Assert.That(MappingPresets.MappingFor(shin, BodySite.LeftKnee), Is.EqualTo(shin.mapping.Mirrored()));
            Assert.That(MappingPresets.MappingFor(null, BodySite.LeftKnee), Is.EqualTo(AxisMapping.Identity));
        }

        // ---- 左の追加肢の設定 ----

        [Test]
        public void MirrorForLeft_SameAttachPointLeftSiteMirroredMapping()
        {
            var right = new LimbRuntimeConfig
            {
                attachPointId = "flank",
                inputSite = BodySite.RightElbow,
                inputSourceId = "LHR-RIGHT",
                positionGain = 3f,
                mappingPresetId = "elbow_2axis",
                axisMapping = new AxisMapping(new ChannelAssignment(InputChannel.TiltRight), new ChannelAssignment(InputChannel.TiltForward),
                    new ChannelAssignment(InputChannel.MoveForward)),
            };
            right.grasp.hand = ControllerHand.Right;
            var leftCurrent = new LimbRuntimeConfig();
            leftCurrent.grasp.hand = ControllerHand.Left;
            leftCurrent.filter.beta = 7f;

            var left = LimbSession.MirrorForLeft(right, leftCurrent);

            Assert.That(left.attachPointId, Is.EqualTo("flank"), "生やす位置は同じ（左右は追加肢の側で決まる）");
            Assert.That(left.inputSite, Is.EqualTo(BodySite.LeftElbow));
            Assert.That(left.inputSourceId, Is.Empty, "トラッカーは部位から選ぶ");
            Assert.That(left.positionGain, Is.EqualTo(3f));
            Assert.That(left.mappingPresetId, Is.EqualTo("elbow_2axis"));
            Assert.That(left.axisMapping, Is.EqualTo(right.axisMapping.Mirrored()));
            Assert.That(left.grasp.hand, Is.EqualTo(ControllerHand.Left), "把持は左のまま");
            Assert.That(left.filter.beta, Is.EqualTo(7f));

            Assert.That(right.inputSite, Is.EqualTo(BodySite.RightElbow), "右の設定は変えない");
            Assert.That(right.inputSourceId, Is.EqualTo("LHR-RIGHT"));
        }

        // ---- 当たり判定 ----

        [Test]
        public void ProbeCenter_PutsSphereFrontAtFingertip()
        {
            var fingertip = new Vector3(0.1f, 1.2f, 0.5f);
            var direction = new Vector3(0f, 0f, 2f);
            const float radius = 0.01f;

            var center = ReachTask.FingertipProbeCenter(fingertip, direction, radius);

            TestMath.AreClose(new Vector3(0.1f, 1.2f, 0.49f), center, 1e-6f, "指の中（指先から半径だけ手前）");
            TestMath.AreClose(fingertip, center + direction.normalized * radius, 1e-6f, "球の前の端が指先");
        }

        [Test]
        public void HandProbeCenter_IsAtNaturalFingertip_LikeExtraLimb()
        {
            var hand = new GameObject("RightHand").transform;
            try
            {
                hand.position = new Vector3(0.3f, 1.1f, 0.3f);
                var distal = new GameObject("RightHandMiddle3").transform;
                distal.SetParent(hand, false);
                distal.position = new Vector3(0.3f, 1.1f, 0.45f);
                var tip = new GameObject("RightHandMiddle4").transform;
                tip.SetParent(distal, false);
                tip.position = new Vector3(0.3f, 1.1f, 0.47f);

                var center = ReachTask.HandProbeCenter(hand, distal, 0.01f);
                TestMath.AreClose(new Vector3(0.3f, 1.1f, 0.46f), center, 1e-6f, "見た目の中指の先から半径だけ手前（追加肢と同じ）");
                TestMath.AreClose(hand.position, ReachTask.HandProbeCenter(hand, null, 0.01f), 1e-6f, "指の骨がなければ手首");
            }
            finally
            {
                Object.DestroyImmediate(hand.gameObject);
            }
        }
    }
}
