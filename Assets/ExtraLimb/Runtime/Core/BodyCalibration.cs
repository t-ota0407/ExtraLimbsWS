using System;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// 参加者の身長・肩幅から、アバターの大きさと Attach Point の位置の拡縮を求める。
    /// <list type="bullet">
    /// <item>アバターは身長の比で一様に拡縮する（AvatarScale）。</item>
    /// <item>Attach Point の左右方向（x）は、さらに「身長に対する肩幅の比」の違いで拡縮する（LateralRatio）。</item>
    /// </list>
    /// Attach Point は骨の子ではなく、骨の位置からのずれで置くので、ずれにもアバターと同じ拡縮をかける（ScaleOffset）。
    /// </summary>
    public sealed class BodyCalibration
    {
        public const float MinHeight = 1.0f;
        public const float MaxHeight = 2.3f;
        public const float MinShoulderWidth = 0.2f;
        public const float MaxShoulderWidth = 0.7f;

        /// <summary>モデルの身長（m）。</summary>
        public float ModelHeight { get; }

        /// <summary>モデルの肩幅（m。左右の上腕の付け根の間）。</summary>
        public float ModelShoulderWidth { get; }

        /// <summary>参加者の身長（m）。寸法を入れるまではモデルと同じ。</summary>
        public float Height { get; private set; }

        /// <summary>参加者の肩幅（m）。寸法を入れるまではモデルと同じ。</summary>
        public float ShoulderWidth { get; private set; }

        /// <summary>参加者の寸法を入れたか。</summary>
        public bool IsCalibrated { get; private set; }

        /// <summary>アバター全体の拡縮率。</summary>
        public float AvatarScale => Height / ModelHeight;

        /// <summary>一様に拡縮した後に、左右方向へさらにかける比。</summary>
        public float LateralRatio => (ShoulderWidth / Height) / (ModelShoulderWidth / ModelHeight);

        public BodyCalibration(float modelHeight, float modelShoulderWidth)
        {
            if (modelHeight <= 0f) throw new ArgumentOutOfRangeException(nameof(modelHeight), "モデルの身長は正の値にする");
            if (modelShoulderWidth <= 0f) throw new ArgumentOutOfRangeException(nameof(modelShoulderWidth), "モデルの肩幅は正の値にする");
            ModelHeight = modelHeight;
            ModelShoulderWidth = modelShoulderWidth;
            Reset();
        }

        /// <summary>寸法が使える範囲か（身長 1.0〜2.3 m、肩幅 0.2〜0.7 m）。</summary>
        public static bool IsValid(float height, float shoulderWidth)
        {
            return height >= MinHeight && height <= MaxHeight && shoulderWidth >= MinShoulderWidth && shoulderWidth <= MaxShoulderWidth;
        }

        /// <summary>参加者の寸法（m）を入れる。範囲外なら何もせず false。</summary>
        public bool TrySet(float height, float shoulderWidth)
        {
            if (!IsValid(height, shoulderWidth)) return false;
            Height = height;
            ShoulderWidth = shoulderWidth;
            IsCalibrated = true;
            return true;
        }

        /// <summary>モデルの寸法に戻す（拡縮なし）。</summary>
        public void Reset()
        {
            Height = ModelHeight;
            ShoulderWidth = ModelShoulderWidth;
            IsCalibrated = false;
        }

        /// <summary>モデルの寸法で測った Attach Point のずれ（体の向き、x=左右）を、参加者の体に合わせる。</summary>
        public Vector3 ScaleOffset(Vector3 offset)
        {
            float scale = AvatarScale;
            return new Vector3(offset.x * scale * LateralRatio, offset.y * scale, offset.z * scale);
        }
    }
}
