using System;
using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Tracking
{
    /// <summary>コードやキーボードから姿勢を設定する仮想トラッカー。HMDやトラッカーなしで開発するために使う。</summary>
    public sealed class MockPoseSource : ITrackedPoseSource
    {
        public string Id { get; }
        public BodySite Site { get; set; }
        public PoseSpace Space => PoseSpace.Virtual;

        /// <summary>今の姿勢（ワールド座標）。</summary>
        public Pose Pose { get; set; }

        /// <summary>false にすると、追跡が切れた状態をまねる。</summary>
        public bool IsTracking { get; set; } = true;

        public MockPoseSource(string id, BodySite site, Pose pose)
        {
            Id = id;
            Site = site;
            Pose = pose;
        }

        public static string IdFor(BodySite site) => "mock:" + site;

        public bool TryGetPose(out Pose pose)
        {
            pose = Pose;
            return IsTracking;
        }
    }

    /// <summary>仮想の体に付ける仮想トラッカー1台の初期配置。位置と向きは腰から見た値。</summary>
    [Serializable]
    public class MockTrackerSpec
    {
        public BodySite site = BodySite.RightAnkle;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;

        public MockTrackerSpec()
        {
        }

        public MockTrackerSpec(BodySite site, Vector3 localPosition, Vector3 localEulerAngles = default)
        {
            this.site = site;
            this.localPosition = localPosition;
            this.localEulerAngles = localEulerAngles;
        }

        public Pose LocalPose => new Pose(localPosition, Quaternion.Euler(localEulerAngles));
    }

    /// <summary>
    /// 腰の仮想トラッカーと、腰に対する各部位の仮想トラッカーをまとめて動かす仮想の体。
    /// 部位は腰の座標で動かし、体全体はその場で向きを変えられる（変位が出ないことの確認用）。
    /// </summary>
    public sealed class MockBody
    {
        readonly Pose waistStart;
        readonly List<MockPoseSource> trackers = new List<MockPoseSource>();
        readonly List<Pose> startLocalPoses = new List<Pose>();
        readonly List<Pose> localPoses = new List<Pose>();
        float bodyYaw;

        public MockPoseSource Waist { get; }

        /// <summary>腰以外の仮想トラッカー。</summary>
        public IReadOnlyList<MockPoseSource> Trackers => trackers;

        /// <summary>体全体の向き（度、鉛直軸まわり）。</summary>
        public float BodyYaw => bodyYaw;

        public MockBody(Pose waist, IEnumerable<MockTrackerSpec> specs)
        {
            waistStart = waist;
            Waist = new MockPoseSource(MockPoseSource.IdFor(BodySite.Waist), BodySite.Waist, waist);
            foreach (var spec in specs)
            {
                trackers.Add(new MockPoseSource(MockPoseSource.IdFor(spec.site), spec.site, default));
                startLocalPoses.Add(spec.LocalPose);
                localPoses.Add(spec.LocalPose);
            }
            Apply();
        }

        /// <summary>腰から見た姿勢。</summary>
        public Pose GetLocalPose(int index) => localPoses[index];

        /// <summary>腰の座標で平行移動する（z=前、x=右、y=上）。</summary>
        public void Move(int index, Vector3 localDelta)
        {
            var local = localPoses[index];
            localPoses[index] = new Pose(local.position + localDelta, local.rotation);
            Apply();
        }

        /// <summary>トラッカー自身の局所Z軸まわりにひねる。</summary>
        public void Twist(int index, float degrees)
        {
            var local = localPoses[index];
            localPoses[index] = new Pose(local.position, local.rotation * Quaternion.AngleAxis(degrees, Vector3.forward));
            Apply();
        }

        /// <summary>体全体を腰の位置で鉛直軸まわりに回す。</summary>
        public void Turn(float degrees)
        {
            bodyYaw = Mathf.Repeat(bodyYaw + degrees, 360f);
            Apply();
        }

        /// <summary>初期配置に戻す。</summary>
        public void Reset()
        {
            bodyYaw = 0f;
            for (int i = 0; i < localPoses.Count; i++) localPoses[i] = startLocalPoses[i];
            Apply();
        }

        void Apply()
        {
            var waistRotation = Quaternion.AngleAxis(bodyYaw, Vector3.up) * waistStart.rotation;
            var waistPosition = waistStart.position;
            Waist.Pose = new Pose(waistPosition, waistRotation);
            for (int i = 0; i < trackers.Count; i++)
            {
                var local = localPoses[i];
                trackers[i].Pose = new Pose(waistPosition + waistRotation * local.position, waistRotation * local.rotation);
            }
        }
    }
}
