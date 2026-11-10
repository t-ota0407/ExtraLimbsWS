using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>マッピングの型：入力の6つの値と、手先の3軸への割り当て（Step 3-3。Step 1-1 の軸の割り当てを広げたもの）。</summary>
    public class AxisMappingTests
    {
        static readonly Vector3 Sample = new Vector3(1f, 2f, 3f);
        const float Angle = 20f;

        static InputChannels Rotated(Quaternion rotation) => new InputChannels(Vector3.zero, InputChannels.RotationVector(rotation));

        static AxisMapping Mapping(InputChannel right, InputChannel up, InputChannel forward, float lever = 0.3f) =>
            new AxisMapping(new ChannelAssignment(right), new ChannelAssignment(up), new ChannelAssignment(forward), lever);

        [Test]
        public void Identity_KeepsTranslation()
        {
            TestMath.AreClose(Sample, AxisMapping.Identity.Apply(Sample));
            Assert.That(AxisMapping.Identity.HasDistinctChannels, Is.True);
            Assert.That(AxisMapping.Identity.UsesRotation, Is.False);
        }

        [Test]
        public void SwapAxes()
        {
            var mapping = Mapping(InputChannel.MoveForward, InputChannel.MoveUp, InputChannel.MoveRight);

            TestMath.AreClose(new Vector3(3f, 2f, 1f), mapping.Apply(Sample));
        }

        [Test]
        public void RotationVector_IsAxisTimesAngle()
        {
            var axis = new Vector3(1f, 2f, -0.5f).normalized;

            TestMath.AreClose(axis * (30f * Mathf.Deg2Rad), InputChannels.RotationVector(Quaternion.AngleAxis(30f, axis)));
            TestMath.AreClose(Vector3.zero, InputChannels.RotationVector(Quaternion.identity));
            // 270° は逆向きの 90° として扱う
            TestMath.AreClose(Vector3.up * (-90f * Mathf.Deg2Rad), InputChannels.RotationVector(Quaternion.AngleAxis(270f, Vector3.up)));
        }

        [Test]
        public void TiltForward_IsPositive_WhenTopTiltsForward()
        {
            float r = Angle * Mathf.Deg2Rad;
            var c = Rotated(Quaternion.FromToRotation(Vector3.up, new Vector3(0f, Mathf.Cos(r), Mathf.Sin(r))));

            Assert.That(c.Raw(InputChannel.TiltForward), Is.EqualTo(r).Within(1e-4f));
            Assert.That(c.Raw(InputChannel.TiltRight), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(c.Raw(InputChannel.TwistRight), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void TiltRight_IsPositive_WhenTopTiltsRight()
        {
            float r = Angle * Mathf.Deg2Rad;
            var c = Rotated(Quaternion.FromToRotation(Vector3.up, new Vector3(Mathf.Sin(r), Mathf.Cos(r), 0f)));

            Assert.That(c.Raw(InputChannel.TiltRight), Is.EqualTo(r).Within(1e-4f));
            Assert.That(c.Raw(InputChannel.TiltForward), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(c.Raw(InputChannel.TwistRight), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void TwistRight_IsPositive_WhenFrontTurnsRight()
        {
            float r = Angle * Mathf.Deg2Rad;
            var c = Rotated(Quaternion.FromToRotation(Vector3.forward, new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r))));

            Assert.That(c.Raw(InputChannel.TwistRight), Is.EqualTo(r).Within(1e-4f));
            Assert.That(c.Raw(InputChannel.TiltForward), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(c.Raw(InputChannel.TiltRight), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Rotation_IsConvertedToLengthWithLever()
        {
            // 上体を右へ10°ひねる → 手先の右へ 0.3 m × 10°（rad）
            var mapping = Mapping(InputChannel.TwistRight, InputChannel.MoveUp, InputChannel.MoveForward, lever: 0.3f);
            var c = new InputChannels(new Vector3(0.5f, 0.02f, 0.03f), Vector3.up * (10f * Mathf.Deg2Rad));

            TestMath.AreClose(new Vector3(0.3f * 10f * Mathf.Deg2Rad, 0.02f, 0.03f), mapping.Apply(c));
            Assert.That(mapping.UsesRotation, Is.True);
            Assert.That(mapping.Uses(InputChannel.TwistRight), Is.True);
            Assert.That(mapping.Uses(InputChannel.MoveRight), Is.False);
        }

        [Test]
        public void Invert_FlipsSignAndBack()
        {
            var inverted = AxisMapping.Identity.WithInverted(1);

            TestMath.AreClose(new Vector3(1f, -2f, 3f), inverted.Apply(Sample));
            Assert.That(inverted.IsInverted(1), Is.True);
            Assert.That(inverted.IsInverted(0), Is.False);
            Assert.That(inverted.DescribeAxis(1), Is.EqualTo("下へ動かす"));
            TestMath.AreClose(Sample, inverted.WithInverted(1).Apply(Sample));
        }

        [Test]
        public void NoneOrZeroScale_DisablesAxis()
        {
            var mapping = new AxisMapping(
                new ChannelAssignment(InputChannel.MoveRight, 2f),
                new ChannelAssignment(InputChannel.MoveUp, 0f),
                new ChannelAssignment(InputChannel.None));

            TestMath.AreClose(new Vector3(2f, 0f, 0f), mapping.Apply(Sample));
            Assert.That(mapping[1].IsUsed, Is.False);
            Assert.That(mapping.DescribeAxis(2), Is.EqualTo("使わない"));
            // 使わない軸どうしは重なってもよい
            Assert.That(mapping.HasDistinctChannels, Is.True);
        }

        [Test]
        public void SameChannelTwice_IsNotDistinct()
        {
            var mapping = Mapping(InputChannel.MoveRight, InputChannel.MoveRight, InputChannel.MoveForward);

            Assert.That(mapping.HasDistinctChannels, Is.False);
        }

        [Test]
        public void Describe_UsesOppositeWordsForNegativeSign()
        {
            Assert.That(InputChannelInfo.Describe(InputChannel.TwistRight), Is.EqualTo("右へひねる"));
            Assert.That(InputChannelInfo.Describe(InputChannel.TwistRight, -1f), Is.EqualTo("左へひねる"));
            Assert.That(InputChannelInfo.Describe(InputChannel.TiltForward, -1f), Is.EqualTo("後ろへ傾ける"));
            Assert.That(InputChannelInfo.All.Length, Is.EqualTo(6));
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
