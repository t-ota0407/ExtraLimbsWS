using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// モニタにだけ・HMD にだけ描く物のレイヤー（Step 4-2b、計画書の Part 2-4）。
    /// <list type="bullet">
    /// <item>MonitorOnly：実験者のための可視化（Re-center時の基準点、変位、IKターゲット、届く範囲、トリガー）。モニタの第三者視点のカメラだけが描く。
    /// フェーズCで参加者にも見せるときは、HMD のカメラにも描かせる。</item>
    /// <item>HmdOnly：参加者への通知（条件が変わります、カウントダウン）。HMD のカメラだけが描く。</item>
    /// </list>
    /// レイヤーの名前は ProjectSettings の TagManager に入れてある（番号だけでも動く）。
    /// </summary>
    public static class DisplayLayers
    {
        public const int MonitorOnly = 29;
        public const string MonitorOnlyName = "ExtraLimbMonitorOnly";
        public const int HmdOnly = 30;
        public const string HmdOnlyName = "ExtraLimbHmdOnly";

        public const int MonitorOnlyMask = 1 << MonitorOnly;
        public const int HmdOnlyMask = 1 << HmdOnly;

        /// <summary>モニタ（第三者視点）のカメラのカリングマスク：可視化を描き、通知は描かない。</summary>
        public static int MonitorMask(int mask) => (mask | MonitorOnlyMask) & ~HmdOnlyMask;

        /// <summary>HMD のカメラのカリングマスク：通知を描く。可視化は showOverlay のときだけ描く。</summary>
        public static int HmdMask(int mask, bool showOverlay) =>
            (showOverlay ? mask | MonitorOnlyMask : mask & ~MonitorOnlyMask) | HmdOnlyMask;

        /// <summary>GameObject とその子をすべてそのレイヤーにする。</summary>
        public static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
        }
    }
}
