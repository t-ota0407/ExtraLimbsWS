using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>身長・肩幅からの拡縮（Step 3-1）。</summary>
    public class BodyCalibrationTests
    {
        const float ModelHeight = 1.8f;
        const float ModelShoulder = 0.376f;

        [Test]
        public void BeforeCalibration_NoScaling()
        {
            var calibration = new BodyCalibration(ModelHeight, ModelShoulder);

            Assert.That(calibration.IsCalibrated, Is.False);
            Assert.That(calibration.AvatarScale, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(calibration.LateralRatio, Is.EqualTo(1f).Within(1e-6f));
            TestMath.AreClose(new Vector3(0.149f, 0.183f, -0.003f), calibration.ScaleOffset(new Vector3(0.149f, 0.183f, -0.003f)));
        }

        [Test]
        public void ShorterPerson_WithSameProportions_ScalesUniformly()
        {
            var calibration = new BodyCalibration(ModelHeight, ModelShoulder);
            float ratio = 1.6f / ModelHeight;

            Assert.That(calibration.TrySet(1.6f, ModelShoulder * ratio), Is.True);

            Assert.That(calibration.AvatarScale, Is.EqualTo(ratio).Within(1e-5f));
            Assert.That(calibration.LateralRatio, Is.EqualTo(1f).Within(1e-5f));
            TestMath.AreClose(new Vector3(0.1f, 0.2f, 0.3f) * ratio, calibration.ScaleOffset(new Vector3(0.1f, 0.2f, 0.3f)));
        }

        [Test]
        public void WiderShoulders_ScaleOnlySideways()
        {
            var calibration = new BodyCalibration(ModelHeight, ModelShoulder);

            calibration.TrySet(ModelHeight, ModelShoulder * 1.1f);

            var scaled = calibration.ScaleOffset(new Vector3(0.1f, 0.2f, 0.3f));
            Assert.That(scaled.x, Is.EqualTo(0.11f).Within(1e-5f));
            Assert.That(scaled.y, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(scaled.z, Is.EqualTo(0.3f).Within(1e-5f));
        }

        [TestCase(0.5f, 0.38f)]
        [TestCase(2.5f, 0.38f)]
        [TestCase(1.7f, 0.1f)]
        [TestCase(1.7f, 0.9f)]
        [TestCase(0f, 0f)]
        public void OutOfRange_IsRejectedAndKeepsPrevious(float height, float shoulder)
        {
            var calibration = new BodyCalibration(ModelHeight, ModelShoulder);
            calibration.TrySet(1.7f, 0.4f);

            Assert.That(calibration.TrySet(height, shoulder), Is.False);
            Assert.That(calibration.Height, Is.EqualTo(1.7f));
            Assert.That(calibration.ShoulderWidth, Is.EqualTo(0.4f));
        }

        [Test]
        public void Reset_ReturnsToModel()
        {
            var calibration = new BodyCalibration(ModelHeight, ModelShoulder);
            calibration.TrySet(1.6f, 0.35f);

            calibration.Reset();

            Assert.That(calibration.IsCalibrated, Is.False);
            Assert.That(calibration.AvatarScale, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void HandLength_Default18cm_RangeChecked_AndResetRestoresDefault()
        {
            var calibration = new BodyCalibration(ModelHeight, ModelShoulder);
            Assert.That(calibration.HandLength, Is.EqualTo(0.18f).Within(1e-6f), "初期値は18cm");
            Assert.That(calibration.TrySetHandLength(0.165f), Is.True);
            Assert.That(calibration.HandLength, Is.EqualTo(0.165f).Within(1e-6f));
            Assert.That(calibration.TrySetHandLength(0.10f), Is.False, "短すぎる");
            Assert.That(calibration.TrySetHandLength(0.30f), Is.False, "長すぎる");
            Assert.That(calibration.HandLength, Is.EqualTo(0.165f).Within(1e-6f), "範囲外は前の値のまま");
            Assert.That(calibration.TrySet(1.7f, 0.4f), Is.True);
            Assert.That(calibration.HandLength, Is.EqualTo(0.165f).Within(1e-6f), "身長・肩幅を入れても手の長さは変わらない");

            Assert.That(calibration.TrySet(1.6f, 0.38f, 0.30f), Is.False, "3つのうち手の長さが範囲外");
            Assert.That(calibration.Height, Is.EqualTo(1.7f).Within(1e-6f), "どれかが範囲外なら、ほかも変えない");
            Assert.That(calibration.TrySet(1.6f, 0.38f, 0.17f), Is.True);
            Assert.That(calibration.Height, Is.EqualTo(1.6f).Within(1e-6f));
            Assert.That(calibration.HandLength, Is.EqualTo(0.17f).Within(1e-6f));

            calibration.Reset();
            Assert.That(calibration.HandLength, Is.EqualTo(BodyCalibration.DefaultHandLength).Within(1e-6f));
        }

        [Test]
        public void HandBoneScale_IsTargetOverUnscaledLength()
        {
            var calibration = new BodyCalibration(ModelHeight, ModelShoulder);
            Assert.That(calibration.TrySetHandLength(0.18f), Is.True);
            Assert.That(calibration.HandBoneScale(0.24f), Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(calibration.HandBoneScale(0f), Is.EqualTo(1f), "長さが分からなければ拡縮しない");
        }
}
}
