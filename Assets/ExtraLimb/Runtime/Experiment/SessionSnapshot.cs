using System;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// ある時点の設定の全体（Step 4-1）。記録の config_history.jsonl に、記録の開始時と設定の版が進むたびに1行ずつ書く。
    /// フレームCSVの rev 列と revision を突き合わせると、各フレームの設定が分かる。
    /// </summary>
    [Serializable]
    public class SessionSnapshot
    {
        /// <summary>設定の版（LimbSession.Revision）。</summary>
        public int revision;

        /// <summary>記録の開始からの秒（記録する側が入れる）。</summary>
        public double time;

        /// <summary>この版になったきっかけ（イベントの種類。記録の開始時は log_start）。</summary>
        public string reason = "";

        /// <summary>左の追加肢も動かしているか。</summary>
        public bool twoLimbs;

        /// <summary>パネルで設定している追加肢（right / left）。</summary>
        public string selected = "";

        /// <summary>参加者の身長・肩幅（cm）とアバターの拡縮。体の寸法を扱う部品がなければ0。</summary>
        public float heightCm;
        public float shoulderWidthCm;
        public float avatarScale;

        public LimbSnapshot[] limbs = Array.Empty<LimbSnapshot>();
    }

    /// <summary>追加肢1本の設定と、今使っているソース。</summary>
    [Serializable]
    public class LimbSnapshot
    {
        /// <summary>right / left。</summary>
        public string limb = "";

        public bool active;

        /// <summary>今使っている入力・基準のソースのID（設定で空にしてある場合は、部位や部屋から選んだもの）。</summary>
        public string inputId = "";
        public string referenceId = "";

        public LimbRuntimeConfig config = new LimbRuntimeConfig();

        /// <summary>下の基準姿勢と前の向きを書いてあるか（Step 4-4 から。古い記録では false）。</summary>
        public bool stateSaved;

        /// <summary>Re-center 済みか。</summary>
        public bool recentered;

        /// <summary>Re-center 時の、基準から見た入力の姿勢（NeutralPoseCalibrator.NeutralLocalPose）。ログの再生で同じ基準姿勢にする。</summary>
        public Vector3 neutralPosition;
        public Quaternion neutralRotation = Quaternion.identity;

        /// <summary>体の前の向きを記録してあるか、と、その向き（基準の座標）。</summary>
        public bool hasForward;
        public Vector3 forward;
    }
}
