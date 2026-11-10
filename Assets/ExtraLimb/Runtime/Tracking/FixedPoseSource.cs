using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// 動かない基準（部屋）。その空間の原点にあり、いつも追跡できているものとして扱う。
    /// 変位の基準にすると、入力のトラッカーが Re-center 時から部屋の中でどれだけ動いたかが変位になる。
    /// 着座・固定椅子なら体はほぼ動かないので、腰トラッカーの代わりになる（計画書の決定事項#9）。
    /// </summary>
    public sealed class FixedPoseSource : ITrackedPoseSource
    {
        public string Id { get; }
        public BodySite Site => BodySite.Unknown;
        public PoseSpace Space { get; }
        public Pose Pose { get; }

        public FixedPoseSource(string id, PoseSpace space)
            : this(id, space, new Pose(Vector3.zero, Quaternion.identity))
        {
        }

        public FixedPoseSource(string id, PoseSpace space, Pose pose)
        {
            Id = id;
            Space = space;
            Pose = pose;
        }

        /// <summary>その空間の部屋の基準のID（例 "room:SteamVR"）。</summary>
        public static string RoomIdFor(PoseSpace space) => "room:" + space;

        /// <summary>その空間の部屋の基準。</summary>
        public static FixedPoseSource Room(PoseSpace space) => new FixedPoseSource(RoomIdFor(space), space);

        public bool TryGetPose(out Pose pose)
        {
            pose = Pose;
            return true;
        }
    }

    /// <summary>あとから部位を割り当てられるソース（SteamVR のトラッカーなど）。</summary>
    public interface IAssignableSite
    {
        /// <summary>割り当てがないときの部位（SteamVR の役割から決めたものなど）。</summary>
        BodySite DefaultSite { get; }

        BodySite Site { get; set; }
    }
}
