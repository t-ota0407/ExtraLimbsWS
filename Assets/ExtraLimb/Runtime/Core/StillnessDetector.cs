using System.Collections.Generic;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// いくつかの部位（トラッカー）が止まっているかを見る（2026-11-15）。課題の開始で、参加者が楽な姿勢で止まってから Re-center するのに使う。
    /// どの部位も、速さが MaxSpeed（m/s）以下・回る速さが MaxAngularSpeed（度/s）以下のまま HoldSeconds 続いたら「止まった」とする。
    /// 追跡が切れた部位があるか、部位の顔ぶれが変わったら数え直す。
    /// </summary>
    public sealed class StillnessDetector
    {
        readonly Dictionary<string, Pose> last = new Dictionary<string, Pose>();
        readonly HashSet<string> seen = new HashSet<string>();

        public float MaxSpeed { get; set; } = 0.03f;
        public float MaxAngularSpeed { get; set; } = 15f;
        public float HoldSeconds { get; set; } = 0.5f;

        /// <summary>止まり続けている秒。</summary>
        public float StillSeconds { get; private set; }

        public void Reset()
        {
            last.Clear();
            StillSeconds = 0f;
        }

        /// <summary>
        /// 今の姿勢を入れて、止まったかを返す。poses は部位の ID と姿勢（追跡が切れている部位は tracked＝false）。部位がなければ false。
        /// </summary>
        public bool Update(IReadOnlyList<(string id, bool tracked, Pose pose)> poses, float deltaTime)
        {
            if (poses == null || poses.Count == 0)
            {
                Reset();
                return false;
            }
            float dt = Mathf.Max(deltaTime, 1e-4f);
            bool still = true;
            seen.Clear();
            foreach (var (id, tracked, pose) in poses)
            {
                seen.Add(id);
                if (!tracked)
                {
                    still = false;
                    last.Remove(id);
                    continue;
                }
                if (last.TryGetValue(id, out var previous))
                {
                    float speed = Vector3.Distance(pose.position, previous.position) / dt;
                    float angular = Quaternion.Angle(pose.rotation, previous.rotation) / dt;
                    if (speed > MaxSpeed || angular > MaxAngularSpeed) still = false;
                }
                else
                {
                    still = false;
                }
                last[id] = pose;
            }
            // 顔ぶれが変わったら数え直す
            if (last.Count != seen.Count)
            {
                var stale = new List<string>();
                foreach (var id in last.Keys)
                    if (!seen.Contains(id)) stale.Add(id);
                foreach (var id in stale) last.Remove(id);
                still = false;
            }
            StillSeconds = still ? StillSeconds + deltaTime : 0f;
            return StillSeconds >= HoldSeconds - 1e-4f;
        }
    }
}
