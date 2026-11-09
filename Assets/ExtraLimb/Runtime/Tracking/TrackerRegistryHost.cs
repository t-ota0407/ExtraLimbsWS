using UnityEngine;

namespace ExtraLimb.Tracking
{
    /// <summary>シーンに1つ置き、TrackerRegistry を持たせる。</summary>
    [DefaultExecutionOrder(-100)]
    public class TrackerRegistryHost : MonoBehaviour
    {
        public TrackerRegistry Registry { get; } = new TrackerRegistry();

        /// <summary>指定がなければシーンから探す。</summary>
        public static TrackerRegistryHost Resolve(TrackerRegistryHost assigned)
        {
            return assigned != null ? assigned : FindFirstObjectByType<TrackerRegistryHost>();
        }
    }
}
