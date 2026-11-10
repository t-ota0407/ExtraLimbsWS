using System.Collections.Generic;
using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>マッピングの型の一覧と、同梱の型（Step 3-3）。</summary>
    public class MappingPresetTests
    {
        readonly List<MappingPreset> created = new List<MappingPreset>();

        MappingPreset Create(string id, BodySite site)
        {
            var preset = ScriptableObject.CreateInstance<MappingPreset>();
            preset.id = id;
            preset.displayName = id;
            preset.site = site;
            created.Add(preset);
            return preset;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var preset in created) Object.DestroyImmediate(preset);
            created.Clear();
        }

        [Test]
        public void ForSite_SpecificFirstThenGeneric()
        {
            var translate = Create("translate", BodySite.Unknown);
            var shoulder = Create("shoulder_twist", BodySite.RightShoulder);
            var elbow = Create("elbow_2axis", BodySite.RightElbow);
            var presets = new[] { translate, shoulder, elbow };

            Assert.That(MappingPresets.ForSite(presets, BodySite.RightShoulder), Is.EqualTo(new[] { shoulder, translate }));
            Assert.That(MappingPresets.ForSite(presets, BodySite.RightAnkle), Is.EqualTo(new[] { translate }));
            Assert.That(MappingPresets.Default(presets, BodySite.RightElbow), Is.SameAs(elbow));
            Assert.That(MappingPresets.Default(presets, BodySite.RightAnkle), Is.SameAs(translate));
            Assert.That(MappingPresets.Default(null, BodySite.RightAnkle), Is.Null);
        }

        [Test]
        public void Next_CyclesWithinSite()
        {
            var translate = Create("translate", BodySite.Unknown);
            var shoulder = Create("shoulder_twist", BodySite.RightShoulder);
            var presets = new[] { translate, shoulder };

            Assert.That(MappingPresets.Next(presets, BodySite.RightShoulder, "shoulder_twist"), Is.SameAs(translate));
            Assert.That(MappingPresets.Next(presets, BodySite.RightShoulder, "translate"), Is.SameAs(shoulder));
            // 今の型がその部位のものでなければ既定の型
            Assert.That(MappingPresets.Next(presets, BodySite.RightShoulder, "elbow_2axis"), Is.SameAs(shoulder));
            Assert.That(MappingPresets.Next(presets, BodySite.RightAnkle, "translate"), Is.SameAs(translate));
            Assert.That(MappingPresets.Find(presets, "shoulder_twist"), Is.SameAs(shoulder));
            Assert.That(MappingPresets.Find(presets, ""), Is.Null);
        }

        [Test]
        public void ShippedPresets_AreValid()
        {
            var presets = Resources.LoadAll<MappingPreset>("ExtraLimbMappings");
            Assert.That(presets.Length, Is.GreaterThanOrEqualTo(4));

            var ids = new HashSet<string>();
            foreach (var preset in presets)
            {
                Assert.That(ids.Add(preset.id), Is.True, $"IDが重なっている：{preset.id}");
                Assert.That(preset.displayName, Is.Not.Empty, preset.id);
                Assert.That(preset.gain, Is.GreaterThan(0f), preset.id);
                Assert.That(preset.mapping.HasDistinctChannels, Is.True, preset.id);
                Assert.That(preset.mapping.leverLength, Is.GreaterThan(0f), preset.id);
                int used = 0;
                for (int axis = 0; axis < 3; axis++)
                    if (preset.mapping[axis].IsUsed) used++;
                Assert.That(used, Is.GreaterThanOrEqualTo(2), $"{preset.id}：手先の軸を2つ以上動かす");
            }

            // 4つの入力の部位すべてに既定の型がある
            foreach (var site in new[] { BodySite.RightAnkle, BodySite.RightKnee, BodySite.RightShoulder, BodySite.RightElbow })
                Assert.That(MappingPresets.Default(presets, site), Is.Not.Null, site.ToString());

            // どの部位でも使える「並進」は、動かした向きにそのまま動く
            var translate = MappingPresets.Find(presets, "translate");
            Assert.That(translate, Is.Not.Null);
            Assert.That(translate.site, Is.EqualTo(BodySite.Unknown));
            TestMath.AreClose(new Vector3(1f, 2f, 3f), translate.mapping.Apply(new Vector3(1f, 2f, 3f)));
            Assert.That(MappingPresets.Default(presets, BodySite.RightShoulder).mapping.UsesRotation, Is.True);
        }
    }
}
