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

        /// <summary>左右を除いた部位の名前（右足首・左足首 → 足首）。2本の追加肢を左右の同じ部位で動かすときの表示に使う。</summary>
        public static string Kind(BodySite site)
        {
            string name = Japanese(site);
            return BodySites.HasSide(site) && name.Length > 1 ? name.Substring(1) : name;
        }
    }

    /// <summary>
    /// 部位の左右（2026-11-13）。追加肢は左右2本で、右の追加肢は右の部位、左の追加肢は左の同じ部位で動かす。
    /// 入力の部位の一覧やマッピングの型は右の部位で持ち、左の追加肢では鏡に写した部位を使う。
    /// </summary>
    public static class BodySites
    {
        /// <summary>左右のある部位か。</summary>
        public static bool HasSide(BodySite site) => IsLeft(site) || IsRight(site);

        public static bool IsLeft(BodySite site) =>
            site == BodySite.LeftShoulder || site == BodySite.LeftHand || site == BodySite.LeftKnee || site == BodySite.LeftAnkle || site == BodySite.LeftElbow;

        public static bool IsRight(BodySite site) =>
            site == BodySite.RightShoulder || site == BodySite.RightHand || site == BodySite.RightKnee || site == BodySite.RightAnkle || site == BodySite.RightElbow;

        /// <summary>左右を入れ替えた部位（左右のない部位はそのまま）。</summary>
        public static BodySite Mirror(BodySite site)
        {
            switch (site)
            {
                case BodySite.LeftShoulder: return BodySite.RightShoulder;
                case BodySite.RightShoulder: return BodySite.LeftShoulder;
                case BodySite.LeftHand: return BodySite.RightHand;
                case BodySite.RightHand: return BodySite.LeftHand;
                case BodySite.LeftKnee: return BodySite.RightKnee;
                case BodySite.RightKnee: return BodySite.LeftKnee;
                case BodySite.LeftAnkle: return BodySite.RightAnkle;
                case BodySite.RightAnkle: return BodySite.LeftAnkle;
                case BodySite.LeftElbow: return BodySite.RightElbow;
                case BodySite.RightElbow: return BodySite.LeftElbow;
                default: return site;
            }
        }

        /// <summary>左右のある部位を、left なら左、そうでなければ右の部位にする（左右のない部位はそのまま）。</summary>
        public static BodySite OnSide(BodySite site, bool left)
        {
            if (!HasSide(site)) return site;
            return IsLeft(site) == left ? site : Mirror(site);
        }
    }
}
