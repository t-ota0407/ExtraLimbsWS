using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// 姿勢を測っている空間。変位は、同じ空間にある2つのソース（腰と入力）からしか計算できない。
    /// </summary>
    public enum PoseSpace
    {
        /// <summary>キーボードで動かす仮想トラッカー。</summary>
        Virtual = 0,

        /// <summary>SteamVR（ヘッドセットなし）が測るVIVEトラッカーの空間。</summary>
        SteamVR = 1,

        /// <summary>Questの空間（HMD・コントローラー・手）。</summary>
        Quest = 2,
    }

    /// <summary>1つの部位の姿勢を返すソース。上の層はこの形だけを見る。</summary>
    public interface ITrackedPoseSource
    {
        /// <summary>例：トラッカーのシリアル番号 "LHR-8A48D912"、"ovr:left_hand"、"mock:RightAnkle"。</summary>
        string Id { get; }

        /// <summary>付けている部位。分からなければ Unknown。</summary>
        BodySite Site { get; }

        PoseSpace Space { get; }

        /// <summary>今の姿勢。追跡が切れているときは false。</summary>
        bool TryGetPose(out Pose pose);
    }

    /// <summary>入力ソースを供給する部品の状態。モニタに表示する。</summary>
    public interface ITrackerProviderStatus
    {
        string ProviderName { get; }
        string StatusText { get; }
    }
}
