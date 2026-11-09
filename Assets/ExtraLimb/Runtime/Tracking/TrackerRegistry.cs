using System;
using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Tracking
{
    /// <summary>
    /// 使える入力ソースの一覧。各プロバイダ（仮想トラッカー、SteamVR、Quest）がここに登録し、上の層はIDか部位で探す。
    /// 同じIDで登録し直すと置き換わる。
    /// </summary>
    public class TrackerRegistry
    {
        readonly List<ITrackedPoseSource> sources = new List<ITrackedPoseSource>();

        /// <summary>登録順の一覧。</summary>
        public IReadOnlyList<ITrackedPoseSource> Sources => sources;

        /// <summary>登録・削除・置き換えがあったとき。</summary>
        public event Action Changed;

        public void Register(ITrackedPoseSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int index = IndexOf(source.Id);
            if (index >= 0)
            {
                if (ReferenceEquals(sources[index], source)) return;
                sources[index] = source;
            }
            else
            {
                sources.Add(source);
            }
            Changed?.Invoke();
        }

        public bool Unregister(string id)
        {
            int index = IndexOf(id);
            if (index < 0) return false;
            sources.RemoveAt(index);
            Changed?.Invoke();
            return true;
        }

        public bool TryGet(string id, out ITrackedPoseSource source)
        {
            int index = IndexOf(id);
            source = index >= 0 ? sources[index] : null;
            return source != null;
        }

        /// <summary>その部位の最初のソース。なければ null。</summary>
        public ITrackedPoseSource FindBySite(BodySite site)
        {
            foreach (var source in sources)
                if (source.Site == site) return source;
            return null;
        }

        /// <summary>その空間・部位の最初のソース。なければ null。</summary>
        public ITrackedPoseSource FindBySite(BodySite site, PoseSpace space)
        {
            foreach (var source in sources)
                if (source.Site == site && source.Space == space) return source;
            return null;
        }

        int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < sources.Count; i++)
                if (string.Equals(sources[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
