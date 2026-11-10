using System.Linq;
using ExtraLimb.Experiment;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>仮想の鏡（Step 3-1）：鏡像の骨が、アバターの骨を鏡の面で反転した位置に来る。</summary>
    public class AvatarMirrorTests
    {
        const string ModelPath = "Assets/Models/ExtraLimbsAvatar_Shoulder.fbx";
        static readonly Vector3 MirrorCenter = new Vector3(0f, 0f, 1.5f);
        static readonly Vector3 MirrorNormal = new Vector3(0f, 0f, -1f); // 鏡を見る人の側

        GameObject avatar, mirrorObject;
        AvatarMirror mirror;

        static Vector3 Reflect(Vector3 p) => p - 2f * Vector3.Dot(p - MirrorCenter, MirrorNormal) * MirrorNormal;

        static Transform Find(Transform root, string boneName) =>
            root.GetComponentsInChildren<Transform>(true).First(t => t.name == boneName);

        [SetUp]
        public void SetUp()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            avatar = Object.Instantiate(model);
            avatar.transform.SetPositionAndRotation(new Vector3(0.2f, 0f, 0.1f), Quaternion.Euler(0f, 20f, 0f));

            mirrorObject = new GameObject("Mirror");
            mirrorObject.transform.SetPositionAndRotation(MirrorCenter, Quaternion.LookRotation(MirrorNormal));
            mirror = mirrorObject.AddComponent<AvatarMirror>();
            mirror.Configure(avatar.transform, model, withRoom: true);
            Assert.That(mirror.Build(), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(mirrorObject);
            Object.DestroyImmediate(avatar);
        }

        [Test]
        public void ImageBones_AreReflectionsOfAvatarBones()
        {
            // 腕と追加肢を曲げてから写す
            Find(avatar.transform, "mixamorig:RightForeArm").localRotation *= Quaternion.Euler(0f, 0f, 50f);
            Find(avatar.transform, "sub_RightArm").localRotation *= Quaternion.Euler(30f, 0f, 0f);
            mirror.Sync();

            foreach (var name in new[] { "mixamorig:Head", "mixamorig:RightHand", "mixamorig:LeftFoot", "sub_RightHand", "sub_LeftHand" })
            {
                var original = Find(avatar.transform, name).position;
                var image = Find(mirror.Image, name).position;
                TestMath.AreClose(Reflect(original), image, 1e-3f, name);
            }
        }

        [Test]
        public void HiddenHead_IsVisibleInMirror()
        {
            var head = Find(avatar.transform, "mixamorig:Head");
            head.localScale = Vector3.one * 1e-4f;

            mirror.Sync();

            Assert.That(Find(mirror.Image, "mixamorig:Head").lossyScale.magnitude, Is.GreaterThan(1f));
        }

        [Test]
        public void HiddenExtraLimb_StaysHiddenInMirror()
        {
            Find(avatar.transform, "sub_LeftShoulder").localScale = Vector3.one * 1e-4f;

            mirror.Sync();

            Assert.That(Find(mirror.Image, "sub_LeftShoulder").localScale.x, Is.LessThan(1e-3f));
        }

        [Test]
        public void Visible_False_HidesImageAndRoom()
        {
            mirror.Visible = false;

            Assert.That(mirror.Image.gameObject.activeInHierarchy, Is.False);
            Assert.That(mirrorObject.transform.Find("MirrorRoom").gameObject.activeSelf, Is.False);
        }
    }
}
