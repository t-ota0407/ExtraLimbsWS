using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// トラッカーの確認（2026-11-15）：部位を1つずつ挙げ、その部位を動かしてもらうか、その部位のトラッカーを揺らしてもらい、
    /// いちばん動いたトラッカーをその部位に割り当てる。体の動きで決めるので、実験者はどのトラッカーをどこへ付けたか覚えなくてよい
    /// （装着の後と、後半のブロックの前の付け替えの後に使う）。
    /// 1つの部位の間に、まだ割り当てていないトラッカーの移動距離を足していき、いちばん動いたものが AcceptDistance 以上で、
    /// 2番目の DominanceRatio 倍以上なら決める。同じくらい動いたトラッカーがあるときは決めずに Hint を出す。
    /// トラッカーのない部位（左のトラッカーを付けずに右だけで行うときなど）は Skip で飛ばす（2026-11-20）。
    /// </summary>
    public sealed class TrackerCheck
    {
        /// <summary>この距離（m）以上動いたトラッカーを候補にする。</summary>
        public const float AcceptDistance = 0.10f;

        /// <summary>いちばん動いたトラッカーが、2番目のこの倍以上動いていれば決める。</summary>
        public const float DominanceRatio = 2f;

        /// <summary>決まらないまま、いちばん動いたものがこの距離（m）を超えたら数え直す。</summary>
        public const float RestartDistance = 0.4f;

        // 1フレームの移動がこれより小さければ揺れ（ノイズ）、大きければ追跡の飛びとみなして足さない
        const float MinStep = 0.0005f;
        const float MaxStep = 0.5f;

        readonly List<BodySite> sites = new List<BodySite>();
        readonly Dictionary<BodySite, string> found = new Dictionary<BodySite, string>();
        readonly HashSet<BodySite> skipped = new HashSet<BodySite>();
        readonly Dictionary<string, float> travelled = new Dictionary<string, float>();
        readonly Dictionary<string, Vector3> lastPosition = new Dictionary<string, Vector3>();

        /// <summary>確かめる部位（順に）。</summary>
        public IReadOnlyList<BodySite> Sites => sites;

        /// <summary>今確かめている部位の番号（確かめていなければ -1、終わったら Sites.Count）。</summary>
        public int Index { get; private set; } = -1;

        public bool IsActive => Index >= 0 && Index < sites.Count;

        public bool IsComplete => sites.Count > 0 && Index >= sites.Count;

        /// <summary>今確かめている部位（なければ Unknown）。</summary>
        public BodySite CurrentSite => IsActive ? sites[Index] : BodySite.Unknown;

        /// <summary>決まらないときの案内（なければ空）。</summary>
        public string Hint { get; private set; } = "";

        /// <summary>部位ごとに決まったトラッカー。</summary>
        public IReadOnlyDictionary<BodySite, string> Found => found;

        public bool TryGetFound(BodySite site, out string sourceId) => found.TryGetValue(site, out sourceId);

        /// <summary>トラッカーなしとして飛ばした部位。</summary>
        public IReadOnlyCollection<BodySite> Skipped => skipped;

        public bool IsSkipped(BodySite site) => skipped.Contains(site);

        /// <summary>確認を始めてから一度でも位置が入ったトラッカーのうち、まだどの部位にも決まっていないものの数。</summary>
        public int UnassignedSeenCount
        {
            get
            {
                int count = 0;
                foreach (var id in lastPosition.Keys)
                    if (!found.ContainsValue(id)) count++;
                return count;
            }
        }

        /// <summary>左右の同じ部位らしい位置：決まった方の部位のトラッカーとの高さの差の上限（m）。</summary>
        public const float PairMaxHeightGap = 0.25f;

        /// <summary>左右の同じ部位らしい位置：決まった方の部位のトラッカーとの水平の間隔の下限・上限（m）。</summary>
        public const float PairMinSeparation = 0.08f, PairMaxSeparation = 0.8f;

        /// <summary>
        /// 残りの部位（今の部位から後ろ）のそれぞれに、まだ決まっていないトラッカー（確認を始めてから見えたもの）を1台ずつ割り振れるか。
        /// 左右の組のもう片方が決まっていれば、その部位に割り振れるのは、決まった方と高さが近く（PairMaxHeightGap 以内）、
        /// ほどよく離れた（PairMinSeparation〜PairMaxSeparation）トラッカーだけ（右だけ付けて、余ったトラッカーが右のほかの部位にあるときは割り振れない）。
        /// </summary>
        public bool CanCoverRemaining(Vector3 up)
        {
            if (!IsActive) return true;
            var candidates = new List<string>();
            foreach (var id in lastPosition.Keys)
                if (!found.ContainsValue(id)) candidates.Add(id);
            var remaining = new List<BodySite>();
            for (int i = Index; i < sites.Count; i++) remaining.Add(sites[i]);
            if (candidates.Count < remaining.Count) return false;

            // 部位ごとに割り振れるトラッカー
            var options = new List<List<int>>();
            foreach (var site in remaining)
            {
                var list = new List<int>();
                bool hasPartner = found.TryGetValue(BodySites.Mirror(site), out string partner) && lastPosition.ContainsKey(partner);
                for (int c = 0; c < candidates.Count; c++)
                    if (!hasPartner || LooksLikePair(lastPosition[partner], lastPosition[candidates[c]], up)) list.Add(c);
                options.Add(list);
            }
            // 部位とトラッカーの組み合わせ（二部グラフの最大マッチング。部位もトラッカーも数台なので総当たりでよい）
            var owner = new int[candidates.Count];
            for (int c = 0; c < owner.Length; c++) owner[c] = -1;
            for (int s = 0; s < remaining.Count; s++)
                if (!Augment(s, options, owner, new bool[candidates.Count])) return false;
            return true;
        }

        static bool LooksLikePair(Vector3 partner, Vector3 candidate, Vector3 up)
        {
            var delta = candidate - partner;
            float height = Mathf.Abs(Vector3.Dot(delta, up.normalized));
            float separation = Vector3.ProjectOnPlane(delta, up.normalized).magnitude;
            return height <= PairMaxHeightGap && separation >= PairMinSeparation && separation <= PairMaxSeparation;
        }

        static bool Augment(int site, List<List<int>> options, int[] owner, bool[] visited)
        {
            foreach (int c in options[site])
            {
                if (visited[c]) continue;
                visited[c] = true;
                if (owner[c] < 0 || Augment(owner[c], options, owner, visited))
                {
                    owner[c] = site;
                    return true;
                }
            }
            return false;
        }

        /// <summary>今の部位を、トラッカーなしとして飛ばす。飛ばした部位を返す（確認中でなければ Unknown）。</summary>
        public BodySite Skip()
        {
            if (!IsActive) return BodySite.Unknown;
            var site = sites[Index];
            skipped.Add(site);
            Index++;
            ResetStep();
            return site;
        }

        /// <summary>確認を始める（部位の順に確かめる）。</summary>
        public void Begin(IEnumerable<BodySite> order)
        {
            sites.Clear();
            found.Clear();
            skipped.Clear();
            if (order != null)
                foreach (var site in order)
                    if (site != BodySite.Unknown && !sites.Contains(site)) sites.Add(site);
            Index = sites.Count > 0 ? 0 : -1;
            ResetStep();
            lastPosition.Clear();
        }

        /// <summary>確認をやめる（決まった分も捨てる）。</summary>
        public void Cancel()
        {
            Index = -1;
            sites.Clear();
            found.Clear();
            skipped.Clear();
            ResetStep();
            lastPosition.Clear();
        }

        /// <summary>
        /// 候補のトラッカーの今の位置を入れる（毎フレーム）。今の部位が決まったらその部位を、決まらなければ Unknown を返す。
        /// </summary>
        public BodySite Update(IReadOnlyList<(string id, Vector3 position)> trackers)
        {
            if (!IsActive || trackers == null) return BodySite.Unknown;
            foreach (var (id, position) in trackers)
            {
                if (string.IsNullOrEmpty(id) || found.ContainsValue(id)) continue;
                if (lastPosition.TryGetValue(id, out var previous))
                {
                    float step = Vector3.Distance(position, previous);
                    if (step >= MinStep && step <= MaxStep) travelled[id] = (travelled.TryGetValue(id, out float sum) ? sum : 0f) + step;
                }
                lastPosition[id] = position;
            }

            string best = null;
            float first = 0f, second = 0f;
            foreach (var pair in travelled)
            {
                if (pair.Value > first)
                {
                    second = first;
                    first = pair.Value;
                    best = pair.Key;
                }
                else if (pair.Value > second)
                {
                    second = pair.Value;
                }
            }
            if (best == null || first < AcceptDistance) return BodySite.Unknown;
            if (first >= DominanceRatio * second)
            {
                var site = sites[Index];
                found[site] = best;
                Index++;
                ResetStep();
                return site;
            }
            Hint = "2つのトラッカーが同じくらい動いています。その部位のトラッカーだけを軽く揺らしてください";
            if (first >= RestartDistance) travelled.Clear();
            return BodySite.Unknown;
        }

        /// <summary>位置だけを覚え、動いた距離には数えない（部位と部位の間の一息など）。</summary>
        public void Observe(IReadOnlyList<(string id, Vector3 position)> trackers)
        {
            if (trackers == null) return;
            foreach (var (id, position) in trackers)
                if (!string.IsNullOrEmpty(id)) lastPosition[id] = position;
        }

        void ResetStep()
        {
            travelled.Clear();
            Hint = "";
        }
    }
}
