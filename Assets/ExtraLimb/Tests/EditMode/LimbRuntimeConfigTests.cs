using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>実行中の設定（Step 1-1）。</summary>
    public class LimbRuntimeConfigTests
    {
        [Test]
        public void Defaults_AreUsable()
        {
            var config = new LimbRuntimeConfig();

            Assert.That(config.mode, Is.EqualTo(ControlModeKind.Position));
            Assert.That(config.positionGain, Is.GreaterThan(0f));
            Assert.That(config.velocityGain, Is.GreaterThan(0f));
            Assert.That(config.hybridRadiusCm, Is.GreaterThan(config.deadzoneCm));
            Assert.That(config.axisMapping.IsPermutation, Is.True);
            TestMath.AreClose(new Vector3(1f, 2f, 3f), config.axisMapping.Apply(new Vector3(1f, 2f, 3f)));
            Assert.That(config.grasp.onAngleDeg, Is.GreaterThan(config.grasp.offAngleDeg));
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
                axisMapping = new AxisMapping(SignedAxis.NegZ, SignedAxis.PosX, SignedAxis.NegY, new Vector3(1f, 0.5f, 2f)),
            };
            config.filter.beta = 7f;
            config.grasp.kind = GraspTriggerKind.Rotation;
            config.grasp.twistAxis = SignedAxis.NegY;

            var restored = LimbRuntimeConfig.FromJson(config.ToJson());

            Assert.That(restored.ToJson(), Is.EqualTo(config.ToJson()));
            Assert.That(restored.mode, Is.EqualTo(ControlModeKind.Hybrid));
            Assert.That(restored.axisMapping.x, Is.EqualTo(SignedAxis.NegZ));
            TestMath.AreClose(new Vector3(1f, 0.5f, 2f), restored.axisMapping.scale);
            Assert.That(restored.filter.beta, Is.EqualTo(7f));
            Assert.That(restored.grasp.twistAxis, Is.EqualTo(SignedAxis.NegY));
        }

        [Test]
        public void FromJson_MissingFieldsKeepDefaults()
        {
            var restored = LimbRuntimeConfig.FromJson("{\"mode\":1}");

            Assert.That(restored.mode, Is.EqualTo(ControlModeKind.Rate));
            Assert.That(restored.positionGain, Is.EqualTo(new LimbRuntimeConfig().positionGain));
            Assert.That(restored.axisMapping.IsPermutation, Is.True);
            TestMath.AreClose(Vector3.one, restored.axisMapping.scale);
        }

        [Test]
        public void Clone_IsIndependentCopy()
        {
            var original = new LimbRuntimeConfig();

            var clone = original.Clone();
            clone.filter.beta = 99f;
            clone.grasp.onAngleDeg = 99f;
            clone.positionGain = 99f;

            Assert.That(original.filter.beta, Is.Not.EqualTo(99f));
            Assert.That(original.grasp.onAngleDeg, Is.Not.EqualTo(99f));
            Assert.That(original.positionGain, Is.Not.EqualTo(99f));
        }
    }
}
