using ExtraLimb.Core;
using UnityEngine;
using UnityEngine.XR;

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
    /// 追跡中かどうかは、Unity の XR 入力（OpenXR）と OVR の両方で確かめ、どちらかが追跡中なら追跡中とする。
    /// アンカーは OVRCameraRig が起動時に用意するので、使うたびに OVRCameraRig から取り出す。
    /// </summary>
    public sealed class OVRDevicePoseSource : ITrackedPoseSource
    {
        readonly OVRCameraRig cameraRig;

        public OVRDevice Device { get; }
        public string Id { get; }
        public BodySite Site { get; }
        public PoseSpace Space => PoseSpace.Quest;
        /// <summary>姿勢を読むアンカー。OVRCameraRig の準備ができる前は null。</summary>
        public Transform Anchor
        {
            get
            {
                if (cameraRig == null) return null;
                switch (Device)
                {
                    case OVRDevice.Head: return cameraRig.centerEyeAnchor;
                    case OVRDevice.LeftHand: return cameraRig.leftHandAnchor;
                    default: return cameraRig.rightHandAnchor;
                }
            }
        }

        public OVRDevicePoseSource(OVRDevice device, OVRCameraRig cameraRig)
        {
            Device = device;
            this.cameraRig = cameraRig;
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
            var anchor = Anchor;
            if (anchor == null || !IsTracked())
            {
                pose = default;
                return false;
            }
            pose = new Pose(anchor.position, anchor.rotation);
            return true;
        }

        public bool IsTracked()
        {
            if (IsTrackedByXR()) return true;
            try
            {
                return IsTrackedByOVR();
            }
            catch (System.Exception)
            {
                // OVRManager の初期化前などに例外が出ても、追跡なしとして扱う
                return false;
            }
        }

        /// <summary>Unity の XR 入力（OpenXR）で追跡中か。</summary>
        public bool IsTrackedByXR()
        {
            switch (Device)
            {
                case OVRDevice.Head: return IsNodeTracked(XRNode.Head) || IsNodeTracked(XRNode.CenterEye);
                case OVRDevice.LeftHand: return IsNodeTracked(XRNode.LeftHand);
                default: return IsNodeTracked(XRNode.RightHand);
            }
        }

        /// <summary>Meta XR SDK（OVRManager・OVRInput）で追跡中か。</summary>
        public bool IsTrackedByOVR()
        {
            switch (Device)
            {
                case OVRDevice.Head:
                    return OVRManager.isHmdPresent && OVRManager.instance != null && OVRManager.instance.isUserPresent;
                case OVRDevice.LeftHand:
                    return OVRInput.GetControllerPositionTracked(OVRInput.Controller.LTouch)
                        || OVRInput.GetControllerPositionTracked(OVRInput.Controller.LHand);
                default:
                    return OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch)
                        || OVRInput.GetControllerPositionTracked(OVRInput.Controller.RHand);
            }
        }

        static bool IsNodeTracked(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return false;
            if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked) return true;
            return device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state)
                && (state & (InputTrackingState.Position | InputTrackingState.Rotation)) != 0;
        }
    }
}
