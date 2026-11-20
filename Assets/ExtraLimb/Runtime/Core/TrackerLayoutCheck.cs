using System.Collections.Generic;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// トラッカーの付け間違いを、Re-center のときの位置から見つける（2026-11-15）。
    /// <list type="bullet">
    /// <item>高さ：座っているとき、同じ側の部位は 肩 ＞ 肘 ＞ 膝 ＞ 足首 の順に高い。順が逆なら、部位を取り違えている。</item>
    /// <item>左右：右の部位は左の同じ部位より体の右にある。逆なら、左右を取り違えている。</item>
    /// </list>
    /// 体の向き（右・上）は、同じ Re-center で求めた前の向きから作る。左右の組から前の向きを求めた組は、この判定では必ず正しく見えるので、
    /// その組の取り違えは、前の向きが前回から大きく変わったこと（LimbSession）で見つける。
    /// </summary>
    public static class TrackerLayoutCheck
    {
        /// <summary>高い部位が低い部位よりこれ（m）以上高くなければ、順が逆とみなす。</summary>
        public const float MinHeightGap = 0.02f;

        /// <summary>右の部位が左の同じ部位よりこれ（m）以上右になければ、左右が逆とみなす。</summary>
        public const float MinSideGap = 0.05f;

        /// <summary>座ったときの高さの順（大きいほど高い）。高さで比べない部位は 0。</summary>
        public static int HeightRank(BodySite site)
        {
            switch (site)
            {
                case BodySite.LeftShoulder:
                case BodySite.RightShoulder: return 4;
                case BodySite.LeftElbow:
                case BodySite.RightElbow: return 3;
                case BodySite.LeftKnee:
                case BodySite.RightKnee: return 2;
                case BodySite.LeftAnkle:
                case BodySite.RightAnkle: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// 付け間違いの疑いを文で返す（なければ空）。trackers は部位と位置（基準の座標）、up・right は体の上と右（基準の座標）。
        /// </summary>
        public static List<string> Find(IReadOnlyList<(BodySite site, Vector3 point)> trackers, Vector3 up, Vector3 right)
        {
            var problems = new List<string>();
            if (trackers == null) return problems;
            var vertical = up.sqrMagnitude > 1e-12f ? up.normalized : Vector3.up;
            var lateral = Vector3.ProjectOnPlane(right, vertical);
            lateral = lateral.sqrMagnitude > 1e-12f ? lateral.normalized : Vector3.right;

            // 高さ：同じ側で、順の違う部位
            for (int i = 0; i < trackers.Count; i++)
            {
                for (int j = 0; j < trackers.Count; j++)
                {
                    var (a, pa) = trackers[i];
                    var (b, pb) = trackers[j];
                    int ra = HeightRank(a), rb = HeightRank(b);
                    if (ra == 0 || rb == 0 || ra <= rb || BodySites.IsRight(a) != BodySites.IsRight(b)) continue;
                    if (Vector3.Dot(pa - pb, vertical) < MinHeightGap)
                        problems.Add($"{BodySiteNames.Japanese(a)}のトラッカーが{BodySiteNames.Japanese(b)}より高くありません（部位の取り違え？）");
                }
            }

            // 左右：同じ種類の左右
            foreach (var (site, point) in trackers)
            {
                if (HeightRank(site) == 0 || !BodySites.IsRight(site)) continue;
                var mirror = BodySites.Mirror(site);
                foreach (var (other, otherPoint) in trackers)
                {
                    if (other != mirror) continue;
                    if (Vector3.Dot(point - otherPoint, lateral) < MinSideGap)
                        problems.Add($"{BodySiteNames.Japanese(site)}と{BodySiteNames.Japanese(mirror)}のトラッカーが左右逆のようです");
                }
            }
            return problems;
        }
    }
}
