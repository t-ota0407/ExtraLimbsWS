using System;
using System.Collections.Generic;
using ExtraLimb.Core;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 1つの条件（Step 4-3、計画書の1.3）：生やす位置 × 入力部位 × マッピングの型。ゲインは型の初期値を使う。
    /// 提示順の中の位置（何番目か、何ブロック目の何番目か）も持つ。JSON（sequence.json）にそのまま書ける。
    /// </summary>
    [Serializable]
    public class ConditionSpec
    {
        /// <summary>提示順の中の番号（0 始まり）。</summary>
        public int order;

        /// <summary>ブロックの番号（0 始まり。ブロックは入力部位ごと）。</summary>
        public int block;

        /// <summary>ブロックの中の番号（0 始まり）。</summary>
        public int indexInBlock;

        /// <summary>そのブロックの条件の数。</summary>
        public int blockSize;

        public string attachPointId = "";
        public string attachPointName = "";
        public BodySite inputSite;
        public string inputSiteName = "";
        public string mappingPresetId = "";
        public string mappingName = "";

        /// <summary>「ブロック-ブロックの中の番号」（1 始まり。例：2-3）。</summary>
        public string Code => $"{block + 1}-{indexInBlock + 1}";

        /// <summary>例：脇腹｜右膝｜膝：すねを振る。</summary>
        public string Label => $"{attachPointName}｜{inputSiteName}｜{mappingName}";

        public override string ToString() => $"{order + 1}（{Code}）{Label}";
    }

    /// <summary>参加者1人ぶんの提示順（sequence.json の中身）。</summary>
    [Serializable]
    public class ConditionPlan
    {
        public string participantId = "";

        /// <summary>入力部位のブロックの順に使ったラテン方格の行（0 始まり）。</summary>
        public int latinRow;

        /// <summary>ラテン方格の行の数（ブロックの順の種類）。</summary>
        public int latinRowCount;

        /// <summary>ブロック（入力部位）の順。</summary>
        public BodySite[] blockSites = Array.Empty<BodySite>();

        /// <summary>ブロックの順（部位の表示名。JSON を読みやすくするため）。</summary>
        public string[] blockSiteNames = Array.Empty<string>();

        public ConditionSpec[] conditions = Array.Empty<ConditionSpec>();

        public int Count => conditions?.Length ?? 0;
        public int BlockCount => blockSites?.Length ?? 0;

        /// <summary>例：右肩 → 右肘 → 右足首 → 右膝。</summary>
        public string BlockOrderText
        {
            get
            {
                var names = new List<string>();
                foreach (var site in blockSites ?? Array.Empty<BodySite>()) names.Add(BodySiteNames.Japanese(site));
                return string.Join(" → ", names);
            }
        }
    }

    /// <summary>
    /// バランスドラテン方格（Williams の方法）。どの行でも各水準が1回ずつ現れ、各列でも各水準が同じ回数現れ、
    /// 「a の直後に b」という並びがどれも同じ回数になる。水準の数が偶数なら n 行、奇数なら 2n 行（後半は前半の行の逆順）。
    /// </summary>
    public static class BalancedLatinSquare
    {
        public static int RowCount(int n) => n <= 0 ? 0 : n % 2 == 0 ? n : 2 * n;

        /// <summary>row 行目（0 始まり、RowCount で折り返す）の並び。値は 0〜n-1。</summary>
        public static int[] Row(int n, int row)
        {
            if (n <= 0) return Array.Empty<int>();
            int rows = RowCount(n);
            row = ((row % rows) + rows) % rows;
            bool reversed = row >= n;
            int shift = row % n;
            // 1行目：0, 1, n-1, 2, n-2, 3, …
            var result = new int[n];
            int low = 1, high = n - 1;
            for (int i = 0; i < n; i++)
            {
                int value = i == 0 ? 0 : i % 2 == 1 ? low++ : high--;
                result[i] = (value + shift) % n;
            }
            if (reversed) Array.Reverse(result);
            return result;
        }
    }

    /// <summary>
    /// 参加者IDから提示順を作る（Step 4-3、計画書の1.3・決定事項#26）。
    /// <list type="bullet">
    /// <item>条件は 生やす位置 × 入力部位 × その部位のマッピングの型 のすべての組み合わせ。</item>
    /// <item>入力部位ごとのブロックにまとめ、ブロックの順はバランスドラテン方格の1行（参加者IDの数字から決める。P01 → 1行目）。</item>
    /// <item>ブロックの中の 生やす位置 × 型 の順はランダム。乱数の種は参加者IDと部位から作るので、同じIDなら同じ順になる。</item>
    /// </list>
    /// </summary>
    public static class ConditionSequence
    {
        /// <summary>選べるもの（ID と表示名）。</summary>
        public readonly struct Option
        {
            public readonly string id;
            public readonly string name;

            public Option(string id, string name)
            {
                this.id = id ?? "";
                this.name = string.IsNullOrEmpty(name) ? this.id : name;
            }
        }

        /// <summary>参加者IDの最後の数字の並び（例：P01 → 1、p12 → 12）。数字がなければ -1。</summary>
        public static int ParticipantNumber(string participantId)
        {
            if (string.IsNullOrEmpty(participantId)) return -1;
            int end = participantId.Length - 1;
            while (end >= 0 && !char.IsDigit(participantId[end])) end--;
            if (end < 0) return -1;
            int start = end;
            while (start > 0 && char.IsDigit(participantId[start - 1])) start--;
            string digits = participantId.Substring(start, Math.Min(end - start + 1, 9));
            return int.TryParse(digits, out int number) ? number : -1;
        }

        /// <summary>参加者IDから決まるラテン方格の行（0 始まり）。数字が n なら (n-1) を行の数で割った余り。数字がなければIDのハッシュから。</summary>
        public static int DefaultRow(string participantId, int rowCount)
        {
            if (rowCount <= 0) return 0;
            int number = ParticipantNumber(participantId);
            if (number > 0) return (number - 1) % rowCount;
            return (int)(StableHash(participantId ?? "") % (uint)rowCount);
        }

        /// <summary>
        /// 提示順を作る。row を省くと参加者IDから決める（DefaultRow）。
        /// mappingsForSite がその部位で型を返さなければ、その部位は条件に入れない。
        /// </summary>
        public static ConditionPlan Build(string participantId, IReadOnlyList<Option> attachPoints, IReadOnlyList<BodySite> sites,
            Func<BodySite, IReadOnlyList<Option>> mappingsForSite, int? row = null)
        {
            participantId = (participantId ?? "").Trim();
            var usableSites = new List<BodySite>();
            var siteMappings = new Dictionary<BodySite, IReadOnlyList<Option>>();
            if (sites != null)
                foreach (var site in sites)
                {
                    var mappings = mappingsForSite?.Invoke(site);
                    if (mappings == null || mappings.Count == 0 || siteMappings.ContainsKey(site)) continue;
                    usableSites.Add(site);
                    siteMappings[site] = mappings;
                }

            int rowCount = BalancedLatinSquare.RowCount(usableSites.Count);
            int latinRow = rowCount > 0 ? ((row ?? DefaultRow(participantId, rowCount)) % rowCount + rowCount) % rowCount : 0;
            var blockOrder = BalancedLatinSquare.Row(usableSites.Count, latinRow);

            var plan = new ConditionPlan
            {
                participantId = participantId,
                latinRow = latinRow,
                latinRowCount = rowCount,
                blockSites = new BodySite[blockOrder.Length],
                blockSiteNames = new string[blockOrder.Length],
            };
            var conditions = new List<ConditionSpec>();
            for (int block = 0; block < blockOrder.Length; block++)
            {
                var site = usableSites[blockOrder[block]];
                plan.blockSites[block] = site;
                plan.blockSiteNames[block] = BodySiteNames.Japanese(site);
                var cells = new List<ConditionSpec>();
                if (attachPoints != null)
                    foreach (var attach in attachPoints)
                    foreach (var mapping in siteMappings[site])
                        cells.Add(new ConditionSpec
                        {
                            block = block,
                            attachPointId = attach.id,
                            attachPointName = attach.name,
                            inputSite = site,
                            inputSiteName = BodySiteNames.Japanese(site),
                            mappingPresetId = mapping.id,
                            mappingName = mapping.name,
                        });
                Shuffle(cells, new StableRandom(StableHash($"{participantId}|{site}")));
                for (int i = 0; i < cells.Count; i++)
                {
                    cells[i].indexInBlock = i;
                    cells[i].blockSize = cells.Count;
                    cells[i].order = conditions.Count;
                    conditions.Add(cells[i]);
                }
            }
            plan.conditions = conditions.ToArray();
            return plan;
        }

        static void Shuffle<T>(IList<T> list, StableRandom random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>文字列のハッシュ（FNV-1a、32 bit）。実行環境によらず同じ値になる（string.GetHashCode は変わることがある）。</summary>
        public static uint StableHash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text ?? "")
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }

    /// <summary>
    /// 実行環境によらず同じ並びを返す乱数（SplitMix64）。提示順（ConditionSequence）とリーチング課題のターゲットの順（ReachTask）に使う。
    /// System.Random や string.GetHashCode は実行環境で並びが変わることがあるので使わない。
    /// </summary>
    public sealed class StableRandom
    {
        ulong state;

        public StableRandom(uint seed) => state = seed;

        /// <summary>文字列から種を作る（ConditionSequence.StableHash）。</summary>
        public StableRandom(string seed) : this(ConditionSequence.StableHash(seed)) { }

        ulong NextUlong()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>0 以上 maxExclusive 未満の整数。</summary>
        public int Next(int maxExclusive) => maxExclusive <= 1 ? 0 : (int)(NextUlong() % (ulong)maxExclusive);

        /// <summary>0 以上 count 未満で、current と違う整数（count が 1 以下なら 0）。</summary>
        public int NextOther(int count, int current)
        {
            if (count <= 1) return 0;
            if (current < 0 || current >= count) return Next(count);
            int value = Next(count - 1);
            return value >= current ? value + 1 : value;
        }
    }
}
