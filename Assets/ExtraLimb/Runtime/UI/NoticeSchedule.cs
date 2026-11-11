using System.Collections.Generic;

namespace ExtraLimb.UI
{
    /// <summary>
    /// HMD に出す通知の予定（Step 4-2b）。文を一定時間出す、またはカウントダウン（3, 2, 1, はじめ）。
    /// 時刻を渡すと、そのとき出す文を返す（何も出さないときは空文字）。新しい通知は、出している途中の通知を置き換える。
    /// 時間の進め方を外から渡すので、EditMode でテストできる。
    /// </summary>
    public class NoticeSchedule
    {
        readonly List<(double start, double end, string text)> items = new List<(double, double, string)>();

        /// <summary>今から seconds 秒だけ text を出す。</summary>
        public void Show(double now, string text, double seconds)
        {
            items.Clear();
            if (string.IsNullOrEmpty(text) || seconds <= 0) return;
            items.Add((now, now + seconds, text));
        }

        /// <summary>from, from-1, …, 1 を1秒ずつ出し、続けて doneText を doneSeconds 秒出す。</summary>
        public void Countdown(double now, int from, string doneText, double doneSeconds = 1.0)
        {
            items.Clear();
            double t = now;
            for (int i = from; i >= 1; i--)
            {
                items.Add((t, t + 1.0, i.ToString()));
                t += 1.0;
            }
            if (!string.IsNullOrEmpty(doneText) && doneSeconds > 0) items.Add((t, t + doneSeconds, doneText));
        }

        /// <summary>出している通知を消す。</summary>
        public void Clear() => items.Clear();

        /// <summary>その時刻に出す文（なければ空文字）。</summary>
        public string TextAt(double now)
        {
            foreach (var (start, end, text) in items)
                if (now >= start && now < end) return text;
            return "";
        }

        /// <summary>その時刻より後に出す予定がまだあるか。</summary>
        public bool IsActive(double now)
        {
            foreach (var item in items)
                if (now < item.end) return true;
            return false;
        }
    }
}
