using System;
using System.Collections.Generic;
using ExtraLimb.Core;
using UnityEngine;

namespace ExtraLimb.Experiment
{
    /// <summary>
    /// 実験者パネルの動かし方（2026-11-12）。
    /// <list type="bullet">
    /// <item>デモ：生やす位置・入力部位・マッピングの型（全組み合わせ）・ゲインなどを自由に選び、今の設定のままリーチング課題を試せる。
    /// 提示順は使わず、記録は自動では始めない（L で始められる）。</item>
    /// <item>実験：生やす位置3 × 入力部位4 の12条件を、提示順どおりに進める。入力部位ごとの型は1つ（ExperimentDesign）。
    /// 条件で決まる設定（生やす位置・入力部位・型・反転・ゲイン・2本目）はパネルとホットキーから変えられない。記録は自動で始める。</item>
    /// </list>
    /// </summary>
    public enum WorkshopMode
    {
        Demo,
        Experiment,
    }

    /// <summary>
    /// 実験モードで、入力部位ごとに使うマッピングの型（1つずつ）。デモモードのパネルで選び、
    /// Application.persistentDataPath/ExtraLimb/experiment_design.json に保存する（リポジトリの外。トラッカーの割り当てと同じ場所）。
    /// 選んでいない部位・選んだ型が見つからない部位は、その部位の既定の型（一覧の先頭）を使う。
    /// </summary>
    [Serializable]
    public class ExperimentDesign
    {
        [Serializable]
        public class Entry
        {
            public BodySite site;
            public string mappingPresetId = "";
        }

        public List<Entry> mappings = new List<Entry>();

        /// <summary>部位に選んである型のID（なければ空）。</summary>
        public string Get(BodySite site)
        {
            foreach (var entry in mappings)
                if (entry != null && entry.site == site) return entry.mappingPresetId ?? "";
            return "";
        }

        /// <summary>部位の型を選ぶ。変わったら true。</summary>
        public bool Set(BodySite site, string mappingPresetId)
        {
            mappingPresetId ??= "";
            foreach (var entry in mappings)
            {
                if (entry == null || entry.site != site) continue;
                if (entry.mappingPresetId == mappingPresetId) return false;
                entry.mappingPresetId = mappingPresetId;
                return true;
            }
            mappings.Add(new Entry { site = site, mappingPresetId = mappingPresetId });
            return true;
        }

        /// <summary>部位に使う型。選んだ型がその部位の型の一覧になければ、既定の型（先頭）。部位の型がなければ null。</summary>
        public MappingPreset Resolve(IEnumerable<MappingPreset> presets, BodySite site)
        {
            var list = MappingPresets.ForSite(presets, site);
            if (list.Count == 0) return null;
            string id = Get(site);
            foreach (var preset in list)
                if (preset.id == id) return preset;
            return list[0];
        }

        /// <summary>選んだ型が、部位の既定の型でなく選んだものとして使われているか（表示用）。</summary>
        public bool IsChosen(IEnumerable<MappingPreset> presets, BodySite site, string mappingPresetId)
        {
            var resolved = Resolve(presets, site);
            return resolved != null && resolved.id == mappingPresetId;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>JSON から読む。読めなければ空の設計（すべて既定の型）。</summary>
        public static ExperimentDesign FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new ExperimentDesign();
            try
            {
                var design = JsonUtility.FromJson<ExperimentDesign>(json);
                if (design == null) return new ExperimentDesign();
                design.mappings ??= new List<Entry>();
                design.mappings.RemoveAll(entry => entry == null);
                return design;
            }
            catch (ArgumentException)
            {
                return new ExperimentDesign();
            }
        }
    }
}
