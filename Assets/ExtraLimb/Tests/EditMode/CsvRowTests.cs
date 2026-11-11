using System.Globalization;
using System.Threading;
using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>CSV の1行の組み立て（Step 4-1）。</summary>
    public class CsvRowTests
    {
        [Test]
        public void Values_AreSeparatedByCommas()
        {
            var row = new CsvRow().Add(12).Add("abc").Add(true).Add(false).AddEmpty(2).Add(-7L);

            Assert.That(row.ToString(), Is.EqualTo("12,abc,1,0,,,-7"));
            Assert.That(row.Count, Is.EqualTo(7));
        }

        [TestCase(1.23456, 4, "1.2346")]
        [TestCase(-1.5, 4, "-1.5000")]
        [TestCase(0.0, 2, "0.00")]
        [TestCase(-0.00001, 4, "0.0000")]
        [TestCase(2.5, 0, "3")]
        [TestCase(123456.789, 3, "123456.789")]
        [TestCase(0.000049, 4, "0.0000")]
        [TestCase(0.00006, 4, "0.0001")]
        public void Numbers_AreRoundedToFixedDecimals(double value, int decimals, string expected)
        {
            Assert.That(new CsvRow().Add(value, decimals).ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void NaNAndInfinity_AreEmpty()
        {
            var row = new CsvRow().Add(float.NaN, 3).Add(float.PositiveInfinity, 3).Add(1f, 1);

            Assert.That(row.ToString(), Is.EqualTo(",,1.0"));
        }

        [Test]
        public void Numbers_IgnoreCurrentCulture()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(new CsvRow().Add(1.25f, 2).Add(new Vector3(0.5f, -0.25f, 1f), 2).ToString(), Is.EqualTo("1.25,0.50,-0.25,1.00"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Test]
        public void Quaternion_WritesXyzw()
        {
            Assert.That(new CsvRow().Add(Quaternion.identity, 3).ToString(), Is.EqualTo("0.000,0.000,0.000,1.000"));
        }

        [Test]
        public void Strings_WithCommasQuotesOrNewlines_AreQuoted()
        {
            Assert.That(CsvRow.Escape("a,b"), Is.EqualTo("\"a,b\""));
            Assert.That(CsvRow.Escape("say \"hi\""), Is.EqualTo("\"say \"\"hi\"\"\""));
            Assert.That(CsvRow.Escape("1行目\n2行目"), Is.EqualTo("\"1行目\n2行目\""));
            Assert.That(CsvRow.Escape("右の追加肢"), Is.EqualTo("右の追加肢"));
            Assert.That(CsvRow.Escape(null), Is.EqualTo(""));
            Assert.That(new CsvRow().Add("x,y").Add("z").ToString(), Is.EqualTo("\"x,y\",z"));
        }

        [Test]
        public void Join_MakesHeaderLine()
        {
            Assert.That(CsvRow.Join(new[] { "time", "type", "a,b" }), Is.EqualTo("time,type,\"a,b\""));
        }

        [Test]
        public void Clear_StartsNewRow()
        {
            var row = new CsvRow().Add(1).Add(2);
            row.Clear().Add(3);

            Assert.That(row.ToString(), Is.EqualTo("3"));
            Assert.That(row.Count, Is.EqualTo(1));
        }
    }
}
