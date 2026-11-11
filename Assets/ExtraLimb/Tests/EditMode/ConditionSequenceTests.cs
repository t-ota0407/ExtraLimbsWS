using System.Collections.Generic;
using System.Linq;
using ExtraLimb.Core;
using ExtraLimb.Experiment;
using ExtraLimb.UI;
using NUnit.Framework;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>条件の提示順・課題の時間・口頭評価（Step 4-3）。</summary>
    public class ConditionSequenceTests
    {
        static readonly ConditionSequence.Option[] AttachPoints =
        {
            new ConditionSequence.Option("shoulder_top", "肩の上"),
            new ConditionSequence.Option("flank", "脇腹"),
            new ConditionSequence.Option("head", "頭部"),
        };

        static readonly BodySite[] Sites = { BodySite.RightAnkle, BodySite.RightKnee, BodySite.RightShoulder, BodySite.RightElbow };

        // 今の仮の型：足首は並進だけ、ほかは部位の型と並進
        static IReadOnlyList<ConditionSequence.Option> Mappings(BodySite site)
        {
            var list = new List<ConditionSequence.Option>();
            if (site == BodySite.RightKnee) list.Add(new ConditionSequence.Option("knee_shin", "膝：すねを振る"));
            if (site == BodySite.RightShoulder) list.Add(new ConditionSequence.Option("shoulder_twist", "肩：上体のひねり"));
            if (site == BodySite.RightElbow) list.Add(new ConditionSequence.Option("elbow_2axis", "肘：2軸"));
            list.Add(new ConditionSequence.Option("translate", "並進"));
            return list;
        }

        static ConditionPlan Build(string id, int? row = null) => ConditionSequence.Build(id, AttachPoints, Sites, Mappings, row);

        // ---- バランスドラテン方格 ----

        [TestCase(4)]
        [TestCase(3)]
        [TestCase(6)]
        public void LatinSquare_RowsArePermutations_AndColumnsBalanced(int n)
        {
            int rows = BalancedLatinSquare.RowCount(n);
            Assert.That(rows, Is.EqualTo(n % 2 == 0 ? n : 2 * n));
            var columnCounts = new int[n, n];
            for (int r = 0; r < rows; r++)
            {
                var row = BalancedLatinSquare.Row(n, r);
                Assert.That(row.OrderBy(v => v), Is.EqualTo(Enumerable.Range(0, n)), $"{r} 行目");
                for (int c = 0; c < n; c++) columnCounts[c, row[c]]++;
            }
            for (int c = 0; c < n; c++)
            for (int v = 0; v < n; v++)
                Assert.That(columnCounts[c, v], Is.EqualTo(rows / n), $"{c} 列目の {v}");
        }

        [TestCase(4)]
        [TestCase(3)]
        public void LatinSquare_EachOrderedPairFollowsEquallyOften(int n)
        {
            int rows = BalancedLatinSquare.RowCount(n);
            var pairs = new Dictionary<(int, int), int>();
            for (int r = 0; r < rows; r++)
            {
                var row = BalancedLatinSquare.Row(n, r);
                for (int i = 0; i + 1 < n; i++)
                {
                    var pair = (row[i], row[i + 1]);
                    pairs[pair] = pairs.TryGetValue(pair, out int count) ? count + 1 : 1;
                }
            }
            Assert.That(pairs.Count, Is.EqualTo(n * (n - 1)), "すべての「a の直後に b」が現れる");
            Assert.That(pairs.Values.Distinct().Count(), Is.EqualTo(1), "どの並びも同じ回数");
        }

        [Test]
        public void LatinSquare_FirstRowOfFour()
        {
            Assert.That(BalancedLatinSquare.Row(4, 0), Is.EqualTo(new[] { 0, 1, 3, 2 }));
            Assert.That(BalancedLatinSquare.Row(4, 5), Is.EqualTo(BalancedLatinSquare.Row(4, 1)), "行の数で折り返す");
        }

        // ---- 参加者ID ----

        [TestCase("P01", 1)]
        [TestCase("p12", 12)]
        [TestCase("P3-2", 2)]
        [TestCase("abc", -1)]
        [TestCase("", -1)]
        public void ParticipantNumber_IsLastDigits(string id, int expected)
        {
            Assert.That(ConditionSequence.ParticipantNumber(id), Is.EqualTo(expected));
        }

        [Test]
        public void DefaultRow_CyclesWithParticipantNumber()
        {
            Assert.That(ConditionSequence.DefaultRow("P01", 4), Is.EqualTo(0));
            Assert.That(ConditionSequence.DefaultRow("P04", 4), Is.EqualTo(3));
            Assert.That(ConditionSequence.DefaultRow("P05", 4), Is.EqualTo(0));
            Assert.That(ConditionSequence.DefaultRow("abc", 4), Is.InRange(0, 3));
        }

        // ---- 提示順 ----

        [Test]
        public void Plan_HasEveryCombinationOnce()
        {
            var plan = Build("P01");

            Assert.That(plan.Count, Is.EqualTo(21), "3か所 ×（足首1＋膝2＋肩2＋肘2）");
            var keys = plan.conditions.Select(c => (c.attachPointId, c.inputSite, c.mappingPresetId)).ToList();
            Assert.That(keys.Distinct().Count(), Is.EqualTo(21));
            Assert.That(plan.conditions.Select(c => c.order), Is.EqualTo(Enumerable.Range(0, 21)));
        }

        [Test]
        public void Plan_GroupsBySiteInLatinOrder()
        {
            var plan = Build("P02");

            var expected = BalancedLatinSquare.Row(4, 1).Select(i => Sites[i]).ToArray();
            Assert.That(plan.latinRow, Is.EqualTo(1));
            Assert.That(plan.blockSites, Is.EqualTo(expected));
            // 条件は部位のブロックごとに続けて並ぶ
            var sitesInOrder = plan.conditions.Select(c => c.inputSite).ToList();
            var blocks = new List<BodySite>();
            foreach (var site in sitesInOrder)
                if (blocks.Count == 0 || blocks[blocks.Count - 1] != site) blocks.Add(site);
            Assert.That(blocks, Is.EqualTo(expected));
            foreach (var spec in plan.conditions)
            {
                Assert.That(spec.inputSite, Is.EqualTo(plan.blockSites[spec.block]));
                Assert.That(spec.blockSize, Is.EqualTo(spec.inputSite == BodySite.RightAnkle ? 3 : 6));
                Assert.That(spec.indexInBlock, Is.InRange(0, spec.blockSize - 1));
            }
        }

        [Test]
        public void Plan_SameIdGivesSameOrder()
        {
            var a = Build("P07").conditions.Select(c => c.Label);
            var b = Build("P07").conditions.Select(c => c.Label);

            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void Plan_WithinBlockOrderDiffersBetweenParticipants()
        {
            // 同じブロックの順（P01 と P05）でも、ブロックの中の順は参加者で変わる
            var first = Build("P01");
            var fifth = Build("P05");
            Assert.That(fifth.blockSites, Is.EqualTo(first.blockSites));
            Assert.That(fifth.conditions.Select(c => c.Label), Is.Not.EqualTo(first.conditions.Select(c => c.Label)));
        }

        [Test]
        public void Plan_RowOverride()
        {
            var plan = Build("P01", 2);

            Assert.That(plan.latinRow, Is.EqualTo(2));
            Assert.That(plan.blockSites[0], Is.EqualTo(Sites[BalancedLatinSquare.Row(4, 2)[0]]));
        }

        [Test]
        public void Plan_SkipsSitesWithoutMappings()
        {
            var plan = ConditionSequence.Build("P01", AttachPoints, Sites,
                site => site == BodySite.RightAnkle ? new List<ConditionSequence.Option>() : Mappings(site));

            Assert.That(plan.BlockCount, Is.EqualTo(3));
            Assert.That(plan.Count, Is.EqualTo(18));
            Assert.That(plan.latinRowCount, Is.EqualTo(6), "3水準のバランスドラテン方格は6行");
        }

        [Test]
        public void Spec_CodeAndLabel()
        {
            var spec = new ConditionSpec
            {
                block = 1, indexInBlock = 2, attachPointName = "脇腹", inputSiteName = "右膝", mappingName = "並進",
            };

            Assert.That(spec.Code, Is.EqualTo("2-3"));
            Assert.That(spec.Label, Is.EqualTo("脇腹｜右膝｜並進"));
        }

        // ---- 課題の時間 ----

        static readonly TrialTiming Timing = new TrialTiming
        {
            leadMessageSeconds = 2f, countdown = 3, segmentSeconds = 60f, interludeMessageSeconds = 3f,
        };

        [Test]
        public void Timeline_CueTimes()
        {
            var timeline = new TrialTimeline();
            timeline.Start(100.0, Timing);

            Assert.That(timeline.TimeOf(TrialCue.LeadMessage), Is.EqualTo(100.0));
            Assert.That(timeline.TimeOf(TrialCue.Countdown1), Is.EqualTo(102.0));
            Assert.That(timeline.TimeOf(TrialCue.Segment1Start), Is.EqualTo(105.0));
            Assert.That(timeline.TimeOf(TrialCue.Segment1End), Is.EqualTo(165.0));
            Assert.That(timeline.TimeOf(TrialCue.InterludeMessage), Is.EqualTo(165.0));
            Assert.That(timeline.TimeOf(TrialCue.Countdown2), Is.EqualTo(168.0));
            Assert.That(timeline.TimeOf(TrialCue.Segment2Start), Is.EqualTo(171.0));
            Assert.That(timeline.TimeOf(TrialCue.Segment2End), Is.EqualTo(231.0));
        }

        [Test]
        public void Timeline_AdvanceReturnsCuesInOrder_EvenAfterALongGap()
        {
            var timeline = new TrialTimeline();
            timeline.Start(0.0, Timing);
            var cues = new List<TrialCue>();

            timeline.Advance(0.0, cues);
            Assert.That(cues, Is.EqualTo(new[] { TrialCue.LeadMessage }));
            Assert.That(timeline.Segment, Is.EqualTo(0));

            cues.Clear();
            timeline.Advance(10.0, cues);
            Assert.That(cues, Is.EqualTo(new[] { TrialCue.Countdown1, TrialCue.Segment1Start }));
            Assert.That(timeline.Segment, Is.EqualTo(1));
            Assert.That(timeline.SegmentRemaining(10.0), Is.EqualTo(55.0).Within(1e-9));

            cues.Clear();
            timeline.Advance(1000.0, cues);
            Assert.That(cues, Is.EqualTo(new[]
            {
                TrialCue.Segment1End, TrialCue.InterludeMessage, TrialCue.Countdown2, TrialCue.Segment2Start, TrialCue.Segment2End,
            }));
            Assert.That(timeline.Running, Is.False);
            Assert.That(timeline.Segment, Is.EqualTo(0));
        }

        [Test]
        public void Timeline_SegmentTwoBetweenItsStartAndEnd()
        {
            var timeline = new TrialTimeline();
            timeline.Start(0.0, Timing);
            // 後半は 71 秒から 131 秒まで（案内 2 ＋ カウントダウン 3 ＋ 前半 60 ＋ 案内 3 ＋ カウントダウン 3）
            timeline.Advance(71.5, null);

            Assert.That(timeline.Segment, Is.EqualTo(2));
            Assert.That(timeline.SegmentRemaining(71.5), Is.EqualTo(59.5).Within(1e-9));
        }

        [Test]
        public void Timeline_WithoutMessages_StartsWithCountdown()
        {
            var timeline = new TrialTimeline();
            timeline.Start(0.0, new TrialTiming { leadMessageSeconds = 0f, countdown = 3, segmentSeconds = 10f, interludeMessageSeconds = 0f });
            var cues = new List<TrialCue>();
            timeline.Advance(100.0, cues);

            Assert.That(cues, Is.EqualTo(new[]
            {
                TrialCue.Countdown1, TrialCue.Segment1Start, TrialCue.Segment1End, TrialCue.Countdown2, TrialCue.Segment2Start, TrialCue.Segment2End,
            }));
            Assert.That(timeline.TimeOf(TrialCue.Segment2Start), Is.EqualTo(16.0));
        }

        [Test]
        public void Timeline_Stop_ReturnsNoMoreCues()
        {
            var timeline = new TrialTimeline();
            timeline.Start(0.0, Timing);
            timeline.Stop();
            var cues = new List<TrialCue>();

            Assert.That(timeline.Advance(1000.0, cues), Is.Zero);
            Assert.That(timeline.Running, Is.False);
        }

        // ---- 口頭評価 ----

        [Test]
        public void Rating_EnterFillsItemsInOrder()
        {
            var rating = new ConditionRating();

            Assert.That(rating.Enter(5), Is.EqualTo(0));
            Assert.That(rating.Cursor, Is.EqualTo(1));
            Assert.That(rating.Enter(6), Is.EqualTo(1));
            Assert.That(rating.IsComplete, Is.False);
            Assert.That(rating.Enter(2), Is.EqualTo(2));
            Assert.That(rating.IsComplete, Is.True);
            Assert.That(rating.Cursor, Is.EqualTo(-1));
            Assert.That(rating.Enter(4), Is.EqualTo(-1), "全部入れた後は入らない");
            Assert.That(rating.ToString(), Is.EqualTo("5 / 6 / 2"));
        }

        [Test]
        public void Rating_RejectsOutOfRange()
        {
            var rating = new ConditionRating();

            Assert.That(rating.Set(0, 0), Is.False);
            Assert.That(rating.Set(0, 8), Is.False);
            Assert.That(rating.Set(3, 4), Is.False);
            Assert.That(rating.Enter(9), Is.EqualTo(-1));
            Assert.That(rating.Cursor, Is.EqualTo(0));
        }

        [Test]
        public void Rating_SetFillsGapThenCursorSkipsIt()
        {
            var rating = new ConditionRating();
            rating.Set(1, 3);

            Assert.That(rating.Cursor, Is.EqualTo(0));
            rating.Enter(7);
            Assert.That(rating.Cursor, Is.EqualTo(2));
        }

        // ---- HMD の文の大きさ ----

        [Test]
        public void NoticeFont_ShrinksLongText()
        {
            Assert.That(HmdNotice.FittedFontSize("条件が変わります", 110f, 8.5f), Is.EqualTo(110f));
            Assert.That(HmdNotice.FittedFontSize("3", 110f, 8.5f), Is.EqualTo(110f));
            float longText = HmdNotice.FittedFontSize("追加肢だけで触れます", 110f, 8.5f);
            Assert.That(longText, Is.LessThan(110f));
            Assert.That(longText * 10f, Is.LessThanOrEqualTo(110f * 8.5f + 0.01f), "板の幅に収まる");
        }
    }
}
