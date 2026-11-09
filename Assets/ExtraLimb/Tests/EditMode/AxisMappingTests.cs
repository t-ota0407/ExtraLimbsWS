using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>軸の入替・反転・倍率（Step 1-1）。</summary>
    public class AxisMappingTests
    {
        static readonly Vector3 Sample = new Vector3(1f, 2f, 3f);

        [Test]
        public void Identity_KeepsVector()
        {
            TestMath.AreClose(Sample, AxisMapping.Identity.Apply(Sample));
            Assert.That(AxisMapping.Identity.IsPermutation, Is.True);
        }

        [Test]
        public void SwapXAndZ()
        {
            var mapping = new AxisMapping(SignedAxis.PosZ, SignedAxis.PosY, SignedAxis.PosX);

            TestMath.AreClose(new Vector3(3f, 2f, 1f), mapping.Apply(Sample));
        }

        [Test]
        public void InvertY()
        {
            var mapping = new AxisMapping(SignedAxis.PosX, SignedAxis.NegY, SignedAxis.PosZ);

            TestMath.AreClose(new Vector3(1f, -2f, 3f), mapping.Apply(Sample));
        }

        [Test]
        public void SwapAndInvertTogether()
        {
            // 出力x←入力の-Z、出力y←入力の+X、出力z←入力の-Y
            var mapping = new AxisMapping(SignedAxis.NegZ, SignedAxis.PosX, SignedAxis.NegY);

            TestMath.AreClose(new Vector3(-3f, 1f, -2f), mapping.Apply(Sample));
            Assert.That(mapping.IsPermutation, Is.True);
        }

        [Test]
        public void FootForwardToHandUp()
        {
            // 足首を前に出すと、追加肢の手先が上がる割り当て（出力y←入力の+Z、出力z←入力の-Y）
            var mapping = new AxisMapping(SignedAxis.PosX, SignedAxis.PosZ, SignedAxis.NegY);

            TestMath.AreClose(new Vector3(0f, 0.1f, 0f), mapping.Apply(new Vector3(0f, 0f, 0.1f)));
        }

        [Test]
        public void ScalePerAxis_ZeroDisablesAxis()
        {
            var mapping = new AxisMapping(SignedAxis.PosX, SignedAxis.PosY, SignedAxis.PosZ, new Vector3(2f, 0f, 0.5f));

            TestMath.AreClose(new Vector3(2f, 0f, 1.5f), mapping.Apply(Sample));
        }

        [Test]
        public void SameInputAxisTwice_IsNotPermutation()
        {
            var mapping = new AxisMapping(SignedAxis.PosX, SignedAxis.NegX, SignedAxis.PosZ);

            Assert.That(mapping.IsPermutation, Is.False);
        }

        [Test]
        public void SignedAxisHelpers()
        {
            Assert.That(SignedAxis.NegY.Index(), Is.EqualTo(1));
            Assert.That(SignedAxis.NegY.Sign(), Is.EqualTo(-1f));
            Assert.That(SignedAxis.PosZ.Sign(), Is.EqualTo(1f));
            TestMath.AreClose(Vector3.left, SignedAxis.NegX.ToVector());
        }
    }
}
