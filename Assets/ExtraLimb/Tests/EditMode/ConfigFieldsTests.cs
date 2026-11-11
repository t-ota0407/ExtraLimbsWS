using System.Globalization;
using System.Linq;
using System.Threading;
using ExtraLimb.Core;
using NUnit.Framework;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>設定の項目の一覧と差（Step 4-1。イベントログの config の行に使う）。</summary>
    public class ConfigFieldsTests
    {
        [Test]
        public void Flatten_ListsNestedFieldsWithPaths()
        {
            var fields = ConfigFields.Flatten(new LimbRuntimeConfig()).ToDictionary(f => f.Key, f => f.Value);

            Assert.That(fields["attachPointId"], Is.EqualTo("shoulder_top"));
            Assert.That(fields["inputSite"], Is.EqualTo("RightAnkle"));
            Assert.That(fields["positionGain"], Is.EqualTo("2"));
            Assert.That(fields["axisMapping.right.channel"], Is.EqualTo("MoveRight"));
            Assert.That(fields["axisMapping.forward.scale"], Is.EqualTo("1"));
            Assert.That(fields["axisMapping.leverLength"], Is.EqualTo("0.3"));
            Assert.That(fields["grasp.hand"], Is.EqualTo("Right"));
            Assert.That(fields["filter.enabled"], Is.EqualTo("true"));
            // 計算用のプロパティ（DeadzoneMeters など）は入らない
            Assert.That(fields.ContainsKey("DeadzoneMeters"), Is.False);
        }

        [Test]
        public void Flatten_KeepsDeclarationOrder()
        {
            var keys = ConfigFields.Flatten(new LimbRuntimeConfig()).Select(f => f.Key).ToList();

            Assert.That(keys[0], Is.EqualTo("attachPointId"));
            Assert.That(keys.IndexOf("inputSite"), Is.LessThan(keys.IndexOf("positionGain")));
            Assert.That(keys.IndexOf("axisMapping.right.channel"), Is.LessThan(keys.IndexOf("axisMapping.up.channel")));
        }

        [Test]
        public void Diff_OfEqualConfigs_IsEmpty()
        {
            var config = new LimbRuntimeConfig();

            Assert.That(ConfigFields.Diff(config, config.Clone()), Is.Empty);
        }

        [Test]
        public void Diff_ListsOnlyChangedFields()
        {
            var before = new LimbRuntimeConfig();
            var after = before.Clone();
            after.positionGain = 2.5f;
            after.axisMapping = after.axisMapping.WithInverted(2);
            after.grasp.hand = ControllerHand.Left;

            var changes = ConfigFields.Diff(before, after);

            Assert.That(changes.Select(c => c.path), Is.EqualTo(new[] { "positionGain", "axisMapping.forward.scale", "grasp.hand" }));
            Assert.That(changes[0].oldValue, Is.EqualTo("2"));
            Assert.That(changes[0].newValue, Is.EqualTo("2.5"));
            Assert.That(changes[1].newValue, Is.EqualTo("-1"));
            Assert.That(changes[2].oldValue, Is.EqualTo("Right"));
            Assert.That(changes[2].newValue, Is.EqualTo("Left"));
        }

        [Test]
        public void Diff_EmptyAndNullStringsAreEqual()
        {
            var before = new LimbRuntimeConfig { referenceSourceId = "" };
            var after = before.Clone();
            after.referenceSourceId = null;

            Assert.That(ConfigFields.Diff(before, after), Is.Empty);
        }

        [Test]
        public void FormatValue_IgnoresCurrentCulture()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            try
            {
                // 小数点がカンマの文化でも「.」で書く
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(ConfigFields.FormatValue(1.5f), Is.EqualTo("1.5"));
                Assert.That(ConfigFields.FormatValue(0.25), Is.EqualTo("0.25"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Test]
        public void FormatValue_WritesBoolsEnumsAndNull()
        {
            Assert.That(ConfigFields.FormatValue(true), Is.EqualTo("true"));
            Assert.That(ConfigFields.FormatValue(BodySite.RightKnee), Is.EqualTo("RightKnee"));
            Assert.That(ConfigFields.FormatValue(null), Is.EqualTo(""));
            Assert.That(ConfigFields.FormatValue(new[] { 1, 2 }), Is.EqualTo("[1, 2]"));
        }
    }
}
