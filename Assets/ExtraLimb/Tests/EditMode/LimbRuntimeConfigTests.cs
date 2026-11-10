using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>実行中の設定（Step 1-1）。</summary>
    public class LimbRuntimeConfigTests
    {
        static readonly Vector3 Sample = new Vector3(1f, 2f, 3f);

        [Test]
        public void Defaults_AreUsable()
        {
            var config = new LimbRuntimeConfig();

            Assert.That(config.mode, Is.EqualTo(ControlModeKind.Position));
            Assert.That(config.positionGain, Is.GreaterThan(0f));
            Assert.That(config.velocityGain, Is.GreaterThan(0f));
            Assert.That(config.hybridRadiusCm, Is.GreaterThan(config.deadzoneCm));
            Assert.That(config.axisMapping.HasDistinctChannels, Is.True);
            TestMath.AreClose(new Vector3(1f, 2f, 3f), config.axisMapping.Apply(new Vector3(1f, 2f, 3f)));
            Assert.That(config.axisMapping.leverLength, Is.GreaterThan(0f));
            Assert.That(config.mappingPresetId, Is.Empty);
            Assert.That(config.grasp.input, Is.EqualTo(GraspInput.ControllerOrKey));
            Assert.That(config.grasp.hand, Is.EqualTo(ControllerHand.Right));
            Assert.That(config.grasp.pressThreshold, Is.GreaterThan(config.grasp.releaseThreshold));
        }

        [Test]
        public void CentimetersAreConvertedToMeters()
        {
            var config = new LimbRuntimeConfig { deadzoneCm = 2f, hybridRadiusCm = 8f };

            Assert.That(config.DeadzoneMeters, Is.EqualTo(0.02f).Within(1e-6f));
            Assert.That(config.HybridRadiusMeters, Is.EqualTo(0.08f).Within(1e-6f));
        }

        [Test]
        public void JsonRoundTrip_KeepsValues()
        {
            var config = new LimbRuntimeConfig
            {
                attachPointId = "flank_right",
                inputSourceId = "LHR-8A48D912",
                referenceSourceId = "LHR-19F8C7B3",
                mode = ControlModeKind.Hybrid,
                positionGain = 1.7f,
                velocityGain = 2.5f,
                deadzoneCm = 1.5f,
                hybridRadiusCm = 6f,
                mappingPresetId = "shoulder_twist",
                axisMapping = new AxisMapping(
                    new ChannelAssignment(InputChannel.TwistRight),
                    new ChannelAssignment(InputChannel.MoveUp, -0.5f),
                    new ChannelAssignment(InputChannel.None, 0f),
                    0.25f),
            };
            config.filter.beta = 7f;
            config.grasp.input = GraspInput.Key;
            config.grasp.hand = ControllerHand.Left;
            config.grasp.pressThreshold = 0.7f;

            var restored = LimbRuntimeConfig.FromJson(config.ToJson());

            Assert.That(restored.ToJson(), Is.EqualTo(config.ToJson()));
            Assert.That(restored.mode, Is.EqualTo(ControlModeKind.Hybrid));
            Assert.That(restored.mappingPresetId, Is.EqualTo("shoulder_twist"));
            Assert.That(restored.axisMapping.right.channel, Is.EqualTo(InputChannel.TwistRight));
            Assert.That(restored.axisMapping.up.scale, Is.EqualTo(-0.5f));
            Assert.That(restored.axisMapping.forward.IsUsed, Is.False);
            Assert.That(restored.axisMapping.leverLength, Is.EqualTo(0.25f));
            Assert.That(restored.filter.beta, Is.EqualTo(7f));
            Assert.That(restored.grasp.input, Is.EqualTo(GraspInput.Key));
            Assert.That(restored.grasp.hand, Is.EqualTo(ControllerHand.Left));
            Assert.That(restored.grasp.pressThreshold, Is.EqualTo(0.7f));
        }

        [Test]
        public void FromJson_MissingFieldsKeepDefaults()
        {
            var restored = LimbRuntimeConfig.FromJson("{\"mode\":1}");

            Assert.That(restored.mode, Is.EqualTo(ControlModeKind.Rate));
            Assert.That(restored.positionGain, Is.EqualTo(new LimbRuntimeConfig().positionGain));
            Assert.That(restored.axisMapping.HasDistinctChannels, Is.True);
            TestMath.AreClose(Sample, restored.axisMapping.Apply(Sample));
        }

        [Test]
        public void Clone_IsIndependentCopy()
        {
            var original = new LimbRuntimeConfig();

            var clone = original.Clone();
            clone.filter.beta = 99f;
            clone.grasp.pressThreshold = 99f;
            clone.positionGain = 99f;

            Assert.That(original.filter.beta, Is.Not.EqualTo(99f));
            Assert.That(original.grasp.pressThreshold, Is.Not.EqualTo(99f));
            Assert.That(original.positionGain, Is.Not.EqualTo(99f));
        }
    }
}
