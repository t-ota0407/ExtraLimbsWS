using System.Collections.Generic;
using ExtraLimb.Limb;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>手先で掴む・放す・持ち替える（Step 3-4）。</summary>
    public class GraspTests
    {
        const float Dt = 1f / 90f;
        readonly List<GameObject> created = new List<GameObject>();

        GameObject Track(GameObject go)
        {
            created.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created)
                if (go != null) Object.DestroyImmediate(go);
            created.Clear();
        }

        Graspable Cube(Vector3 position, bool withBody = true)
        {
            var go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.transform.localScale = Vector3.one * 0.05f;
            go.transform.position = position;
            if (withBody) go.AddComponent<Rigidbody>();
            return go.AddComponent<Graspable>();
        }

        // 手の位置を hand から読む手先
        EndEffectorGrasper Grasper(Transform hand, float radius = 0.07f)
        {
            var grasper = Track(new GameObject("Grasper")).AddComponent<EndEffectorGrasper>();
            grasper.Radius = radius;
            grasper.PoseSource = () => new Pose(hand.position, hand.rotation);
            return grasper;
        }

        Transform Hand(Vector3 position)
        {
            var hand = Track(new GameObject("Hand")).transform;
            hand.position = position;
            return hand;
        }

        static void Step(EndEffectorGrasper grasper)
        {
            Physics.SyncTransforms();
            grasper.Step(Dt);
        }

        [Test]
        public void PressNearObject_GrabsAndCarriesWithOffset()
        {
            var cube = Cube(new Vector3(0f, 1f, 0.03f));
            var hand = Hand(new Vector3(0f, 1f, 0f));
            var grasper = Grasper(hand);
            Step(grasper);
            Assert.That(grasper.Hovered, Is.SameAs(cube));

            grasper.Pressed = true;
            Step(grasper);

            Assert.That(grasper.Held, Is.SameAs(cube));
            Assert.That(cube.Holder, Is.SameAs(grasper));
            Assert.That(cube.Body.isKinematic, Is.True);

            // 手を動かし、回すと、掴んだときの位置関係のまま付いてくる
            hand.SetPositionAndRotation(new Vector3(0.5f, 1.2f, 0f), Quaternion.AngleAxis(90f, Vector3.up));
            Step(grasper);
            TestMath.AreClose(new Vector3(0.5f + 0.03f, 1.2f, 0f), cube.transform.position);
        }

        [Test]
        public void Release_RestoresPhysicsAndGivesHandVelocity()
        {
            var cube = Cube(new Vector3(0f, 1f, 0f));
            var hand = Hand(new Vector3(0f, 1f, 0f));
            var grasper = Grasper(hand);
            grasper.Pressed = true;
            Step(grasper);
            for (int i = 0; i < 10; i++)
            {
                hand.position += new Vector3(0.01f, 0f, 0f);
                Step(grasper);
            }

            grasper.Pressed = false;
            Step(grasper);

            Assert.That(grasper.Held, Is.Null);
            Assert.That(cube.IsHeld, Is.False);
            Assert.That(cube.Body.isKinematic, Is.False);
            Assert.That(cube.Body.linearVelocity.x, Is.GreaterThan(0.3f));
        }

        [Test]
        public void PressBeforeReaching_DoesNotGrab()
        {
            // 押してから近づけても掴まない（押した瞬間に近くにある物だけを掴む）
            var cube = Cube(new Vector3(0f, 1f, 0f));
            var hand = Hand(new Vector3(0.5f, 1f, 0f));
            var grasper = Grasper(hand);
            grasper.Pressed = true;
            Step(grasper);

            hand.position = new Vector3(0f, 1f, 0f);
            Step(grasper);

            Assert.That(grasper.Held, Is.Null);
            Assert.That(grasper.Hovered, Is.SameAs(cube));
        }

        [Test]
        public void NearestObjectIsGrabbed_AndFarObjectIgnored()
        {
            var near = Cube(new Vector3(0f, 1f, 0.04f));
            Cube(new Vector3(0f, 1f, -0.06f));
            var far = Cube(new Vector3(0.5f, 1f, 0f));
            var hand = Hand(new Vector3(0f, 1f, 0f));
            var grasper = Grasper(hand);

            grasper.Pressed = true;
            Step(grasper);

            Assert.That(grasper.Held, Is.SameAs(near));
            Assert.That(far.IsHeld, Is.False);
        }

        [Test]
        public void Handover_ToAnotherGrasper()
        {
            // 追加肢から本来の手へ持ち替える
            var cube = Cube(new Vector3(0f, 1f, 0f));
            var limbHand = Hand(new Vector3(0f, 1f, 0f));
            var realHand = Hand(new Vector3(0.02f, 1f, 0f));
            var limb = Grasper(limbHand);
            var hand = Grasper(realHand);
            int limbReleased = 0;
            limb.Released += _ => limbReleased++;
            limb.Pressed = true;
            Step(limb);

            hand.Pressed = true;
            Step(hand);

            Assert.That(cube.Holder, Is.SameAs(hand));
            Assert.That(limb.Held, Is.Null);
            Assert.That(limbReleased, Is.EqualTo(1));
            Assert.That(cube.Body.isKinematic, Is.True);

            // 追加肢がトリガーを離しても、持ち替えた後の物は落ちない
            limb.Pressed = false;
            Step(limb);
            Assert.That(cube.Holder, Is.SameAs(hand));

            hand.Pressed = false;
            Step(hand);
            Assert.That(cube.IsHeld, Is.False);
            Assert.That(cube.Body.isKinematic, Is.False);
        }

        [Test]
        public void KinematicObject_StaysKinematicAfterRelease()
        {
            var cube = Cube(new Vector3(0f, 1f, 0f));
            cube.Body.isKinematic = true;
            var hand = Hand(new Vector3(0f, 1f, 0f));
            var grasper = Grasper(hand);
            grasper.Pressed = true;
            Step(grasper);

            grasper.Pressed = false;
            Step(grasper);

            Assert.That(cube.Body.isKinematic, Is.True);
        }
    }
}
