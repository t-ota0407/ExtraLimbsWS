using System;
using System.Collections.Generic;
using System.IO;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// トラッカーと部位の対応（TrackerBindingProfile）をファイルに保存し、登録されたトラッカーに部位を書き込む。
    /// 割り当てのないトラッカーは、SteamVR の役割から決めた部位のまま（ほかのトラッカーに割り当て済みの部位なら未割り当て）。
    /// 「前を記録」で求めた体の前の向きも、基準ごとに同じファイルに保存する。
    /// 保存先：Application.persistentDataPath/ExtraLimb/tracker_bindings.json（リポジトリの外）。
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class TrackerBindingService : MonoBehaviour
    {
        [SerializeField] TrackerRegistryHost registryHost;
        [SerializeField] string fileName = "tracker_bindings.json";

        /// <summary>モニタで部位を切り替えるときの順番。</summary>
        static readonly BodySite[] CycleOrder =
        {
            BodySite.Unknown, BodySite.RightAnkle, BodySite.RightKnee, BodySite.RightShoulder, BodySite.RightElbow, BodySite.Waist,
        };

        TrackerRegistry registry;

        public TrackerBindingProfile Profile { get; private set; } = new TrackerBindingProfile();
        public string FilePath => Path.Combine(Application.persistentDataPath, "ExtraLimb", fileName);

        /// <summary>部位の割り当てが変わったとき。</summary>
        public event Action BindingsChanged;

        /// <summary>シーンにある最初のもの。</summary>
        public static TrackerBindingService Find() => FindFirstObjectByType<TrackerBindingService>();

        void Awake() => Load();

        void OnEnable()
        {
            registryHost = TrackerRegistryHost.Resolve(registryHost);
            registry = registryHost != null ? registryHost.Registry : null;
            if (registry == null) return;
            registry.Changed += ApplyAll;
            ApplyAll();
        }

        void OnDisable()
        {
            if (registry != null) registry.Changed -= ApplyAll;
        }

        /// <summary>ファイルから読み直す。</summary>
        public void Load()
        {
            try
            {
                Profile = File.Exists(FilePath) ? TrackerBindingProfile.FromJson(File.ReadAllText(FilePath)) : new TrackerBindingProfile();
                if (Profile.bindings.Count > 0) Debug.Log($"[ExtraLimb] トラッカーの割り当てを読みました（{Profile.bindings.Count}台）：{FilePath}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[ExtraLimb] トラッカーの割り当てを読めません：{e.Message}");
                Profile = new TrackerBindingProfile();
            }
            ApplyAll();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, Profile.ToJson());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[ExtraLimb] トラッカーの割り当てを保存できません：{e.Message}");
            }
        }

        /// <summary>
        /// トラッカーに部位を割り当てて保存する。Unknown なら割り当てを消す。
        /// その部位をほかのトラッカーが使っていたら、そのトラッカーは未割り当てに戻し、その ID を返す（なければ null）。
        /// </summary>
        public string Assign(string sourceId, BodySite site)
        {
            string previous = site == BodySite.Unknown ? null : FindAssignedSource(site, except: sourceId);
            Profile.Assign(sourceId, site);
            Save();
            ApplyAll();
            Debug.Log($"[ExtraLimb] トラッカー {sourceId} を「{BodySiteNames.Japanese(site)}」に割り当てました" +
                      (previous != null ? $"（{previous} は未割り当てに戻しました）" : "") + $"：{FilePath}");
            BindingsChanged?.Invoke();
            return previous;
        }

        /// <summary>
        /// 部位を順番に切り替える（未割り当て → 右足首 → 右膝 → 右肩 → 右肘 → 腰 → 未割り当て）。
        /// ほかのトラッカーが使っている部位は飛ばすので、途中で通っても前のトラッカーの割り当ては消えない。
        /// </summary>
        public void CycleSite(string sourceId)
        {
            var current = registry != null && registry.TryGet(sourceId, out var source) ? source.Site : BodySite.Unknown;
            Assign(sourceId, TrackerBindingProfile.NextFreeSite(current, CycleOrder, SitesInUse(except: sourceId)));
        }

        /// <summary>その部位を使っているトラッカー（except 以外）。なければ null。</summary>
        public string FindAssignedSource(BodySite site, string except = null)
        {
            if (registry == null || site == BodySite.Unknown) return null;
            foreach (var source in registry.Sources)
                if (source is IAssignableSite && source.Id != except && source.Site == site) return source.Id;
            return null;
        }

        // 使用中の部位：接続中のトラッカーの部位と、今は接続していないトラッカーに保存してある割り当て
        HashSet<BodySite> SitesInUse(string except)
        {
            var taken = new HashSet<BodySite>();
            foreach (var binding in Profile.bindings)
                if (binding.serial != except) taken.Add(binding.site);
            if (registry == null) return taken;
            foreach (var source in registry.Sources)
                if (source is IAssignableSite && source.Id != except && source.Site != BodySite.Unknown) taken.Add(source.Site);
            return taken;
        }

        /// <summary>その基準での体の前の向きを保存する。</summary>
        public void SaveForward(string referenceId, Vector3 forward)
        {
            if (string.IsNullOrEmpty(referenceId) || forward.sqrMagnitude < 1e-12f) return;
            Profile.SetForward(referenceId, forward);
            Save();
        }

        public bool TryGetForward(string referenceId, out Vector3 forward) => Profile.TryGetForward(referenceId, out forward);

        /// <summary>割り当てを登録済みのトラッカーに書き込む。</summary>
        public void ApplyAll()
        {
            if (registry == null) return;
            foreach (var source in registry.Sources)
                if (source is IAssignableSite assignable) assignable.Site = Profile.ResolveSite(source.Id, assignable.DefaultSite);
        }
    }
}
