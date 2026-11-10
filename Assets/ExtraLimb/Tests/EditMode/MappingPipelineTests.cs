using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>回転を使うマッピングの型で、入力の姿勢から手先のターゲットまで（Step 3-3）。</summary>
    public class MappingPipelineTests
    {
        static readonly Pose Room = new Pose(Vector3.zero, Quaternion.identity);
        static readonly Vector3 ShoulderPosition = new Vector3(0.2f, 1.25f, -0.4f);
        static readonly Vector3 BasePosition = new Vector3(0.12f, -0.06f, 0.30f);
        const float Lever = 0.3f;

        // トラッカーの付け方（向き）は人や日で変わる
        static readonly Quaternion Mounting = Quaternion.Euler(-70f, 35f, 120f);

        /// <summary>肩のトラッカー：部屋の中で rotation だけ回し、displacement だけ動かした姿勢。</summary>
        static Pose Shoulder(Quaternion rotation, Vector3 displacement) => new Pose(ShoulderPosition + displacement, rotation * Mounting);

        static LimbControlPipeline CreatePipeline(AxisMapping mapping, float gain = 2f)
        {
            var config = new LimbRuntimeConfig { positionGain = gain, axisMapping = mapping };
            config.filter.enabled = false;
            var pipeline = new LimbControlPipeline(config);
            pipeline.SetBasePosition(BasePosition);
            return pipeline;
        }

        static AxisMapping ShoulderTwist => new AxisMapping(
            new ChannelAssignment(InputChannel.TwistRight),
            new ChannelAssignment(InputChannel.MoveUp),
            new ChannelAssignment(InputChannel.MoveForward),
            Lever);

        [Test]
        public void TwistRight_MovesHandRight_RegardlessOfMounting()
        {
            var pipeline = CreatePipeline(ShoulderTwist);
            pipeline.Recenter(Room, Shoulder(Quaternion.identity, Vector3.zero));

            var target = pipeline.Step(Room, Shoulder(Quaternion.AngleAxis(15f, Vector3.up), Vector3.zero), 1f / 90f);

            float expected = Lever * 15f * Mathf.Deg2Rad;
            Assert.That(pipeline.LastChannels.Raw(InputChannel.TwistRight), Is.EqualTo(15f * Mathf.Deg2Rad).Within(1e-4f));
            TestMath.AreClose(new Vector3(expected, 0f, 0f), pipeline.LastDisplacement);
            TestMath.AreClose(BasePosition + new Vector3(2f * expected, 0f, 0f), target);
        }

        [Test]
        public void TranslationAndRotationCombine()
        {
            var pipeline = CreatePipeline(ShoulderTwist);
            pipeline.Recenter(Room, Shoulder(Quaternion.identity, Vector3.zero));

            // 肩をすくめて前へ出しながら、左へ10°ひねる
            pipeline.Step(Room, Shoulder(Quaternion.AngleAxis(-10f, Vector3.up), new Vector3(0f, 0.03f, 0.04f)), 1f / 90f);

            TestMath.AreClose(new Vector3(-Lever * 10f * Mathf.Deg2Rad, 0.03f, 0.04f), pipeline.LastDisplacement);
        }

        [Test]
        public void TiltIsMeasuredInBodyAxes_AfterRecordingForward()
        {
            // 参加者が部屋の +X を向いて座っている。体の前＝+X、体の右＝−Z
            var mapping = new AxisMapping(
                new ChannelAssignment(InputChannel.MoveRight),
                new ChannelAssignment(InputChannel.MoveUp),
                new ChannelAssignment(InputChannel.TiltForward),
                Lever);
            var pipeline = CreatePipeline(mapping);
            pipeline.Recenter(Room, Shoulder(Quaternion.identity, Vector3.zero));
            Assert.That(pipeline.TryRecordForward(Room, Shoulder(Quaternion.identity, new Vector3(0.1f, 0f, 0f)), 0.05f), Is.True);
            pipeline.Recenter(Room, Shoulder(Quaternion.identity, Vector3.zero));

            // 上の端を体の前（+X）へ20°倒す
            float r = 20f * Mathf.Deg2Rad;
            var tilt = Quaternion.FromToRotation(Vector3.up, new Vector3(Mathf.Sin(r), Mathf.Cos(r), 0f));
            pipeline.Step(Room, Shoulder(tilt, Vector3.zero), 1f / 90f);

            Assert.That(pipeline.LastChannels.Raw(InputChannel.TiltForward), Is.EqualTo(r).Within(1e-3f));
            Assert.That(pipeline.LastChannels.Raw(InputChannel.TiltRight), Is.EqualTo(0f).Within(1e-3f));
            TestMath.AreClose(new Vector3(0f, 0f, Lever * r), pipeline.LastDisplacement);
        }

        [Test]
        public void UnusedAxis_StaysAtBase()
        {
            // 肘：前後は使わない
            var mapping = new AxisMapping(
                new ChannelAssignment(InputChannel.MoveRight),
                new ChannelAssignment(InputChannel.MoveUp),
                new ChannelAssignment(InputChannel.None, 0f),
                Lever);
            var pipeline = CreatePipeline(mapping, gain: 3f);
            pipeline.Recenter(Room, Shoulder(Quaternion.identity, Vector3.zero));

            var target = pipeline.Step(Room, Shoulder(Quaternion.identity, new Vector3(0.02f, 0.01f, 0.05f)), 1f / 90f);

            TestMath.AreClose(BasePosition + new Vector3(0.06f, 0.03f, 0f), target);
        }

        [Test]
        public void Recenter_ClearsChannels()
        {
            var pipeline = CreatePipeline(ShoulderTwist);
            pipeline.Recenter(Room, Shoulder(Quaternion.identity, Vector3.zero));
            pipeline.Step(Room, Shoulder(Quaternion.AngleAxis(15f, Vector3.up), Vector3.zero), 1f / 90f);

            pipeline.Recenter(Room, Shoulder(Quaternion.AngleAxis(15f, Vector3.up), Vector3.zero));

            TestMath.AreClose(Vector3.zero, pipeline.LastChannels.rotation);
            pipeline.Step(Room, Shoulder(Quaternion.AngleAxis(15f, Vector3.up), Vector3.zero), 1f / 90f);
            TestMath.AreClose(Vector3.zero, pipeline.LastDisplacement);
        }
    }
}
