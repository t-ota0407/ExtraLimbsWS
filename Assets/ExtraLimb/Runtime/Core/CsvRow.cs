using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ExtraLimb.Core
{
    /// <summary>
    /// CSV の1行を組み立てる（Step 4-1）。数値は文化によらず小数点を「.」にし、決めた桁で丸める。
    /// カンマ・引用符・改行を含む文字列は引用符で囲む。毎フレーム使うので、数値は途中で文字列を作らずに書く。
    /// 値がないところ（NaN、追跡が切れているなど）は空欄にする。
    /// </summary>
    public class CsvRow
    {
        static readonly long[] Pow10 = { 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000 };

        readonly StringBuilder builder = new StringBuilder(2048);
        readonly char[] digits = new char[20];

        /// <summary>今の行に入っている値の数。</summary>
        public int Count { get; private set; }

        public CsvRow Clear()
        {
            builder.Clear();
            Count = 0;
            return this;
        }

        void Separate()
        {
            if (Count > 0) builder.Append(',');
            Count++;
        }

        /// <summary>文字列（必要なら引用符で囲む）。null は空欄。</summary>
        public CsvRow Add(string value)
        {
            Separate();
            AppendEscaped(builder, value);
            return this;
        }

        /// <summary>空欄を count 個。</summary>
        public CsvRow AddEmpty(int count = 1)
        {
            for (int i = 0; i < count; i++) Separate();
            return this;
        }

        /// <summary>真偽は 1 / 0。</summary>
        public CsvRow Add(bool value)
        {
            Separate();
            builder.Append(value ? '1' : '0');
            return this;
        }

        public CsvRow Add(int value) => Add((long)value);

        public CsvRow Add(long value)
        {
            Separate();
            if (value < 0)
            {
                builder.Append('-');
                AppendInteger(value == long.MinValue ? long.MaxValue : -value);
            }
            else
            {
                AppendInteger(value);
            }
            return this;
        }

        /// <summary>小数（decimals 桁、0〜9）。NaN と無限大は空欄。</summary>
        public CsvRow Add(float value, int decimals) => Add((double)value, decimals);

        public CsvRow Add(double value, int decimals)
        {
            Separate();
            AppendFixed(value, decimals);
            return this;
        }

        /// <summary>x, y, z の3つ。</summary>
        public CsvRow Add(Vector3 value, int decimals) => Add(value.x, decimals).Add(value.y, decimals).Add(value.z, decimals);

        /// <summary>x, y, z, w の4つ。</summary>
        public CsvRow Add(Quaternion value, int decimals) => Add(value.x, decimals).Add(value.y, decimals).Add(value.z, decimals).Add(value.w, decimals);

        public override string ToString() => builder.ToString();

        /// <summary>CSV の1つの値として書ける形にする。</summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (!NeedsQuotes(value)) return value;
            var text = new StringBuilder(value.Length + 2);
            AppendEscaped(text, value);
            return text.ToString();
        }

        /// <summary>値を並べた1行（ヘッダーなど）。</summary>
        public static string Join(IEnumerable<string> values)
        {
            var text = new StringBuilder();
            bool first = true;
            foreach (var value in values)
            {
                if (!first) text.Append(',');
                AppendEscaped(text, value);
                first = false;
            }
            return text.ToString();
        }

        /// <summary>小数を決めた桁の文字列にする（Add と同じ書式）。</summary>
        public static string Format(double value, int decimals) => new CsvRow().Add(value, decimals).ToString();

        static bool NeedsQuotes(string value)
        {
            foreach (char c in value)
                if (c == ',' || c == '"' || c == '\n' || c == '\r')
                    return true;
            return false;
        }

        static void AppendEscaped(StringBuilder text, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (!NeedsQuotes(value))
            {
                text.Append(value);
                return;
            }
            text.Append('"');
            foreach (char c in value)
            {
                if (c == '"') text.Append('"');
                text.Append(c);
            }
            text.Append('"');
        }

        void AppendFixed(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            decimals = Mathf.Clamp(decimals, 0, Pow10.Length - 1);
            long scale = Pow10[decimals];
            double scaled = Math.Round(Math.Abs(value) * scale, MidpointRounding.AwayFromZero);
            if (scaled >= 9e17)
            {
                builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            long n = (long)scaled;
            // 丸めて0になる負の数は「-0.0000」にしない
            if (value < 0 && n != 0) builder.Append('-');
            AppendInteger(n / scale);
            if (decimals == 0) return;
            builder.Append('.');
            long fraction = n % scale;
            for (int i = decimals - 1; i >= 0; i--) builder.Append((char)('0' + fraction / Pow10[i] % 10));
        }

        void AppendInteger(long n)
        {
            if (n == 0)
            {
                builder.Append('0');
                return;
            }
            int length = 0;
            while (n > 0 && length < digits.Length)
            {
                digits[length++] = (char)('0' + n % 10);
                n /= 10;
            }
            for (int i = length - 1; i >= 0; i--) builder.Append(digits[i]);
        }
    }
}
