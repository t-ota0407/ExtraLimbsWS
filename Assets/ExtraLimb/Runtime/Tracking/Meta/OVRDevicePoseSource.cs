using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Tracking.Meta
{
    /// <summary>OVRCameraRig のアンカーで測る、Questの機器。</summary>
    public enum OVRDevice
    {
        Head = 0,
        LeftHand = 1,
        RightHand = 2,
    }

    /// <summary>
    /// OVRCameraRig のアンカー（頭・左手・右手）の姿勢を返すソース。姿勢はワールド座標（Questの空間）。
    /// 手はコントローラーとハンドトラッキングのどちらで追跡していてもよい。
    /// </summary>
    public sealed class OVRDevicePoseSource : ITrackedPoseSource
    {
        readonly Transform anchor;

        public OVRDevice Device { get; }
        public string Id { get; }
        public BodySite Site { get; }
        public PoseSpace Space => PoseSpace.Quest;

        public OVRDevicePoseSource(OVRDevice device, Transform anchor)
        {
            Device = device;
            this.anchor = anchor;
            switch (device)
            {
                case OVRDevice.Head:
                    Id = "ovr:head";
                    Site = BodySite.Head;
                    break;
                case OVRDevice.LeftHand:
                    Id = "ovr:left_hand";
                    Site = BodySite.LeftHand;
                    break;
                default:
                    Id = "ovr:right_hand";
                    Site = BodySite.RightHand;
                    break;
            }
        }

        public bool TryGetPose(out Pose pose)
        {
            if (anchor == null || !IsTracked())
            {
                pose = default;
                return false;
            }
            pose = new Pose(anchor.position, anchor.rotation);
            return true;
        }

        bool IsTracked()
        {
            switch (Device)
            {
                case OVRDevice.Head:
                    return OVRManager.isHmdPresent;
                case OVRDevice.LeftHand:
                    return OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch)
                        || OVRInput.GetControllerPositionTracked(OVRInput.Controller.LHand);
                default:
                    return OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch)
                        || OVRInput.GetControllerPositionTracked(OVRInput.Controller.RHand);
            }
        }
    }
}
