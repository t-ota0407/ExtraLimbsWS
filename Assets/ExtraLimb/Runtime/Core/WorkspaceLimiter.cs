using System;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// 手先のターゲットを、追加肢が届く範囲（根元を中心とする球殻）に収める。
    /// 座標は胴体フレーム（制御則と同じ座標）で表す。
    /// 速度制御では積分している手先の位置そのものを、ハイブリッド制御では端に当たった分だけ基準位置を戻すので、
    /// 範囲の端で入力を続けても量が溜まらず、戻し始めた瞬間に手先が戻り始める。
    /// </summary>
    public class WorkspaceLimiter
    {
        /// <summary>範囲の中心（追加肢の根元）。</summary>
        public Vector3 Center { get; set; }

        /// <summary>中心からの最小距離（m）。腕を折りたたみ切った特異な姿勢を避ける。</summary>
        public float MinRadius { get; private set; }

        /// <summary>中心からの最大距離（m）。腕が伸び切る手前にする。</summary>
        public float MaxRadius { get; private set; }

        /// <summary>ちょうど中心にあるときに押し出す向き。</summary>
        public Vector3 FallbackDirection { get; set; } = Vector3.forward;

        public WorkspaceLimiter(Vector3 center, float minRadius, float maxRadius)
        {
            Center = center;
            SetRadii(minRadius, maxRadius);
        }

        public void SetRadii(float minRadius, float maxRadius)
        {
            if (minRadius < 0f) throw new ArgumentOutOfRangeException(nameof(minRadius), "最小距離は0以上にする");
            if (maxRadius <= minRadius) throw new ArgumentOutOfRangeException(nameof(maxRadius), "最大距離は最小距離より大きくする");
            MinRadius = minRadius;
            MaxRadius = maxRadius;
        }

        /// <summary>範囲の中にあるか。</summary>
        public bool Contains(Vector3 point, float tolerance = 1e-5f)
        {
            float distance = Vector3.Distance(point, Center);
            return distance >= MinRadius - tolerance && distance <= MaxRadius + tolerance;
        }

        /// <summary>範囲の中ならそのまま、外なら最も近い範囲内の点を返す。</summary>
        public Vector3 Clamp(Vector3 point)
        {
            var offset = point - Center;
            float distance = offset.magnitude;
            if (distance > MaxRadius) return Center + offset * (MaxRadius / distance);
            if (distance >= MinRadius) return point;
            if (distance < 1e-6f) return Center + FallbackDirection.normalized * MinRadius;
            return Center + offset * (MinRadius / distance);
        }
    }
}
