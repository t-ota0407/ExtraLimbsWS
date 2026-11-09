namespace ExtraLimb.Core
{
    /// <summary>
    /// トラッカーやコントローラーを付ける体の部位。
    /// 入力ソース（Tracking層）と設定（LimbRuntimeConfig）の両方から使うので、いちばん下のCoreに置く。
    /// </summary>
    public enum BodySite
    {
        Unknown = 0,
        Waist,
        Chest,
        Head,
        LeftShoulder,
        RightShoulder,
        LeftHand,
        RightHand,
        LeftKnee,
        RightKnee,
        LeftAnkle,
        RightAnkle,
    }
}
