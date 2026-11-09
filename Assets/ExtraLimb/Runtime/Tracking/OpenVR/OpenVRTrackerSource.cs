using ExtraLimb.Core;
using UnityEngine;
using Valve.VR;

namespace ExtraLimb.Tracking.OpenVR
{
    /// <summary>
    /// SteamVR（ヘッドセットなし）から読む VIVE トラッカー1台。値は OpenVRTrackerProvider が毎フレーム書き込む。
    /// 姿勢は SteamVR のトラッカーの空間（RawAndUncalibrated）で、Questの空間とは合っていない。
    /// </summary>
    public sealed class OpenVRTrackerSource : ITrackedPoseSource
    {
        /// <summary>シリアル番号（例 "LHR-8A48D912"）。</summary>
        public string Id { get; }

        /// <summary>部位。最初は SteamVR の役割から決め、あとから書き換えられる（Step 3-2 の TrackerBindingProfile）。</summary>
        public BodySite Site { get; set; }

        public PoseSpace Space => PoseSpace.SteamVR;

        /// <summary>SteamVR の役割（例 "TrackerRole_Waist"）。分からなければ null。</summary>
        public string SteamVRRole { get; internal set; }

        public uint DeviceIndex { get; internal set; }
        public bool IsConnected { get; internal set; }
        public bool IsPoseValid { get; internal set; }
        public ETrackingResult TrackingResult { get; internal set; }

        /// <summary>最後に得られた正しい姿勢。</summary>
        public Pose LastPose { get; internal set; } = new Pose(Vector3.zero, Quaternion.identity);

        public OpenVRTrackerSource(string serial, uint deviceIndex)
        {
            Id = serial;
            DeviceIndex = deviceIndex;
        }

        public bool TryGetPose(out Pose pose)
        {
            pose = LastPose;
            return IsConnected && IsPoseValid;
        }
    }
}
