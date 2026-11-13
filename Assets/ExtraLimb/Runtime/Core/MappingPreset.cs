using System.Collections.Generic;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// マッピングの型（計画書の決定事項#21）。入力の部位ごとに研究側が決めておき、条件の中ではゲインだけを変える。
    /// フェーズCでは、その部位の型（2〜3種）から選び直せる。部位を Unknown にした型は、どの部位でも使える。
    /// 値は仮で、パイロット（Step 6-1）で入力の6つの値の動ける範囲を見ながら決める。
    /// </summary>
    [CreateAssetMenu(menuName = "ExtraLimb/Mapping Preset", fileName = "MappingPreset")]
    public class MappingPreset : ScriptableObject
    {
        [Tooltip("設定に保存するID")]
        public string id = "";

        [Tooltip("パネルに出す名前")]
        public string displayName = "";

        [Tooltip("この型を使う入力の部位。Unknown ならどの部位でも使える")]
        public BodySite site = BodySite.Unknown;

        public AxisMapping mapping = AxisMapping.Identity;

        [Tooltip("位置ゲイン G_p の初期値")]
        public float gain = 2f;

        [Tooltip("参加者に伝える動かし方")]
        [TextArea(2, 4)]
        public string instruction = "";

        public override string ToString() => $"{displayName}（{id}）";
    }

    /// <summary>マッピングの型の一覧から、部位に合うものを選ぶ。</summary>
    public static class MappingPresets
    {
        /// <summary>その部位で使える型。部位専用の型（一覧の順）の後に、どの部位でも使える型が並ぶ。</summary>
        public static List<MappingPreset> ForSite(IEnumerable<MappingPreset> presets, BodySite site)
        {
            var specific = new List<MappingPreset>();
            var generic = new List<MappingPreset>();
            if (presets == null) return specific;
            foreach (var preset in presets)
            {
                if (preset == null) continue;
                if (preset.site == BodySite.Unknown) generic.Add(preset);
                else if (preset.site == site) specific.Add(preset);
            }
            specific.AddRange(generic);
            return specific;
        }

        /// <summary>型の短い名前（「膝：すねを振る」→「すねを振る」）。部位と並べて出すときに使う。</summary>
        public static string ShortName(MappingPreset preset)
        {
            if (preset == null) return "";
            string name = preset.displayName ?? "";
            int colon = name.IndexOf('：');
            return colon >= 0 && colon + 1 < name.Length ? name.Substring(colon + 1) : name;
        }

        /// <summary>その部位の既定の型（ForSite の先頭）。なければ null。</summary>
        public static MappingPreset Default(IEnumerable<MappingPreset> presets, BodySite site)
        {
            var list = ForSite(presets, site);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>ID で探す。なければ null。</summary>
        public static MappingPreset Find(IEnumerable<MappingPreset> presets, string id)
        {
            if (presets == null || string.IsNullOrEmpty(id)) return null;
            foreach (var preset in presets)
                if (preset != null && preset.id == id) return preset;
            return null;
        }

        /// <summary>その部位で、currentId の次の型（最後なら先頭）。今の型がその部位の型でなければ既定の型。</summary>
        public static MappingPreset Next(IEnumerable<MappingPreset> presets, BodySite site, string currentId)
        {
            var list = ForSite(presets, site);
            if (list.Count == 0) return null;
            int index = list.FindIndex(p => p.id == currentId);
            return list[(index + 1) % list.Count];
        }
    }
}
