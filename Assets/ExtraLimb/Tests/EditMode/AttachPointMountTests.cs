using System.Collections.Generic;
using System.Linq;
using ExtraLimb.Body;
using ExtraLimb.Limb;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>
    /// Attach Point の定義と付け替え（Step 3-1）。肩の上版モデルに、定義アセット3つ（肩の上・脇腹・頭部）を使って付ける。
    /// 3つの位置別モデルは、追加肢と体のメッシュが同じで、上腕の付け根の位置だけが違う。
    /// そのため付け根を各モデルと同じ位置に置けば、見た目は各モデルを使ったときと同じになる。
    /// </summary>
    public class AttachPointMountTests
    {
        const string ModelFolder = "Assets/Models/";
        const string DefinitionFolder = "Assets/ExtraLimb/Presets/AttachPoints";
        static readonly string[] ExpectedIds = { "shoulder_top", "flank", "head" };

        readonly List<GameObject> created = new List<GameObject>();
        GameObject avatar;
        BodyFrameProvider body;
        AttachPointRegistry registry;

        static List<AttachPointDefinition> LoadDefinitions()
        {
            return AssetDatabase.FindAssets("t:AttachPointDefinition", new[] { DefinitionFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .Select(AssetDatabase.LoadAssetAtPath<AttachPointDefinition>)
                .ToList();
        }

        GameObject Instantiate(string model)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + model + ".fbx"));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            created.Add(go);
            return go;
        }

        static Transform Find(GameObject root, string boneName) =>
            root.GetComponentsInChildren<Transform>(true).First(t => t.name == boneName);

        [SetUp]
        public void SetUp()
        {
            avatar = Instantiate("ExtraLimbsAvatar_Shoulder");
            body = avatar.AddComponent<BodyFrameProvider>();
            Assert.That(body.Initialize(), Is.True);
            registry = avatar.AddComponent<AttachPointRegistry>();
            registry.Configure(body, LoadDefinitions());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created)
                if (go != null) Object.DestroyImmediate(go);
            created.Clear();
        }

        (ExtraLimbRig rig, LimbMountController mount) CreateLimb(LimbSide side, string initialId = "shoulder_top")
        {
            var rig = avatar.AddComponent<ExtraLimbRig>();
            rig.Configure(avatar.transform, side);
            Assert.That(rig.Initialize(), Is.True);
            var mount = avatar.AddComponent<LimbMountController>();
            mount.Configure(rig, registry, initialId);
            Assert.That(mount.Initialize(), Is.True);
            return (rig, mount);
        }

        Vector3 ModelJoint(string model, string boneName = "sub_RightArm") => Find(Instantiate(model), boneName).position;

        [Test]
        public void Definitions_AreThreeInHotkeyOrder()
        {
            var definitions = LoadDefinitions();

            Assert.That(definitions.Select(d => d.id), Is.EqualTo(ExpectedIds));
            Assert.That(definitions.All(d => !string.IsNullOrEmpty(d.displayName)), Is.True);
        }

        [Test]
        public void ShoulderTop_MatchesShoulderModel()
        {
            var expected = ModelJoint("ExtraLimbsAvatar_Shoulder");
            var (rig, mount) = CreateLimb(LimbSide.Right);

            Assert.That(mount.CurrentId, Is.EqualTo("shoulder_top"));
            TestMath.AreClose(expected, rig.UpperArm.position, 1e-3f);
            TestMath.AreClose(Quaternion.identity, rig.Frame.rotation, 0.5f, "Tポーズでは根元の座標が体の向きとそろう");
        }

        [TestCase("head", "ExtraLimbsAvatar_Head")]
        [TestCase("flank", "ExtraLimbsAvatar_Flank")]
        public void AttachPoint_MatchesBlenderModel(string id, string model)
        {
            var expectedRight = ModelJoint(model);
            var expectedLeft = ModelJoint(model, "sub_LeftArm");
            var (right, rightMount) = CreateLimb(LimbSide.Right, id);
            var (left, leftMount) = CreateLimb(LimbSide.Left, id);

            Assert.That(rightMount.CurrentId, Is.EqualTo(id));
            TestMath.AreClose(expectedRight, right.UpperArm.position, 1e-3f, "右");
            TestMath.AreClose(expectedLeft, left.UpperArm.position, 1e-3f, "左（x を反転）");
        }

        [Test]
        public void EveryAttachPoint_RestHandIsReachable()
        {
            var (rig, mount) = CreateLimb(LimbSide.Right);
            var workspace = rig.CreateWorkspace();
            foreach (var id in ExpectedIds)
            {
                Assert.That(mount.MountTo(id, immediate: true), Is.True, id);
                Assert.That(workspace.Contains(rig.RestHandPosition), Is.True, $"{id} の手首の既定位置が届く範囲の中");

                rig.SolveLocal(rig.RestHandPosition);

                TestMath.AreClose(rig.FrameToWorld(rig.RestHandPosition), rig.Hand.position, 1e-3f, id);
            }
        }

        [Test]
        public void Switching_MovesSmoothly_WithoutJump()
        {
            var (rig, mount) = CreateLimb(LimbSide.Right);
            mount.TransitionSeconds = 0.35f;
            int completed = 0;
            mount.MountCompleted += _ => completed++;
            Assert.That(registry.TryComputeMount(registry.Get(1), false, out var flank), Is.True);
            var start = rig.UpperArm.position;

            Assert.That(mount.MountTo("flank"), Is.True);
            Assert.That(mount.IsTransitioning, Is.True);

            const float dt = 1f / 90f;
            mount.Step(dt);
            Assert.That(Vector3.Distance(start, rig.UpperArm.position), Is.LessThan(0.01f), "最初のフレームはほとんど動かない");

            var previous = rig.UpperArm.position;
            float maxStep = 0f;
            for (int i = 0; i < 100 && mount.IsTransitioning; i++)
            {
                mount.Step(dt);
                maxStep = Mathf.Max(maxStep, Vector3.Distance(previous, rig.UpperArm.position));
                previous = rig.UpperArm.position;
            }

            float distance = Vector3.Distance(start, flank.joint);
            Assert.That(mount.IsTransitioning, Is.False);
            Assert.That(maxStep, Is.LessThan(distance / (0.35f * 90f) * 1.6f), "1フレームの移動は平均の1.6倍以下");
            TestMath.AreClose(flank.joint, rig.UpperArm.position, 1e-3f);
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test]
        public void SwitchingDuringTransition_StartsFromCurrentPosition()
        {
            var (rig, mount) = CreateLimb(LimbSide.Right);
            mount.TransitionSeconds = 0.35f;
            mount.MountTo("flank");
            for (int i = 0; i < 15; i++) mount.Step(1f / 90f);
            var middle = rig.UpperArm.position;

            mount.MountTo("head");
            mount.Step(1f / 90f);

            Assert.That(Vector3.Distance(middle, rig.UpperArm.position), Is.LessThan(0.01f));
        }

        [Test]
        public void HiddenHead_DoesNotShrinkHeadMountedLimb()
        {
            var (rig, mount) = CreateLimb(LimbSide.Right, "head");
            var expected = rig.UpperArm.position;
            var head = body.Animator.GetBoneTransform(HumanBodyBones.Head);

            head.localScale = Vector3.one * 1e-4f;
            mount.Step(0.01f);

            TestMath.AreClose(expected, rig.UpperArm.position, 1e-3f);
            Assert.That(rig.UpperArm.lossyScale.x, Is.EqualTo(1f).Within(1e-3f));
        }

        // 2026-11-13：追加肢は生やした部位について動く。部位の骨を動かす・回すと、トラッカーを動かしていなくても
        // 手先（の基準位置）と根元の座標の向きが、その骨から見て同じところに留まる
        [TestCase("head", HumanBodyBones.Head, false)]
        [TestCase("head", HumanBodyBones.Head, true)]
        [TestCase("shoulder_top", HumanBodyBones.UpperChest, true)]
        [TestCase("flank", HumanBodyBones.UpperChest, true)]
        public void LimbFrame_FollowsAttachSiteBone(string id, HumanBodyBones bone, bool rotate)
        {
            var (rig, mount) = CreateLimb(LimbSide.Right, id);
            mount.Step(0.01f);
            var site = body.Animator.GetBoneTransform(bone);
            var restLocal = site.InverseTransformPoint(rig.FrameToWorld(rig.RestHandPosition));
            var frameLocal = Quaternion.Inverse(site.rotation) * rig.Frame.rotation;
            var restWorldBefore = rig.FrameToWorld(rig.RestHandPosition);

            site.position += new Vector3(0.04f, -0.03f, 0.05f);
            if (rotate) site.rotation = Quaternion.AngleAxis(35f, Vector3.up) * Quaternion.AngleAxis(20f, Vector3.right) * site.rotation;
            mount.Step(0.01f);

            var restWorld = rig.FrameToWorld(rig.RestHandPosition);
            Assert.That(Vector3.Distance(restWorld, restWorldBefore), Is.GreaterThan(0.04f), "部位を動かすと手先の基準位置も動く");
            float error = Vector3.Distance(site.InverseTransformPoint(restWorld), restLocal);
            Assert.That(error, Is.LessThan(1e-3f), $"部位から見た手先の基準位置のずれ {error * 100f:F1} cm");
            float angle = Quaternion.Angle(frameLocal, Quaternion.Inverse(site.rotation) * rig.Frame.rotation);
            Assert.That(angle, Is.LessThan(0.5f), $"部位から見た根元の座標の向きのずれ {angle:F1}°");
        }

        [Test]
        public void BodyDimensions_ScaleAvatarAndAttachPoints()
        {
            var expected = ModelJoint("ExtraLimbsAvatar_Shoulder");
            var (rig, mount) = CreateLimb(LimbSide.Right);
            float ratio = 1.6f / body.Calibration.ModelHeight;

            Assert.That(body.ApplyBodyDimensions(1.6f, body.Calibration.ModelShoulderWidth * ratio), Is.True);
            mount.Step(0.01f);

            Assert.That(avatar.transform.localScale.x, Is.EqualTo(ratio).Within(1e-4f));
            TestMath.AreClose(expected * ratio, rig.UpperArm.position, 1e-3f, "体の比率が同じなら、付け根もアバターと一緒に縮む");
            Assert.That(rig.Reach, Is.EqualTo(0.55f * ratio).Within(0.005f));
        }
    }
}
