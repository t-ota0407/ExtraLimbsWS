using System;
using System.IO;
using System.Linq;
using System.Text;
using ExtraLimb.Core;
using ExtraLimb.Experiment;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>記録のファイル（Step 4-1）。一時フォルダに書いて、中身を読み直す。</summary>
    public class ExperimentLoggerTests
    {
        string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "ExtraLimbLoggerTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        static readonly string[] Columns = { "frame", "time", "value" };

        [Test]
        public void Open_CreatesSessionFolderWithHeaders()
        {
            using (var logger = new ExperimentLogger(root, "20261111_120000_P01", Columns))
            {
                Assert.That(logger.Directory, Is.EqualTo(Path.GetFullPath(Path.Combine(root, "20261111_120000_P01"))));
                Assert.That(logger.IsOpen, Is.True);
            }

            string directory = Path.Combine(root, "20261111_120000_P01");
            Assert.That(File.ReadAllLines(Path.Combine(directory, ExperimentLogger.FramesFileName), Encoding.UTF8)[0], Is.EqualTo("frame,time,value"));
            Assert.That(File.ReadAllLines(Path.Combine(directory, ExperimentLogger.EventsFileName), Encoding.UTF8)[0],
                Is.EqualTo(string.Join(",", ExperimentLogger.EventColumns)));
            Assert.That(File.Exists(Path.Combine(directory, ExperimentLogger.ConfigHistoryFileName)), Is.True);
            Assert.That(File.Exists(Path.Combine(directory, ExperimentLogger.SessionFileName)), Is.True);
        }

        [Test]
        public void CsvFiles_StartWithUtf8Bom()
        {
            string directory;
            using (var logger = new ExperimentLogger(root, "bom", Columns)) directory = logger.Directory;

            var bytes = File.ReadAllBytes(Path.Combine(directory, ExperimentLogger.EventsFileName));
            Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        }

        [Test]
        public void SameName_GetsNumberedFolder()
        {
            using (var first = new ExperimentLogger(root, "same", Columns))
            using (var second = new ExperimentLogger(root, "same", Columns))
            {
                Assert.That(Path.GetFileName(first.Directory), Is.EqualTo("same"));
                Assert.That(Path.GetFileName(second.Directory), Is.EqualTo("same_2"));
            }
        }

        [Test]
        public void FramesEventsAndConfigs_AreWrittenAsRows()
        {
            string directory;
            using (var logger = new ExperimentLogger(root, "rows", Columns))
            {
                var row = new CsvRow();
                logger.WriteFrame(row.Clear().Add(1).Add(0.0, 4).Add(1.5f, 2));
                logger.WriteFrame(row.Clear().Add(2).Add(0.0111, 4).AddEmpty());
                logger.WriteEvent(0.5, 2, new SessionEvent(SessionEventTypes.Config, "right", ChangeOrigins.Key("F1"), "attachPointId", "shoulder_top", "flank", "右の追加肢：生やす位置を変更, 理由つき"));
                logger.WriteConfig("{\"revision\":1,\n\"twoLimbs\":false}");
                Assert.That(logger.FrameRows, Is.EqualTo(2));
                Assert.That(logger.EventRows, Is.EqualTo(1));
                Assert.That(logger.ConfigRows, Is.EqualTo(1));
                directory = logger.Directory;
            }

            var frames = File.ReadAllLines(Path.Combine(directory, ExperimentLogger.FramesFileName), Encoding.UTF8);
            Assert.That(frames, Is.EqualTo(new[] { "frame,time,value", "1,0.0000,1.50", "2,0.0111," }));

            var events = File.ReadAllLines(Path.Combine(directory, ExperimentLogger.EventsFileName), Encoding.UTF8);
            Assert.That(events.Length, Is.EqualTo(2));
            Assert.That(events[1], Does.StartWith("0.5000,"));
            Assert.That(events[1], Does.EndWith(",config,right,key:F1,attachPointId,shoulder_top,flank,\"右の追加肢：生やす位置を変更, 理由つき\""));

            var configs = File.ReadAllLines(Path.Combine(directory, ExperimentLogger.ConfigHistoryFileName), Encoding.UTF8);
            Assert.That(configs, Is.EqualTo(new[] { "{\"revision\":1,\"twoLimbs\":false}" }));
        }

        [Test]
        public void Close_WritesSessionInfoWithCounts()
        {
            string directory;
            var info = new SessionInfo { scene = "Sandbox", label = "P01" };
            using (var logger = new ExperimentLogger(root, "info", Columns, info))
            {
                logger.WriteFrame(new CsvRow().Add(1).Add(0.0, 4).Add(0f, 1));
                directory = logger.Directory;
            }

            var saved = JsonUtility.FromJson<SessionInfo>(File.ReadAllText(Path.Combine(directory, ExperimentLogger.SessionFileName)));
            Assert.That(saved.sessionName, Is.EqualTo("info"));
            Assert.That(saved.scene, Is.EqualTo("Sandbox"));
            Assert.That(saved.frameRows, Is.EqualTo(1));
            Assert.That(saved.frameColumns, Is.EqualTo(Columns));
            Assert.That(saved.startedAt, Is.Not.Empty);
            Assert.That(saved.endedAt, Is.Not.Empty);
        }

        [Test]
        public void SaveJson_DoesNotOverwrite()
        {
            using (var logger = new ExperimentLogger(root, "json", Columns))
            {
                string first = logger.SaveJson("config_120000", "{}");
                string second = logger.SaveJson("config_120000", "{\"a\":1}");

                Assert.That(Path.GetFileName(first), Is.EqualTo("config_120000.json"));
                Assert.That(Path.GetFileName(second), Is.EqualTo("config_120000_2.json"));
                Assert.That(File.ReadAllText(first), Is.EqualTo("{}"));
            }
        }

        [Test]
        public void TableRows_CreateFileWithHeaderOnFirstRow()
        {
            string directory;
            var columns = new[] { "order", "status", "rating_ease" };
            using (var logger = new ExperimentLogger(root, "table", Columns, new SessionInfo { label = "P01" }))
            {
                Assert.That(logger.Label, Is.EqualTo("P01"));
                directory = logger.Directory;
                Assert.That(File.Exists(Path.Combine(directory, "conditions.csv")), Is.False, "書くまでは作らない");
                var row = new CsvRow();
                logger.WriteTableRow("conditions.csv", columns, row.Clear().Add(1).Add("done").Add(5));
                logger.WriteTableRow("conditions.csv", columns, row.Clear().Add("practice").Add("aborted").AddEmpty());
            }

            string path = Path.Combine(directory, "conditions.csv");
            Assert.That(File.ReadAllBytes(path).Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(File.ReadAllLines(path, Encoding.UTF8), Is.EqualTo(new[] { "order,status,rating_ease", "1,done,5", "practice,aborted," }));
        }

        [Test]
        public void ConditionColumns_AreUnique()
        {
            var columns = ConditionRunner.ConditionColumns;

            Assert.That(columns.Distinct().Count(), Is.EqualTo(columns.Length));
            Assert.That(columns, Does.Contain("seg1_hits").And.Contain("seg2_hits").And.Contain("rating_fatigue"));
        }

        [Test]
        public void WritingAfterClose_IsIgnored()
        {
            var logger = new ExperimentLogger(root, "closed", Columns);
            logger.Close();

            Assert.DoesNotThrow(() => logger.WriteFrame(new CsvRow().Add(1)));
            Assert.DoesNotThrow(() => logger.WriteEvent(0, 0, new SessionEvent("marker", "session", "api")));
            Assert.That(logger.IsOpen, Is.False);
            Assert.That(logger.FrameRows, Is.EqualTo(0));
        }

        [Test]
        public void SessionName_UsesTimeAndSanitizedLabel()
        {
            var time = new DateTime(2026, 11, 11, 9, 5, 3);

            Assert.That(ExperimentLogger.SessionName(time), Is.EqualTo("20261111_090503"));
            Assert.That(ExperimentLogger.SessionName(time, "P 01/a"), Is.EqualTo("20261111_090503_P_01_a"));
            Assert.That(ExperimentLogger.SessionName(time, "  "), Is.EqualTo("20261111_090503"));
        }

        [Test]
        public void FrameColumns_AreUniqueAndCoverBothLimbsAndSites()
        {
            var sites = new[] { BodySite.RightAnkle, BodySite.Head };
            var columns = ExperimentRecorder.FrameColumns(sites);

            Assert.That(columns.Distinct().Count(), Is.EqualTo(columns.Count));
            Assert.That(columns.Take(5), Is.EqualTo(new[] { "frame", "time", "unix_ms", "dt", "rev" }));
            Assert.That(columns, Does.Contain("r_move_r").And.Contain("l_twist_r").And.Contain("r_held").And.Contain("hand_l_grip"));
            Assert.That(columns, Does.Contain("right_ankle_qw").And.Contain("head_px"));
            // 5（共通）＋ 21 × 2（追加肢）＋ 6（本来の手）＋ 8 × 2（部位）
            Assert.That(columns.Count, Is.EqualTo(5 + 21 * 2 + 6 + 8 * 2));
        }

        [Test]
        public void WithBothSides_AddsOppositeSideOnce()
        {
            var configured = new[]
            {
                BodySite.RightAnkle, BodySite.RightKnee, BodySite.RightShoulder, BodySite.RightElbow,
                BodySite.Waist, BodySite.Head, BodySite.LeftHand, BodySite.RightHand,
            };
            var sites = ExperimentRecorder.WithBothSides(configured);

            Assert.That(sites.Take(8), Is.EqualTo(configured), "設定した部位はその順のまま");
            Assert.That(sites.Skip(8), Is.EqualTo(new[] { BodySite.LeftAnkle, BodySite.LeftKnee, BodySite.LeftShoulder, BodySite.LeftElbow }),
                "左の足首・膝・肩・肘を足す（左右の手はもうある）");
            Assert.That(ExperimentRecorder.WithBothSides(new[] { BodySite.LeftKnee, BodySite.LeftKnee, BodySite.Unknown }),
                Is.EqualTo(new[] { BodySite.LeftKnee, BodySite.RightKnee }));
            Assert.That(ExperimentRecorder.WithBothSides(null), Is.Empty);
            // 5（共通）＋ 21 × 2（追加肢）＋ 6（本来の手）＋ 8 × 12（部位）
            Assert.That(ExperimentRecorder.FrameColumns(sites).Count, Is.EqualTo(149));
        }

        [Test]
        public void SiteColumnName_IsSnakeCase()
        {
            Assert.That(ExperimentRecorder.SiteColumnName(BodySite.RightAnkle), Is.EqualTo("right_ankle"));
            Assert.That(ExperimentRecorder.SiteColumnName(BodySite.Waist), Is.EqualTo("waist"));
            Assert.That(ExperimentRecorder.SiteColumnName(BodySite.LeftHand), Is.EqualTo("left_hand"));
        }
    }
}
