using System;
using System.Collections.Generic;

namespace ExtraLimb.Experiment
{
    /// <summary>1条件の課題の時間の割り振り（Step 4-3、計画書の1.5）。</summary>
    [Serializable]
    public class TrialTiming
    {
        /// <summary>前半の前に、何を使うかを HMD に出す秒数（0 なら出さない）。</summary>
        public float leadMessageSeconds = 2.5f;

        /// <summary>カウントダウン（3, 2, 1）の数。最後に「はじめ」を出したときに区間が始まる。</summary>
        public int countdown = 3;

        /// <summary>前半・後半それぞれの秒数（計画書の決定事項#24。一旦は1分）。</summary>
        public float segmentSeconds = 60f;

        /// <summary>前半が終わってから、後半の案内を HMD に出す秒数（その後にカウントダウン）。</summary>
        public float interludeMessageSeconds = 3f;
    }

    /// <summary>課題の区切り（時刻の順）。</summary>
    public enum TrialCue
    {
        /// <summary>前半の案内（「追加肢だけで触れます」）を出す。</summary>
        LeadMessage,
        Countdown1,
        Segment1Start,
        Segment1End,

        /// <summary>後半の案内（「次は両手も使えます」）を出す。</summary>
        InterludeMessage,
        Countdown2,
        Segment2Start,
        Segment2End,
    }

    /// <summary>
    /// 1条件の課題の時間の進み方（Step 4-3）：案内 → カウントダウン → 前半 → 案内 → カウントダウン → 後半。
    /// 始めた時刻から各区切りの時刻を決めておき、Advance に今の時刻を渡すと、過ぎた区切りを順に返す。
    /// 時刻は外から渡すので、EditMode でテストできる（ConditionRunner は Time.realtimeSinceStartupAsDouble を渡す）。
    /// </summary>
    public class TrialTimeline
    {
        readonly List<(double time, TrialCue cue)> cues = new List<(double, TrialCue)>();
        int next;

        /// <summary>進めている途中か（後半が終わるか止めると false）。</summary>
        public bool Running { get; private set; }

        /// <summary>始めた時刻。</summary>
        public double StartTime { get; private set; }

        /// <summary>最後に過ぎた区切り（まだなければ null）。</summary>
        public TrialCue? LastCue => next > 0 ? cues[next - 1].cue : (TrialCue?)null;

        /// <summary>今の区間（0＝区間の外、1＝前半、2＝後半）。</summary>
        public int Segment
        {
            get
            {
                if (!Running) return 0;
                var last = LastCue;
                if (last == TrialCue.Segment1Start) return 1;
                if (last == TrialCue.Segment2Start) return 2;
                return 0;
            }
        }

        /// <summary>now に start から始める。</summary>
        public void Start(double now, TrialTiming timing)
        {
            timing ??= new TrialTiming();
            cues.Clear();
            next = 0;
            StartTime = now;
            double segment = Math.Max(0.1, timing.segmentSeconds);
            double countdown = Math.Max(0, timing.countdown);
            double t = now;
            if (timing.leadMessageSeconds > 0)
            {
                cues.Add((t, TrialCue.LeadMessage));
                t += timing.leadMessageSeconds;
            }
            cues.Add((t, TrialCue.Countdown1));
            t += countdown;
            cues.Add((t, TrialCue.Segment1Start));
            t += segment;
            cues.Add((t, TrialCue.Segment1End));
            if (timing.interludeMessageSeconds > 0)
            {
                cues.Add((t, TrialCue.InterludeMessage));
                t += timing.interludeMessageSeconds;
            }
            cues.Add((t, TrialCue.Countdown2));
            t += countdown;
            cues.Add((t, TrialCue.Segment2Start));
            t += segment;
            cues.Add((t, TrialCue.Segment2End));
            Running = true;
        }

        /// <summary>止める（区切りはもう返さない）。</summary>
        public void Stop()
        {
            Running = false;
            cues.Clear();
            next = 0;
        }

        /// <summary>now までに過ぎた区切りを、時刻の順に output に足す。足した数を返す。後半が終わったら Running が false になる。</summary>
        public int Advance(double now, List<TrialCue> output)
        {
            if (!Running) return 0;
            int count = 0;
            while (next < cues.Count && cues[next].time <= now)
            {
                output?.Add(cues[next].cue);
                next++;
                count++;
            }
            if (next >= cues.Count) Running = false;
            return count;
        }

        /// <summary>区切り cue の時刻（なければ NaN）。</summary>
        public double TimeOf(TrialCue cue)
        {
            foreach (var item in cues)
                if (item.cue == cue) return item.time;
            return double.NaN;
        }

        /// <summary>次の区切りまでの秒（なければ 0）。</summary>
        public double UntilNext(double now) => Running && next < cues.Count ? Math.Max(0, cues[next].time - now) : 0;

        /// <summary>今の区間の残りの秒（区間の外なら 0）。</summary>
        public double SegmentRemaining(double now)
        {
            int segment = Segment;
            if (segment == 0) return 0;
            double end = TimeOf(segment == 1 ? TrialCue.Segment1End : TrialCue.Segment2End);
            return double.IsNaN(end) ? 0 : Math.Max(0, end - now);
        }
    }

    /// <summary>
    /// 口頭評価（Step 4-3、計画書の1.5・決定事項#27）：3項目 × 7段階。0 はまだ入れていない。
    /// 数字キーは「次に入れる項目」（Cursor）に入れて、次の空いた項目へ進む。
    /// </summary>
    [Serializable]
    public class ConditionRating
    {
        public const int Min = 1, Max = 7;

        /// <summary>項目の名前（参加者に読み上げる）。</summary>
        public static readonly string[] ItemNames = { "操作しやすい", "自分の身体の一部のように感じた", "疲れた" };

        /// <summary>記録の列の名前。</summary>
        public static readonly string[] ItemKeys = { "ease", "body", "fatigue" };

        public int[] values = new int[3];

        public int Count => ItemNames.Length;

        public int this[int item] => item >= 0 && item < values.Length ? values[item] : 0;

        /// <summary>3項目とも入れたか。</summary>
        public bool IsComplete
        {
            get
            {
                foreach (int value in values)
                    if (value < Min) return false;
                return true;
            }
        }

        /// <summary>次に入れる項目（全部入れたら -1）。</summary>
        public int Cursor
        {
            get
            {
                for (int i = 0; i < values.Length; i++)
                    if (values[i] < Min) return i;
                return -1;
            }
        }

        /// <summary>item に value（1〜7）を入れる。範囲外なら何もしないで false。</summary>
        public bool Set(int item, int value)
        {
            if (item < 0 || item >= values.Length || value < Min || value > Max) return false;
            values[item] = value;
            return true;
        }

        /// <summary>次に入れる項目に value を入れ、入れた項目を返す（全部入れてあるか範囲外なら -1）。</summary>
        public int Enter(int value)
        {
            int item = Cursor;
            return item >= 0 && Set(item, value) ? item : -1;
        }

        public void Clear() => Array.Clear(values, 0, values.Length);

        public override string ToString()
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++) parts[i] = values[i] >= Min ? values[i].ToString() : "—";
            return string.Join(" / ", parts);
        }
    }
}
