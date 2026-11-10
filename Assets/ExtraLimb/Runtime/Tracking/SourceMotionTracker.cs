using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// 入力ソースごとの速さを測り、最近振られたトラッカーを覚えておく。
    /// 「トラッカーを振ってからボタンを押す」で部位を割り当てるのと、モニタの「動き」の欄に使う。
    /// 速さは毎フレームの移動距離÷時間をならしたもの（m/s）。
    /// </summary>
    public class SourceMotionTracker
    {
        class State
        {
            public Vector3 lastPosition;
            public bool hasLast;
            public float speed;
        }

        readonly Dictionary<string, State> states = new Dictionary<string, State>();

        /// <summary>速さをならす割合（0〜1、大きいほど速く追従する）。</summary>
        public float Smoothing { get; set; } = 0.2f;

        /// <summary>この速さ（m/s）を超えたら「動いている」とみなす。</summary>
        public float ThresholdMetersPerSecond { get; set; } = 0.15f;

        /// <summary>最後に、動いているソースの中でいちばん速かったもの。なければ null。</summary>
        public string LastMovedId { get; private set; }

        /// <summary>LastMovedId が動いていた最後の時刻（秒）。</summary>
        public float LastMovedTime { get; private set; } = float.NegativeInfinity;

        /// <summary>
        /// 速さを更新する。time は今の時刻（秒）。candidate を満たすソースだけが「最近動いたもの」の候補になる。
        /// </summary>
        public void Update(TrackerRegistry registry, float deltaTime, float time, Func<ITrackedPoseSource, bool> candidate = null)
        {
            if (registry == null) return;
            float dt = Mathf.Max(deltaTime, 1e-4f);
            string fastestId = null;
            float fastest = ThresholdMetersPerSecond;
            foreach (var source in registry.Sources)
            {
                if (!states.TryGetValue(source.Id, out var state))
                {
                    state = new State();
                    states.Add(source.Id, state);
                }
                if (!source.TryGetPose(out var pose))
                {
                    state.hasLast = false;
                    state.speed = 0f;
                    continue;
                }
                if (state.hasLast) state.speed = Mathf.Lerp(state.speed, Vector3.Distance(pose.position, state.lastPosition) / dt, Smoothing);
                state.lastPosition = pose.position;
                state.hasLast = true;
                if (state.speed >= fastest && (candidate == null || candidate(source)))
                {
                    fastest = state.speed;
                    fastestId = source.Id;
                }
            }
            if (fastestId != null)
            {
                LastMovedId = fastestId;
                LastMovedTime = time;
            }
        }

        /// <summary>そのソースの速さ（m/s）。測っていなければ0。</summary>
        public float GetSpeed(string id) => id != null && states.TryGetValue(id, out var state) ? state.speed : 0f;

        /// <summary>window 秒以内に動いていたソース。なければ null。</summary>
        public string RecentlyMoved(float time, float window) => LastMovedId != null && time - LastMovedTime <= window ? LastMovedId : null;

        /// <summary>最近動いたものを忘れる（割り当てた後など）。</summary>
        public void ClearRecent()
        {
            LastMovedId = null;
            LastMovedTime = float.NegativeInfinity;
        }
    }

    /// <summary>今どのソースを入力と基準にしているか（追加肢など）。モニタはこれに合わせて変位を表示する。</summary>
    public interface ISourceSelection
    {
        string InputId { get; }
        string ReferenceId { get; }
    }
}
