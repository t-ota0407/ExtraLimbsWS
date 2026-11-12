using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ExtraLimb.Core
{
    /// <summary>
    /// CSV を1行（1レコード）ずつ読む（Step 4-4、ログの再生）。CsvRow が書いた形式を読み戻す：
    /// カンマ区切り、引用符で囲んだ値（中の "" は1つの "、カンマや改行を含んでよい）、空欄。
    /// </summary>
    public static class CsvReader
    {
        /// <summary>次のレコードを fields に読む（前の中身は消す）。ファイルの終わりなら false。</summary>
        public static bool ReadRecord(TextReader reader, List<string> fields, StringBuilder buffer = null)
        {
            fields.Clear();
            int c = reader.Read();
            if (c < 0) return false;
            buffer ??= new StringBuilder(64);
            buffer.Clear();
            bool quoted = false, inQuotes = false;
            while (true)
            {
                if (c < 0)
                {
                    fields.Add(buffer.ToString());
                    return true;
                }
                char ch = (char)c;
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (reader.Peek() == '"')
                        {
                            reader.Read();
                            buffer.Append('"');
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        buffer.Append(ch);
                    }
                }
                else if (ch == '"' && buffer.Length == 0 && !quoted)
                {
                    inQuotes = quoted = true;
                }
                else if (ch == ',')
                {
                    fields.Add(buffer.ToString());
                    buffer.Clear();
                    quoted = false;
                }
                else if (ch == '\n' || ch == '\r')
                {
                    if (ch == '\r' && reader.Peek() == '\n') reader.Read();
                    fields.Add(buffer.ToString());
                    return true;
                }
                else
                {
                    buffer.Append(ch);
                }
                c = reader.Read();
            }
        }

        /// <summary>1行の文字列を値に分ける（改行を含む値がない行用）。</summary>
        public static List<string> Split(string line)
        {
            var fields = new List<string>();
            if (line == null) return fields;
            using (var reader = new StringReader(line)) ReadRecord(reader, fields);
            return fields;
        }

        /// <summary>数値として読む（小数点は「.」）。空欄や読めないときは NaN。</summary>
        public static double ParseDouble(string text) =>
            !string.IsNullOrEmpty(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : double.NaN;

        /// <summary>真偽として読む（1 / true）。</summary>
        public static bool ParseBool(string text) => text == "1" || text == "true" || text == "True";
    }
}
