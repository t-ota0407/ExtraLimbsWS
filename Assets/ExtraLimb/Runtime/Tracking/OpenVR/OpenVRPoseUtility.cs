using System.Text;
using ExtraLimb.Core;
using UnityEngine;
using Valve.VR;

namespace ExtraLimb.Tracking.OpenVR
{
    /// <summary>OpenVR の値を Unity の形に直す。</summary>
    public static class OpenVRPoseUtility
    {
        /// <summary>OpenVR（右手系、-Zが前）の3x4行列を、Unity（左手系、+Zが前）の姿勢に直す。</summary>
        public static Pose ToUnityPose(HmdMatrix34_t m)
        {
            var matrix = Matrix4x4.identity;
            matrix.m00 = m.m0; matrix.m01 = m.m1; matrix.m02 = -m.m2; matrix.m03 = m.m3;
            matrix.m10 = m.m4; matrix.m11 = m.m5; matrix.m12 = -m.m6; matrix.m13 = m.m7;
            matrix.m20 = -m.m8; matrix.m21 = -m.m9; matrix.m22 = m.m10; matrix.m23 = -m.m11;
            var position = new Vector3(matrix.m03, matrix.m13, matrix.m23);
            var rotation = Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));
            return new Pose(position, rotation);
        }

        /// <summary>OpenVR の座標の点を Unity の座標に直す（z の符号を反転）。</summary>
        public static Vector3 ToUnityPoint(Vector3 openVrPoint) => new Vector3(openVrPoint.x, openVrPoint.y, -openVrPoint.z);

        /// <summary>
        /// SteamVR の「トラッカーの管理」で付けた役割（例 "TrackerRole_Waist"）から部位を決める。
        /// 足（Foot）は足首として扱う。対応しない役割は Unknown。
        /// </summary>
        public static BodySite SiteFromTrackerRole(string role)
        {
            switch (role)
            {
                case "TrackerRole_Waist": return BodySite.Waist;
                case "TrackerRole_Chest": return BodySite.Chest;
                case "TrackerRole_LeftShoulder": return BodySite.LeftShoulder;
                case "TrackerRole_RightShoulder": return BodySite.RightShoulder;
                case "TrackerRole_LeftKnee": return BodySite.LeftKnee;
                case "TrackerRole_RightKnee": return BodySite.RightKnee;
                case "TrackerRole_LeftFoot":
                case "TrackerRole_LeftAnkle": return BodySite.LeftAnkle;
                case "TrackerRole_RightFoot":
                case "TrackerRole_RightAnkle": return BodySite.RightAnkle;
                default: return BodySite.Unknown;
            }
        }

        internal static string GetStringProperty(CVRSystem system, uint index, ETrackedDeviceProperty property)
        {
            var error = ETrackedPropertyError.TrackedProp_Success;
            var buffer = new StringBuilder(128);
            system.GetStringTrackedDeviceProperty(index, property, buffer, (uint)buffer.Capacity, ref error);
            return error == ETrackedPropertyError.TrackedProp_Success ? buffer.ToString() : null;
        }

        /// <summary>SteamVR の設定（trackers セクション）から、そのトラッカーの役割を読む。読めなければ null。</summary>
        internal static string GetTrackerRole(string registeredDeviceType)
        {
            if (string.IsNullOrEmpty(registeredDeviceType)) return null;
            var settings = Valve.VR.OpenVR.Settings;
            if (settings == null) return null;
            var error = EVRSettingsError.None;
            var buffer = new StringBuilder(128);
            settings.GetString("trackers", "/devices/" + registeredDeviceType, buffer, (uint)buffer.Capacity, ref error);
            return error == EVRSettingsError.None && buffer.Length > 0 ? buffer.ToString() : null;
        }
    }
}
