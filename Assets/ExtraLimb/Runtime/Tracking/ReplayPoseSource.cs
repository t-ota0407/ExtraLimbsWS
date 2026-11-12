using System;
using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// 記録した1部位の姿勢の並び（Step 4-4、ログの再生）。時刻（記録の開始からの秒）を渡すと、その時刻の姿勢を前後のフレームから補間して返す。
    /// 追跡が切れていたフレームの近くは、切れたものとして返す。フレームの間が maxGapSeconds より空いているところは補間しない。
    /// </summary>
    public sealed class ReplayTrack
    {
        readonly double[] times;
        readonly bool[] ok;
        readonly Vector3[] positions;
        readonly Quaternion[] rotations;

        /// <summary>この秒数より離れたフレームの間は補間しない（記録の途切れ）。</summary>
        public double MaxGapSeconds { get; set; } = 0.25;

        public int Count => times.Length;
        public double StartTime => times.Length > 0 ? times[0] : 0;
        public double EndTime => times.Length > 0 ? times[times.Length - 1] : 0;

        public ReplayTrack(IReadOnlyList<double> times, IReadOnlyList<bool> ok, IReadOnlyList<Vector3> positions, IReadOnlyList<Quaternion> rotations)
        {
            int count = times?.Count ?? 0;
            if (ok == null || positions == null || rotations == null || ok.Count != count || positions.Count != count || rotations.Count != count)
                throw new ArgumentException("times・ok・positions・rotations の長さがそろっていません");
            this.times = new double[count];
            this.ok = new bool[count];
            this.positions = new Vector3[count];
            this.rotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                this.times[i] = times[i];
                this.ok[i] = ok[i];
                this.positions[i] = positions[i];
                this.rotations[i] = rotations[i];
            }
        }

        /// <summary>何フレーム目が追跡できていたか。</summary>
        public bool IsTracked(int index) => index >= 0 && index < ok.Length && ok[index];

        /// <summary>time 以下でいちばん後のフレーム（なければ -1）。</summary>
        public int IndexAt(double time)
        {
            int low = 0, high = times.Length - 1, found = -1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                if (times[mid] <= time)
                {
                    found = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            return found;
        }

        /// <summary>time の姿勢。最初のフレームより前は最初、最後より後は最後のフレームの姿勢。追跡が切れていれば false。</summary>
        public bool TryGetPose(double time, out Pose pose)
        {
            pose = new Pose(Vector3.zero, Quaternion.identity);
            if (times.Length == 0) return false;
            int i = IndexAt(time);
            if (i < 0) i = 0;
            int j = i + 1;
            if (j >= times.Length || time <= times[i] || !ok[j] || times[j] - times[i] > MaxGapSeconds || !ok[i])
            {
                // 補間しない：近い方のフレーム
                int nearest = j < times.Length && times[j] - time < time - times[i] ? j : i;
                if (!ok[nearest]) return false;
                pose = new Pose(positions[nearest], rotations[nearest]);
                return true;
            }
            float t = (float)((time - times[i]) / (times[j] - times[i]));
            pose = new Pose(Vector3.Lerp(positions[i], positions[j], t), Quaternion.Slerp(rotations[i], rotations[j], t));
            return true;
        }
    }

    /// <summary>
    /// 記録を再生するソース（Step 4-4）。空間は Replay で、再生している間は同じ部位のほかのソースより優先される（SourceSelector）。
    /// 時刻は clock（再生している時刻）から読む。
    /// </summary>
    public sealed class ReplayPoseSource : ITrackedPoseSource
    {
        readonly ReplayTrack track;
        readonly Func<double> clock;

        public string Id { get; }
        public BodySite Site { get; }
        public PoseSpace Space => PoseSpace.Replay;

        public ReplayPoseSource(string id, BodySite site, ReplayTrack track, Func<double> clock)
        {
            Id = id;
            Site = site;
            this.track = track ?? throw new ArgumentNullException(nameof(track));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public bool TryGetPose(out Pose pose) => track.TryGetPose(clock(), out pose);
    }
}
