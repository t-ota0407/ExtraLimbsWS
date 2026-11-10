using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>腰トラッカーを使わず、部屋を基準にしたときの計算（Step 3-2、決定事項#9）。</summary>
    public class RoomReferenceTests
    {
        static readonly Pose Room = new Pose(Vector3.zero, Quaternion.identity);
        static readonly Vector3 KneeNeutral = new Vector3(0.6f, 0.55f, -0.4f); // 部屋の中の右膝（着座）
        static readonly Vector3 BasePosition = new Vector3(0.12f, -0.06f, 0.30f);

        static Pose Knee(Vector3 roomDisplacement) => new Pose(KneeNeutral + roomDisplacement, Quaternion.Euler(-80f, 15f, 5f));

        static LimbControlPipeline CreatePipeline()
        {
            var config = new LimbRuntimeConfig { positionGain = 2f };
            config.filter.enabled = false;
            var pipeline = new LimbControlPipeline(config);
            pipeline.SetBasePosition(BasePosition);
            return pipeline;
        }

        [Test]
        public void DisplacementIsMovementInRoom()
        {
            var pipeline = CreatePipeline();
            pipeline.Recenter(Room, Knee(Vector3.zero));

            var target = pipeline.Step(Room, Knee(new Vector3(0f, 0.04f, 0.05f)), 1f / 90f);

            TestMath.AreClose(new Vector3(0f, 0.04f, 0.05f), pipeline.LastDisplacement);
            TestMath.AreClose(BasePosition + new Vector3(0f, 0.08f, 0.10f), target);
        }

        [Test]
        public void TrackerRotationAlone_DoesNotMoveTarget()
        {
            // 部屋基準では位置だけを見る（トラッカーの向きが変わっても変位は0）
            var pipeline = CreatePipeline();
            pipeline.Recenter(Room, Knee(Vector3.zero));

            var twisted = new Pose(KneeNeutral, Quaternion.Euler(-60f, 40f, -20f));
            var target = pipeline.Step(Room, twisted, 1f / 90f);

            TestMath.AreClose(Vector3.zero, pipeline.LastDisplacement);
            TestMath.AreClose(BasePosition, target);
        }

        [Test]
        public void RecordedForward_MapsRoomDirectionToBodyForward()
        {
            // 参加者が部屋の +X を向いて座っている
            var pipeline = CreatePipeline();
            pipeline.Recenter(Room, Knee(Vector3.zero));
            Assert.That(pipeline.TryRecordForward(Room, Knee(new Vector3(0.10f, 0.01f, 0f)), 0.05f), Is.True);
            pipeline.Recenter(Room, Knee(Vector3.zero));

            pipeline.Step(Room, Knee(new Vector3(0.05f, 0f, 0f)), 1f / 90f);
            TestMath.AreClose(new Vector3(0f, 0f, 0.05f), pipeline.LastDisplacement);

            // 体の右＝部屋の −Z
            pipeline.Step(Room, Knee(new Vector3(0f, 0f, -0.05f)), 1f / 90f);
            TestMath.AreClose(new Vector3(0.05f, 0f, 0f), pipeline.LastDisplacement);
        }

        [Test]
        public void SavedForward_RestoredAfterClear()
        {
            // 保存した前の向き（TrackerBindingProfile）を、次の再生で読み直したときと同じ結果になる
            var recorded = CreatePipeline();
            recorded.Recenter(Room, Knee(Vector3.zero));
            recorded.TryRecordForward(Room, Knee(new Vector3(-0.07f, 0f, 0.07f)), 0.05f);
            var profile = new TrackerBindingProfile();
            profile.SetForward("room:SteamVR", recorded.BodyFrame.Forward);

            var restored = CreatePipeline();
            restored.Recenter(Room, Knee(Vector3.zero));
            Assert.That(profile.TryGetForward("room:SteamVR", out var forward), Is.True);
            Assert.That(restored.BodyFrame.TrySetForward(forward, 0.01f), Is.True);

            var move = new Vector3(0.02f, 0.03f, -0.04f);
            recorded.Step(Room, Knee(move), 1f / 90f);
            restored.Step(Room, Knee(move), 1f / 90f);
            TestMath.AreClose(recorded.LastDisplacement, restored.LastDisplacement);
        }
    }
}
