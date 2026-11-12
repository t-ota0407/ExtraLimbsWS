using ExtraLimb.Core;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// 入力と基準のソースの選び方。追加肢（LimbSession）と確認用のモニタで同じ規則を使う。
    /// <list type="bullet">
    /// <item>入力：IDの指定があればそれ。なければ部位で探す（記録の再生 → SteamVR のトラッカー → 仮想トラッカー → Quest の順）。</item>
    /// <item>基準：IDの指定があり、入力と同じ空間で入力とは別のソースならそれ。なければ入力と同じ空間の部屋。</item>
    /// </list>
    /// </summary>
    public static class SourceSelector
    {
        // 記録の再生（Step 4-4）は、再生している間だけソースがあるので、いちばん先に探す
        static readonly PoseSpace[] SpacePreference = { PoseSpace.Replay, PoseSpace.SteamVR, PoseSpace.Virtual, PoseSpace.Quest };

        /// <summary>入力のソースのID。見つからなければ null。</summary>
        public static string SelectInput(TrackerRegistry registry, string explicitId, BodySite site)
        {
            if (registry == null) return null;
            if (!string.IsNullOrEmpty(explicitId) && registry.TryGet(explicitId, out var chosen) && !(chosen is FixedPoseSource)) return explicitId;
            if (site == BodySite.Unknown) return null;
            foreach (var space in SpacePreference)
            {
                var source = registry.FindBySite(site, space);
                if (source != null) return source.Id;
            }
            return null;
        }

        /// <summary>基準のソースのID。入力がなければ null。</summary>
        public static string SelectReference(TrackerRegistry registry, string explicitId, string inputId)
        {
            if (registry == null || !registry.TryGet(inputId, out var input)) return null;
            if (!string.IsNullOrEmpty(explicitId) && explicitId != inputId
                && registry.TryGet(explicitId, out var chosen) && chosen.Space == input.Space)
                return explicitId;
            string room = FixedPoseSource.RoomIdFor(input.Space);
            return registry.TryGet(room, out _) ? room : null;
        }

        /// <summary>基準が部屋か。</summary>
        public static bool IsRoom(TrackerRegistry registry, string id) =>
            registry != null && registry.TryGet(id, out var source) && source is FixedPoseSource;
    }
}
