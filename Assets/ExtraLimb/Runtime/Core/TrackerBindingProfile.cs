using System;
using System.Collections.Generic;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>トラッカー1台（シリアル番号）と、付ける部位の対応。</summary>
    [Serializable]
    public class TrackerBinding
    {
        public string serial = "";
        public BodySite site = BodySite.Unknown;

        public TrackerBinding()
        {
        }

        public TrackerBinding(string serial, BodySite site)
        {
            this.serial = serial;
            this.site = site;
        }
    }

    /// <summary>「前を記録」で求めた体の前の向き（基準の座標、水平の単位ベクトル）。基準ごとに持つ。</summary>
    [Serializable]
    public class ForwardCalibration
    {
        public string referenceId = "";
        public Vector3 forward = Vector3.forward;

        public ForwardCalibration()
        {
        }

        public ForwardCalibration(string referenceId, Vector3 forward)
        {
            this.referenceId = referenceId;
            this.forward = forward;
        }
    }

    /// <summary>
    /// 実験室の機材の設定：どのトラッカーをどの部位に付けるかと、基準ごとの「前の向き」。
    /// 参加者や条件が変わっても同じなので、条件の設定（LimbRuntimeConfig）とは分けてファイルに保存する。
    /// 1つの部位には1台だけを割り当てる（Assign で別のトラッカーに割り当てると、前のトラッカーは未割り当てに戻る。
    /// 順番に切り替えるときは NextFreeSite で使用中の部位を飛ばすので、前のトラッカーの割り当ては消えない）。
    /// </summary>
    [Serializable]
    public class TrackerBindingProfile
    {
        public List<TrackerBinding> bindings = new List<TrackerBinding>();
        public List<ForwardCalibration> forwards = new List<ForwardCalibration>();

        /// <summary>そのトラッカーに割り当てた部位。割り当てがなければ false。</summary>
        public bool TryGetSite(string serial, out BodySite site)
        {
            foreach (var binding in bindings)
            {
                if (binding.serial != serial) continue;
                site = binding.site;
                return true;
            }
            site = BodySite.Unknown;
            return false;
        }

        /// <summary>その部位に割り当てたトラッカーのシリアル番号。なければ null。</summary>
        public string FindSerial(BodySite site)
        {
            if (site == BodySite.Unknown) return null;
            foreach (var binding in bindings)
                if (binding.site == site) return binding.serial;
            return null;
        }

        /// <summary>
        /// そのトラッカーに使う部位。割り当てがあればそれ、なければ defaultSite（SteamVR の役割から決めたものなど）。
        /// ただし defaultSite がほかのトラッカーに割り当て済みなら未割り当て（1つの部位に2台が重ならないように）。
        /// </summary>
        public BodySite ResolveSite(string serial, BodySite defaultSite)
        {
            if (TryGetSite(serial, out var site)) return site;
            return FindSerial(defaultSite) != null ? BodySite.Unknown : defaultSite;
        }

        /// <summary>
        /// 部位を順番に切り替えるときの次の部位。ほかのトラッカーが使っている部位（taken）は飛ばす（未割り当ては飛ばさない）。
        /// 例：order＝未割り当て→右足首→右膝→右肘、右膝が使用中なら、右足首の次は右肘。
        /// </summary>
        public static BodySite NextFreeSite(BodySite current, IList<BodySite> order, ICollection<BodySite> taken)
        {
            if (order == null || order.Count == 0) return BodySite.Unknown;
            int start = order.IndexOf(current);
            for (int step = 1; step <= order.Count; step++)
            {
                var candidate = order[(start + step) % order.Count];
                if (candidate == BodySite.Unknown || taken == null || !taken.Contains(candidate)) return candidate;
            }
            return BodySite.Unknown;
        }

        /// <summary>トラッカーに部位を割り当てる。Unknown なら割り当てを消す。</summary>
        public void Assign(string serial, BodySite site)
        {
            if (string.IsNullOrEmpty(serial)) throw new ArgumentException("シリアル番号が空です", nameof(serial));
            bindings.RemoveAll(b => b.serial == serial || (site != BodySite.Unknown && b.site == site));
            if (site != BodySite.Unknown) bindings.Add(new TrackerBinding(serial, site));
        }

        /// <summary>その基準での体の前の向きを記録する（水平の単位ベクトル）。</summary>
        public void SetForward(string referenceId, Vector3 forward)
        {
            if (string.IsNullOrEmpty(referenceId)) throw new ArgumentException("基準のIDが空です", nameof(referenceId));
            if (forward.sqrMagnitude < 1e-12f) throw new ArgumentException("向きが0です", nameof(forward));
            forwards.RemoveAll(f => f.referenceId == referenceId);
            forwards.Add(new ForwardCalibration(referenceId, forward.normalized));
        }

        public bool TryGetForward(string referenceId, out Vector3 forward)
        {
            foreach (var calibration in forwards)
            {
                if (calibration.referenceId != referenceId) continue;
                forward = calibration.forward;
                return forward.sqrMagnitude > 1e-12f;
            }
            forward = Vector3.forward;
            return false;
        }

        public string ToJson(bool prettyPrint = true) => JsonUtility.ToJson(this, prettyPrint);

        /// <summary>JSONから読む。空や壊れたJSONなら空の設定を返す。</summary>
        public static TrackerBindingProfile FromJson(string json)
        {
            var profile = new TrackerBindingProfile();
            if (string.IsNullOrWhiteSpace(json)) return profile;
            try
            {
                JsonUtility.FromJsonOverwrite(json, profile);
            }
            catch (ArgumentException)
            {
                return new TrackerBindingProfile();
            }
            profile.bindings ??= new List<TrackerBinding>();
            profile.forwards ??= new List<ForwardCalibration>();
            return profile;
        }
    }
}
