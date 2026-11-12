using UnityEngine;
using UnityEngine.XR;

namespace ExtraLimb.Tracking.Meta
{
    /// <summary>
    /// HMD のセッションが動いていない間、OVRManager を止める（Step 5-2）。
    /// <para>
    /// 2026-11-12 のドライランで、Quest を Link でつないだまま HMD のセッションが動いていない状態（IDLE）で Sandbox を約2分半動かすと、Unity が落ちた。
    /// その間 OVRManager は毎フレーム、ランタイムが返す原点の種類と自分の設定（FloorLevel）が違うとして原点を設定し直し、
    /// そのたびに OpenXR の基準の空間（reference space）が1つ作られて、約1万個で上限（XR_ERROR_LIMIT_REACHED）に達していた。
    /// HMD をかぶっている間（セッションが動いている間）は起きない。休憩で HMD を外し、Quest が休止したときに起きうる。
    /// </para>
    /// セッションが止まって stopDelaySeconds たったら OVRManager を止め、動き出したら（HMD をかぶる）すぐに戻す。
    /// 止めている間は OVRInput（コントローラー）も更新されないが、HMD を外している間なので使わない。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class OVRManagerIdleGuard : MonoBehaviour, ITrackerProviderStatus
    {
        [Tooltip("空ならシーンから探す")]
        [SerializeField] OVRManager manager;
        [Tooltip("セッションが止まってから OVRManager を止めるまでの秒数")]
        [SerializeField] float stopDelaySeconds = 3f;
        [Tooltip("再生を始めてからこの秒数は止めない（起動中はセッションが動き出すまで少しかかる）")]
        [SerializeField] float startupGraceSeconds = 10f;

        float notRunningSince = -1f;
        bool paused;
        int pauseCount;

        /// <summary>OVRManager を止めているか。</summary>
        public bool IsPaused => paused;

        /// <summary>HMD のセッションが動いているか（XR の表示が動いているか、アプリに VR のフォーカスがある）。</summary>
        public static bool SessionRunning
        {
            get
            {
                if (XRSettings.isDeviceActive) return true;
                try
                {
                    return OVRPlugin.hasVrFocus;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string ProviderName => "HMD";

        /// <summary>
        /// 機器の状態の一覧には出さない（HMD を追跡していないことは Quest の行に出るので、重ねない）。止めたこと・戻したことはコンソールに出す。
        /// </summary>
        public string StatusText => "";

        /// <summary>止めた回数。</summary>
        public int PauseCount => pauseCount;

        void Update()
        {
            if (manager == null) manager = OVRManager.instance != null ? OVRManager.instance : FindFirstObjectByType<OVRManager>(FindObjectsInactive.Include);
            if (manager == null) return;

            bool running = SessionRunning;
            float now = Time.unscaledTime;
            if (running)
            {
                notRunningSince = -1f;
                if (paused) Resume();
                return;
            }
            if (notRunningSince < 0f) notRunningSince = now;
            if (!paused && now >= startupGraceSeconds && now - notRunningSince >= stopDelaySeconds) Pause();
        }

        void OnDisable()
        {
            if (paused) Resume();
        }

        void Pause()
        {
            paused = true;
            pauseCount++;
            manager.enabled = false;
            Debug.Log("[ExtraLimb] HMD のセッションが止まっているので、OVRManager を止めました（毎フレーム原点を設定し直して落ちるのを防ぐ）");
        }

        void Resume()
        {
            paused = false;
            if (manager != null) manager.enabled = true;
            Debug.Log("[ExtraLimb] HMD のセッションが動き出したので、OVRManager を戻しました");
        }
    }
}
