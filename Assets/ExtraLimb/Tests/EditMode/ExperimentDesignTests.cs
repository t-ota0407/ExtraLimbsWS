using System.Collections.Generic;
using System.Linq;
using ExtraLimb.Core;
using ExtraLimb.Experiment;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>実験モードで入力部位ごとに使う型（ExperimentDesign）と、型を1つに絞った12条件の提示順（2026-11-12）。</summary>
    public class ExperimentDesignTests
    {
        static readonly BodySite[] Sites = { BodySite.RightAnkle, BodySite.RightKnee, BodySite.RightShoulder, BodySite.RightElbow };

        static readonly ConditionSequence.Option[] AttachPoints =
        {
            new ConditionSequence.Option("shoulder_top", "肩の上"),
            new ConditionSequence.Option("flank", "脇腹"),
            new ConditionSequence.Option("head", "頭部"),
        };

        readonly List<MappingPreset> created = new List<MappingPreset>();

        MappingPreset Preset(string id, BodySite site)
        {
            var preset = ScriptableObject.CreateInstance<MappingPreset>();
            preset.id = id;
            preset.displayName = id;
            preset.site = site;
            created.Add(preset);
            return preset;
        }

        // 今の仮の型：足首は並進だけ、膝・肩・肘は並進と部位の型
        List<MappingPreset> Presets() => new List<MappingPreset>
        {
            Preset("ankle_translate", BodySite.RightAnkle),
            Preset("knee_translate", BodySite.RightKnee),
            Preset("knee_shin", BodySite.RightKnee),
            Preset("shoulder_translate", BodySite.RightShoulder),
            Preset("shoulder_twist", BodySite.RightShoulder),
            Preset("elbow_translate", BodySite.RightElbow),
            Preset("elbow_2axis", BodySite.RightElbow),
        };

        [TearDown]
        public void TearDown()
        {
            foreach (var preset in created) Object.DestroyImmediate(preset);
            created.Clear();
        }

        [Test]
        public void Resolve_WithoutChoice_UsesFirstPresetOfSite()
        {
            var presets = Presets();
            var design = new ExperimentDesign();
            Assert.That(design.Resolve(presets, BodySite.RightKnee).id, Is.EqualTo("knee_translate"));
            Assert.That(design.Resolve(presets, BodySite.RightAnkle).id, Is.EqualTo("ankle_translate"));
            Assert.That(design.Resolve(presets, BodySite.Waist), Is.Null);
        }

        [Test]
        public void Set_ChoosesPresetPerSite_AndReportsChange()
        {
            var presets = Presets();
            var design = new ExperimentDesign();
            Assert.That(design.Set(BodySite.RightKnee, "knee_shin"), Is.True);
            Assert.That(design.Set(BodySite.RightKnee, "knee_shin"), Is.False);
            Assert.That(design.Resolve(presets, BodySite.RightKnee).id, Is.EqualTo("knee_shin"));
            Assert.That(design.Resolve(presets, BodySite.RightShoulder).id, Is.EqualTo("shoulder_translate"));
            Assert.That(design.IsChosen(presets, BodySite.RightKnee, "knee_shin"), Is.True);
            Assert.That(design.IsChosen(presets, BodySite.RightKnee, "knee_translate"), Is.False);
        }

        [Test]
        public void Resolve_UnknownOrOtherSitePreset_FallsBackToDefault()
        {
            var presets = Presets();
            var design = new ExperimentDesign();
            design.Set(BodySite.RightElbow, "knee_shin");
            Assert.That(design.Resolve(presets, BodySite.RightElbow).id, Is.EqualTo("elbow_translate"));
            design.Set(BodySite.RightElbow, "removed_preset");
            Assert.That(design.Resolve(presets, BodySite.RightElbow).id, Is.EqualTo("elbow_translate"));
        }

        [Test]
        public void Json_RoundTrips_AndBrokenJsonGivesEmptyDesign()
        {
            var design = new ExperimentDesign();
            design.Set(BodySite.RightShoulder, "shoulder_twist");
            design.Set(BodySite.RightElbow, "elbow_2axis");
            var loaded = ExperimentDesign.FromJson(design.ToJson());
            Assert.That(loaded.Get(BodySite.RightShoulder), Is.EqualTo("shoulder_twist"));
            Assert.That(loaded.Get(BodySite.RightElbow), Is.EqualTo("elbow_2axis"));
            Assert.That(loaded.Get(BodySite.RightKnee), Is.EqualTo(""));
            Assert.That(ExperimentDesign.FromJson("").mappings, Is.Empty);
            Assert.That(ExperimentDesign.FromJson("{ not json").mappings, Is.Empty);
        }

        [Test]
        public void ShortName_DropsSitePrefix()
        {
            var shin = Preset("knee_shin", BodySite.RightKnee);
            shin.displayName = "膝：すねを振る";
            var translate = Preset("translate", BodySite.Unknown);
            translate.displayName = "並進";
            Assert.That(MappingPresets.ShortName(shin), Is.EqualTo("すねを振る"));
            Assert.That(MappingPresets.ShortName(translate), Is.EqualTo("並進"));
            Assert.That(MappingPresets.ShortName(null), Is.EqualTo(""));
        }

        [Test]
        public void Plan_WithOnePresetPerSite_Has12ConditionsIn4BlocksOf3()
        {
            var presets = Presets();
            var design = new ExperimentDesign();
            design.Set(BodySite.RightKnee, "knee_shin");
            var plan = ConditionSequence.Build("P01", AttachPoints, Sites, site =>
            {
                var preset = design.Resolve(presets, site);
                return new List<ConditionSequence.Option> { new ConditionSequence.Option(preset.id, preset.displayName) };
            });
            Assert.That(plan.Count, Is.EqualTo(12));
            Assert.That(plan.BlockCount, Is.EqualTo(4));
            foreach (var block in plan.conditions.GroupBy(c => c.block))
            {
                Assert.That(block.Count(), Is.EqualTo(3), $"ブロック {block.Key}");
                Assert.That(block.Select(c => c.inputSite).Distinct().Count(), Is.EqualTo(1), "ブロックの中は同じ入力部位");
                Assert.That(block.Select(c => c.attachPointId).OrderBy(id => id), Is.EqualTo(AttachPoints.Select(p => p.id).OrderBy(id => id)),
                    "ブロックの中で生やす位置3つを1回ずつ");
                Assert.That(block.All(c => c.blockSize == 3), Is.True);
            }
            Assert.That(plan.conditions.Where(c => c.inputSite == BodySite.RightKnee).All(c => c.mappingPresetId == "knee_shin"), Is.True);
            Assert.That(plan.conditions.Where(c => c.inputSite == BodySite.RightShoulder).All(c => c.mappingPresetId == "shoulder_translate"), Is.True);
        }
    }
}
