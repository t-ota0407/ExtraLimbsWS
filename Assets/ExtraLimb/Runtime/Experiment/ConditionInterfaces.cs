namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 参加者（HMD）への通知（Step 4-3）。ExtraLimb.UI の HmdNotice が実装する。
    /// 条件の進行（ConditionRunner）は UI に依存しないよう、これを通して通知を出す。
    /// </summary>
    public interface IParticipantNotice
    {
        /// <summary>文を seconds 秒（0 以下なら既定の秒数）出す。</summary>
        void Show(string text, float seconds);

        /// <summary>from から1秒ずつ数え、最後に doneText を出す。</summary>
        void Countdown(int from, string doneText);

        void Clear();
    }

    /// <summary>
    /// 条件の中で行う課題（Step 5-1 のリーチング課題が実装する）。ConditionRunner が前半・後半の始めと終わりに呼ぶ。
    /// </summary>
    public interface IConditionTask
    {
        /// <summary>区間（1＝前半、2＝後半）を始める。naturalHandsCount＝本来の手で触れても数えるか（前半は false）。</summary>
        void BeginSegment(int segment, bool naturalHandsCount);

        /// <summary>区間を終え、その区間で数えた到達数を返す。</summary>
        int EndSegment(int segment);

        /// <summary>課題を途中で止めた（中止）。</summary>
        void CancelSegment();
    }
}
