using System;
using System.Collections.Generic;
using System.IO;
using ExtraLimb.Core;
using ExtraLimb.Experiment;
using ExtraLimb.Tracking;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>ログの再生（Step 4-4）：CSV の読み戻し、姿勢の補間、記録フォルダの読み込み、再生のソースの優先。</summary>
    public class ReplayTests
    {
        // ---- CSV の読み戻し ----

        [Test]
        public void Csv_SplitsQuotedFields()
        {
            var fields = CsvReader.Split("a,\"b,c\",,\"d\"\"e\"");

            Assert.That(fields, Is.EqualTo(new[] { "a", "b,c", "", "d\"e" }));
        }

        [Test]
        public void Csv_ReadsRecordsWithNewlinesInQuotes()
        {
            var reader = new StringReader("x,\"l1\nl2\"\r\nnext,1\nlast,");
            var fields = new List<string>();

            Assert.That(CsvReader.ReadRecord(reader, fields), Is.True);
            Assert.That(fields, Is.EqualTo(new[] { "x", "l1\nl2" }));
            Assert.That(CsvReader.ReadRecord(reader, fields), Is.True);
            Assert.That(fields, Is.EqualTo(new[] { "next", "1" }));
            Assert.That(CsvReader.ReadRecord(reader, fields), Is.True);
            Assert.That(fields, Is.EqualTo(new[] { "last", "" }));
            Assert.That(CsvReader.ReadRecord(reader, fields), Is.False);
        }

        [Test]
        public void Csv_RoundTripsWhatCsvRowWrites()
        {
            var row = new CsvRow().Add("右の追加肢：変更, 理由").Add(1.25, 2).Add(true).AddEmpty().Add("引用\"符\"と\n改行");

            var fields = CsvReader.Split(row.ToString());
            Assert.That(fields.Count, Is.EqualTo(5));
            Assert.That(fields[4], Is.EqualTo("引用\"符\"と\n改行"));
            Assert.That(fields[0], Is.EqualTo("右の追加肢：変更, 理由"));
            Assert.That(CsvReader.ParseDouble(fields[1]), Is.EqualTo(1.25));
            Assert.That(CsvReader.ParseBool(fields[2]), Is.True);
            Assert.That(double.IsNaN(CsvReader.ParseDouble(fields[3])), Is.True);
        }

        // ---- 姿勢の補間 ----

        static ReplayTrack Track(double[] times, bool[] ok, float[] xs)
        {
            var positions = new List<Vector3>();
            var rotations = new List<Quaternion>();
            foreach (float x in xs)
            {
                positions.Add(new Vector3(x, 0f, 0f));
                rotations.Add(Quaternion.Euler(0f, x * 100f, 0f));
            }
            return new ReplayTrack(times, ok, positions, rotations);
        }

        [Test]
        public void Track_InterpolatesBetweenFrames()
        {
            var track = Track(new[] { 0.0, 0.1, 0.2 }, new[] { true, true, true }, new[] { 0f, 0.1f, 0.2f });

            Assert.That(track.TryGetPose(0.15, out var pose), Is.True);
            TestMath.AreClose(new Vector3(0.15f, 0f, 0f), pose.position);
            Assert.That(Quaternion.Angle(pose.rotation, Quaternion.Euler(0f, 15f, 0f)), Is.LessThan(0.01f));
        }

        [Test]
        public void Track_ClampsOutsideTheRecording()
        {
            var track = Track(new[] { 1.0, 1.1 }, new[] { true, true }, new[] { 0.3f, 0.4f });

            Assert.That(track.TryGetPose(0.0, out var before), Is.True);
            TestMath.AreClose(new Vector3(0.3f, 0f, 0f), before.position);
            Assert.That(track.TryGetPose(5.0, out var after), Is.True);
            TestMath.AreClose(new Vector3(0.4f, 0f, 0f), after.position);
        }

        [Test]
        public void Track_LostFramesAreLost_AndGapsAreNotInterpolated()
        {
            var track = Track(new[] { 0.0, 0.1, 0.2, 1.0 }, new[] { true, false, true, true }, new[] { 0f, 9f, 0.2f, 1f });

            Assert.That(track.TryGetPose(0.11, out _), Is.False, "追跡が切れたフレームの近く");
            Assert.That(track.TryGetPose(0.02, out var early), Is.True);
            TestMath.AreClose(Vector3.zero, early.position);
            // 0.2 秒と 1.0 秒の間は空きすぎなので、近い方のフレームのまま
            Assert.That(track.TryGetPose(0.3, out var gap), Is.True);
            TestMath.AreClose(new Vector3(0.2f, 0f, 0f), gap.position);
            Assert.That(track.TryGetPose(0.95, out var late), Is.True);
            TestMath.AreClose(new Vector3(1f, 0f, 0f), late.position);
        }

        [Test]
        public void ReplaySource_ReadsTheClock()
        {
            double clock = 0.05;
            var track = Track(new[] { 0.0, 0.1 }, new[] { true, true }, new[] { 0f, 0.1f });
            var source = new ReplayPoseSource("replay:right_ankle", BodySite.RightAnkle, track, () => clock);

            Assert.That(source.Space, Is.EqualTo(PoseSpace.Replay));
            Assert.That(source.TryGetPose(out var first), Is.True);
            TestMath.AreClose(new Vector3(0.05f, 0f, 0f), first.position);
            clock = 0.1;
            source.TryGetPose(out var second);
            TestMath.AreClose(new Vector3(0.1f, 0f, 0f), second.position);
        }

        // ---- 再生のソースの優先 ----

        sealed class FakeSource : ITrackedPoseSource
        {
            public string Id { get; set; }
            public BodySite Site { get; set; }
            public PoseSpace Space { get; set; }

            public bool TryGetPose(out Pose pose)
            {
                pose = new Pose(Vector3.zero, Quaternion.identity);
                return true;
            }
        }

        [Test]
        public void Selector_PrefersReplay_AndUsesReplayRoom()
        {
            var registry = new TrackerRegistry();
            registry.Register(FixedPoseSource.Room(PoseSpace.SteamVR));
            registry.Register(new FakeSource { Id = "LHR-ANKLE", Site = BodySite.RightAnkle, Space = PoseSpace.SteamVR });
            Assert.That(SourceSelector.SelectInput(registry, "", BodySite.RightAnkle), Is.EqualTo("LHR-ANKLE"));

            registry.Register(FixedPoseSource.Room(PoseSpace.Replay));
            registry.Register(new FakeSource { Id = "replay:right_ankle", Site = BodySite.RightAnkle, Space = PoseSpace.Replay });

            string input = SourceSelector.SelectInput(registry, "", BodySite.RightAnkle);
            Assert.That(input, Is.EqualTo("replay:right_ankle"));
            Assert.That(SourceSelector.SelectReference(registry, "", input), Is.EqualTo("room:Replay"));
        }

        // ---- 記録フォルダの読み込み ----

        string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "ExtraLimbReplayTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        string WriteSession()
        {
            var sites = new[] { BodySite.RightAnkle, BodySite.Head };
            var columns = ExperimentRecorder.FrameColumns(sites);
            string directory;
            using (var logger = new ExperimentLogger(root, "replay", columns, new SessionInfo { label = "P09" }))
            {
                directory = logger.Directory;
                var row = new CsvRow();
                for (int i = 0; i < 3; i++)
                {
                    var values = new Dictionary<string, string>
                    {
                        ["frame"] = (10 + i).ToString(),
                        ["time"] = CsvRow.Format(0.1 * i, 4),
                        ["r_active"] = "1",
                        ["r_valid"] = "1",
                        ["r_u_r"] = CsvRow.Format(0.01 * i, 5),
                        ["r_u_u"] = "0",
                        ["r_u_f"] = "0",
                        ["right_ankle_ok"] = "1",
                        ["right_ankle_px"] = CsvRow.Format(0.1 * i, 4),
                        ["right_ankle_py"] = "0",
                        ["right_ankle_pz"] = "0",
                        ["right_ankle_qx"] = "0",
                        ["right_ankle_qy"] = "0",
                        ["right_ankle_qz"] = "0",
                        ["right_ankle_qw"] = "1",
                        ["head_ok"] = "0",
                    };
                    row.Clear();
                    foreach (string column in columns) row.Add(values.TryGetValue(column, out string value) ? value : "");
                    logger.WriteFrame(row);
                }
                // 設定の全体は、時刻の順に並んでいなくてもよい
                var later = new SessionSnapshot
                {
                    time = 0.15, reason = SessionEventTypes.Recenter,
                    limbs = new[]
                    {
                        new LimbSnapshot
                        {
                            limb = "right", active = true, stateSaved = true, recentered = true,
                            neutralPosition = new Vector3(0.1f, 0.2f, 0.3f), hasForward = true, forward = Vector3.forward,
                        },
                    },
                };
                var first = new SessionSnapshot { time = 0.0, reason = SessionEventTypes.LogStart, limbs = new[] { new LimbSnapshot { limb = "right", active = true } } };
                logger.WriteConfig(JsonUtility.ToJson(later));
                logger.WriteConfig(JsonUtility.ToJson(first));
                logger.WriteEvent(0.0, 10, new SessionEvent(SessionEventTypes.TrackerMap, "right_ankle", ChangeOrigins.System, "RightAnkle", "", "LHR-A", "右足首の列：LHR-A（SteamVR）, 説明"));
                logger.WriteEvent(0.15, 11, new SessionEvent(SessionEventTypes.Recenter, "right", ChangeOrigins.System, "input", "", "LHR-A", "1行目\n2行目"));
            }
            return directory;
        }

        [Test]
        public void Log_ReadsTracksSnapshotsAndEvents()
        {
            var log = ReplayLog.Load(WriteSession());

            Assert.That(log.Label, Is.EqualTo("P09"));
            Assert.That(log.FrameCount, Is.EqualTo(3));
            Assert.That(log.Duration, Is.EqualTo(0.2).Within(1e-9));
            Assert.That(log.Tracks.ContainsKey(BodySite.RightAnkle), Is.True);
            Assert.That(log.Tracks.ContainsKey(BodySite.Head), Is.False, "一度も追跡できなかった部位は入れない");
            Assert.That(log.Tracks[BodySite.RightAnkle].TryGetPose(0.15, out var pose), Is.True);
            TestMath.AreClose(new Vector3(0.15f, 0f, 0f), pose.position);

            Assert.That(log.OriginalIds[BodySite.RightAnkle], Is.EqualTo("LHR-A"));
            Assert.That(log.Snapshots.Count, Is.EqualTo(2));
            Assert.That(log.Snapshots[0].reason, Is.EqualTo(SessionEventTypes.LogStart), "時刻の順に並べ直す");
            Assert.That(log.HasSavedState, Is.True);
            Assert.That(log.SnapshotIndexAt(0.1), Is.EqualTo(0));
            Assert.That(log.SnapshotIndexAt(0.2), Is.EqualTo(1));
            TestMath.AreClose(new Vector3(0.1f, 0.2f, 0.3f), log.Snapshots[1].limbs[0].neutralPosition);

            Assert.That(log.Events.Count, Is.EqualTo(1));
            Assert.That(log.Events[0].type, Is.EqualTo(SessionEventTypes.Recenter));
            Assert.That(log.Events[0].subject, Is.EqualTo("right"));

            Assert.That(log.TryGetU("r_", 0.1, out var u, 0.2), Is.True);
            TestMath.AreClose(new Vector3(0.01f, 0f, 0f), u);
            Assert.That(log.TryGetU("r_", 0.19, out var held, 0.2), Is.True);
            TestMath.AreClose(new Vector3(0.01f, 0f, 0f), held, message: "u は補間せず、前のフレームの値");
            Assert.That(log.TryGetU("r_", 0.1, out _), Is.False, "次のフレームまで 0.1 秒空いている（50 ms を超える）ところは比べない");
            Assert.That(log.TryGetU("r_", 0.25, out _, 0.2), Is.False, "最後のフレームより後は比べない");
            Assert.That(log.TryGetU("l_", 0.1, out _, 0.2), Is.False, "左は動かしていない");
        }

        [Test]
        public void Log_MissingFramesFile_Throws()
        {
            Directory.CreateDirectory(root);
            Assert.Throws<FileNotFoundException>(() => ReplayLog.Load(root));
        }

        [Test]
        public void Snapshot_KeepsNeutralPoseAndForwardInJson()
        {
            var limb = new LimbSnapshot
            {
                limb = "right", stateSaved = true, recentered = true,
                neutralPosition = new Vector3(0.1f, -0.2f, 0.3f), neutralRotation = Quaternion.Euler(10f, 20f, 30f),
                hasForward = true, forward = new Vector3(0f, 0f, 1f),
            };

            var copy = JsonUtility.FromJson<LimbSnapshot>(JsonUtility.ToJson(limb));

            Assert.That(copy.stateSaved && copy.recentered && copy.hasForward, Is.True);
            TestMath.AreClose(limb.neutralPosition, copy.neutralPosition);
            Assert.That(Quaternion.Angle(limb.neutralRotation, copy.neutralRotation), Is.LessThan(0.01f));
            TestMath.AreClose(limb.forward, copy.forward);
        }
    }
}
