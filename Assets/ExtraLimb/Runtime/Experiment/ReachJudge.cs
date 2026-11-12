using System.Collections.Generic;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>ターゲットに触れるもの（効果器）。</summary>
    public enum EffectorKind
    {
        None = 0,
        ExtraRight = 1,
        ExtraLeft = 2,
        HandLeft = 3,
        HandRight = 4,
    }

    /// <summary>効果器の名前（記録の列の値と表示名）。</summary>
    public static class EffectorKinds
    {
        public static bool IsExtraLimb(EffectorKind kind) => kind == EffectorKind.ExtraRight || kind == EffectorKind.ExtraLeft;

        /// <summary>reach.csv に書く名前。</summary>
        public static string Key(EffectorKind kind)
        {
            switch (kind)
            {
                case EffectorKind.ExtraRight: return "extra_right";
                case EffectorKind.ExtraLeft: return "extra_left";
                case EffectorKind.HandLeft: return "hand_left";
                case EffectorKind.HandRight: return "hand_right";
                default: return "";
            }
        }

        public static string Japanese(EffectorKind kind)
        {
            switch (kind)
            {
                case EffectorKind.ExtraRight: return "右の追加肢";
                case EffectorKind.ExtraLeft: return "左の追加肢";
                case EffectorKind.HandLeft: return "左手";
                case EffectorKind.HandRight: return "右手";
                default: return "—";
            }
        }
    }

    /// <summary>
    /// 効果器の1フレームぶんの位置（Step 5-1 の EffectorProbe）。追加肢は指先、本来の手は手のひらの点。
    /// radius は効果器の大きさで、ターゲットの表面からこの距離に入ったら触れたとする。
    /// </summary>
    public struct EffectorProbe
    {
        public EffectorKind kind;
        public bool tracked;
        public Vector3 point;
        public float radius;

        public EffectorProbe(EffectorKind kind, bool tracked, Vector3 point, float radius)
        {
            this.kind = kind;
            this.tracked = tracked;
            this.point = point;
            this.radius = radius;
        }
    }

    /// <summary>
    /// 点灯しているターゲットに、どの効果器が触れたかを決める（Step 5-1）。
    /// 触れたのは「外から中へ入った」とき（点灯した時点で中にいたものは、一度出てから入り直す）。
    /// 数える効果器が入ったらその効果器を返す。前半（本来の手は数えない）に本来の手が入ったときは、数えずに ignored を増やす。
    /// 同じフレームに複数入ったら、渡した順で先のもの（追加肢を先に渡す）。
    /// </summary>
    public sealed class ReachJudge
    {
        readonly Dictionary<EffectorKind, bool> inside = new Dictionary<EffectorKind, bool>();

        public static bool IsInside(in EffectorProbe probe, Vector3 center, float targetRadius) =>
            probe.tracked && (probe.point - center).sqrMagnitude <= (targetRadius + probe.radius) * (targetRadius + probe.radius);

        /// <summary>新しいターゲットを点灯したとき：今中にいる効果器を覚えるだけで、触れたとは数えない。</summary>
        public void Reset(Vector3 center, float targetRadius, IReadOnlyList<EffectorProbe> probes)
        {
            inside.Clear();
            if (probes == null) return;
            foreach (var probe in probes) inside[probe.kind] = IsInside(probe, center, targetRadius);
        }

        /// <summary>
        /// 1フレームぶん進める。数える効果器が入ったらそれを返す（なければ None）。
        /// naturalHandsCount が false のとき、本来の手が入った回数を ignored に返す。
        /// </summary>
        public EffectorKind Update(Vector3 center, float targetRadius, IReadOnlyList<EffectorProbe> probes, bool naturalHandsCount, out int ignored)
        {
            ignored = 0;
            var hit = EffectorKind.None;
            if (probes == null) return hit;
            foreach (var probe in probes)
            {
                bool now = IsInside(probe, center, targetRadius);
                inside.TryGetValue(probe.kind, out bool before);
                inside[probe.kind] = now;
                if (!now || before) continue;
                bool counts = EffectorKinds.IsExtraLimb(probe.kind) || naturalHandsCount;
                if (counts)
                {
                    if (hit == EffectorKind.None) hit = probe.kind;
                }
                else
                {
                    ignored++;
                }
            }
            return hit;
        }
    }
}
