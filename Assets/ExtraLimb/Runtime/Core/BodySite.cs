namespace ExtraLimb.Core
{
    /// <summary>
    /// トラッカーやコントローラーを付ける体の部位。
    /// 入力ソース（Tracking層）と設定（LimbRuntimeConfig）の両方から使うので、いちばん下のCoreに置く。
    /// 値はシーンや設定ファイルに数値で保存されるので、新しい部位は末尾に足す。
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
        LeftElbow,
        RightElbow,
    }

    /// <summary>部位の表示名。</summary>
    public static class BodySiteNames
    {
        public static string Japanese(BodySite site)
        {
            switch (site)
            {
                case BodySite.Waist: return "腰";
                case BodySite.Chest: return "胸";
                case BodySite.Head: return "頭";
                case BodySite.LeftShoulder: return "左肩";
                case BodySite.RightShoulder: return "右肩";
                case BodySite.LeftHand: return "左手";
                case BodySite.RightHand: return "右手";
                case BodySite.LeftKnee: return "左膝";
                case BodySite.RightKnee: return "右膝";
                case BodySite.LeftAnkle: return "左足首";
                case BodySite.RightAnkle: return "右足首";
                case BodySite.LeftElbow: return "左肘";
                case BodySite.RightElbow: return "右肘";
                default: return "未割り当て";
            }
        }
    }
}
