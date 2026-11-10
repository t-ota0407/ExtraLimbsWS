namespace ExtraLimb.Core
{
    /// <summary>
    /// 押し込み量（0〜1）を、ヒステリシス付きでオン・オフにする。On 以上でオン、Off 以下でオフ（On &gt; Off）。
    /// コントローラーのトリガーやグリップで「掴む・放す」を決めるのに使う。閾値付近でちらつかない。
    /// </summary>
    public class HysteresisSwitch
    {
        public float On { get; set; }
        public float Off { get; set; }
        public bool State { get; private set; }

        public HysteresisSwitch(float on = 0.6f, float off = 0.4f)
        {
            On = on;
            Off = off;
        }

        /// <summary>今の値で状態を更新して返す。</summary>
        public bool Update(float value)
        {
            if (!State && value >= On) State = true;
            else if (State && value <= Off) State = false;
            return State;
        }

        public void Reset() => State = false;
    }
}
