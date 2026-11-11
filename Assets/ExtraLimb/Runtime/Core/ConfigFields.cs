using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace ExtraLimb.Core
{
    /// <summary>設定の1項目の変更（例：positionGain が 2 から 2.5 へ）。</summary>
    public readonly struct FieldChange
    {
        /// <summary>項目のパス（例：axisMapping.forward.scale）。</summary>
        public readonly string path;

        public readonly string oldValue;
        public readonly string newValue;

        public FieldChange(string path, string oldValue, string newValue)
        {
            this.path = path;
            this.oldValue = oldValue;
            this.newValue = newValue;
        }

        public override string ToString() => $"{path}: {oldValue} → {newValue}";
    }

    /// <summary>
    /// 設定（LimbRuntimeConfig など、[Serializable] のクラス・構造体）を「項目のパス → 値の文字列」に平らにし、2つの差を取る（Step 4-1）。
    /// イベントログに「どの項目が何から何へ変わったか」を1項目1行で残すのに使う。
    /// 対象は public なインスタンスのフィールド（JsonUtility と同じ）で、宣言の順に並ぶ。値は文化によらない書式（小数点は「.」）。
    /// </summary>
    public static class ConfigFields
    {
        const int MaxDepth = 8;

        /// <summary>項目のパスと値の一覧（宣言の順）。</summary>
        public static List<KeyValuePair<string, string>> Flatten(object value)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (value != null) Collect(value, "", 0, result);
            return result;
        }

        /// <summary>before から after への変更（変わった項目だけ、before の宣言の順）。片方にしかない項目は、もう片方を空文字にする。</summary>
        public static List<FieldChange> Diff(object before, object after)
        {
            var oldFields = Flatten(before);
            var newFields = Flatten(after);
            var newLookup = new Dictionary<string, string>();
            foreach (var field in newFields) newLookup[field.Key] = field.Value;

            var changes = new List<FieldChange>();
            var seen = new HashSet<string>();
            foreach (var field in oldFields)
            {
                seen.Add(field.Key);
                newLookup.TryGetValue(field.Key, out string newValue);
                newValue ??= "";
                if (field.Value != newValue) changes.Add(new FieldChange(field.Key, field.Value, newValue));
            }
            foreach (var field in newFields)
                if (!seen.Contains(field.Key)) changes.Add(new FieldChange(field.Key, "", field.Value));
            return changes;
        }

        /// <summary>1つの値の文字列。数値は文化によらず、往復できる桁で書く。bool は true / false、enum は名前。</summary>
        public static string FormatValue(object value)
        {
            switch (value)
            {
                case null: return "";
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case Enum e: return e.ToString();
                case UnityEngine.Object o: return o != null ? o.name : "";
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                case IEnumerable sequence:
                {
                    var text = new StringBuilder("[");
                    bool first = true;
                    foreach (var item in sequence)
                    {
                        if (!first) text.Append(", ");
                        text.Append(FormatValue(item));
                        first = false;
                    }
                    return text.Append(']').ToString();
                }
                default: return value.ToString();
            }
        }

        static bool IsLeaf(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || typeof(UnityEngine.Object).IsAssignableFrom(type) || typeof(IEnumerable).IsAssignableFrom(type);

        static void Collect(object value, string prefix, int depth, List<KeyValuePair<string, string>> result)
        {
            var type = value.GetType();
            if (IsLeaf(type) || depth >= MaxDepth)
            {
                result.Add(new KeyValuePair<string, string>(prefix, FormatValue(value)));
                return;
            }

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            foreach (var field in fields)
            {
                if (field.IsNotSerialized) continue;
                string path = prefix.Length > 0 ? $"{prefix}.{field.Name}" : field.Name;
                var child = field.GetValue(value);
                if (child == null) result.Add(new KeyValuePair<string, string>(path, ""));
                else Collect(child, path, depth + 1, result);
            }
        }
    }
}
