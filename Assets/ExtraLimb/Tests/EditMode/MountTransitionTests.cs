using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>付け替えの補間（Step 3-1）。</summary>
    public class MountTransitionTests
    {
        static readonly MountPose A = new MountPose(new Vector3(0.15f, 1.53f, -0.05f), Quaternion.identity, new Vector3(0.12f, -0.06f, 0.30f), new Vector3(0.35f, -0.1f, -0.05f));
        static readonly MountPose B = new MountPose(new Vector3(0.21f, 1.05f, 0.05f), Quaternion.Euler(0f, 30f, 0f), new Vector3(0.05f, 0.15f, 0.35f), new Vector3(0.3f, -0.15f, -0.05f));

        [Test]
        public void Lerp_EndpointsAndMiddle()
        {
            var start = MountPose.Lerp(A, B, 0f);
            var end = MountPose.Lerp(A, B, 1f);
            var middle = MountPose.Lerp(A, B, 0.5f);

            TestMath.AreClose(A.joint, start.joint);
            TestMath.AreClose(A.frameRotation, start.frameRotation);
            TestMath.AreClose(B.joint, end.joint);
            TestMath.AreClose(B.restHandPosition, end.restHandPosition);
            TestMath.AreClose((A.joint + B.joint) * 0.5f, middle.joint);
            TestMath.AreClose(Quaternion.Euler(0f, 15f, 0f), middle.frameRotation);
        }

        [Test]
        public void ToLocalAndBack_IsIdentity()
        {
            var frame = new Pose(new Vector3(0.1f, 1.3f, -0.04f), Quaternion.Euler(10f, 40f, -5f));

            var back = A.ToLocal(frame).ToWorld(frame);

            TestMath.AreClose(A.joint, back.joint);
            TestMath.AreClose(A.frameRotation, back.frameRotation);
            TestMath.AreClose(A.restHandPosition, back.restHandPosition);
        }

        [Test]
        public void Transition_StartsAtZero_EndsAtOne_AndIsMonotonic()
        {
            var transition = new MountTransition();
            transition.Start(0.35f);
            Assert.That(transition.IsActive, Is.True);
            Assert.That(transition.Progress, Is.EqualTo(0f));

            float previous = 0f;
            int frames = 0;
            while (transition.IsActive && frames < 1000)
            {
                float progress = transition.Advance(1f / 90f);
                Assert.That(progress, Is.GreaterThanOrEqualTo(previous));
                previous = progress;
                frames++;
            }

            Assert.That(previous, Is.EqualTo(1f));
            Assert.That(frames, Is.EqualTo(Mathf.CeilToInt(0.35f * 90f)).Within(1));
        }

        [Test]
        public void Transition_FirstAndLastStepsAreSmall()
        {
            // smoothstep なので、始めと終わりの1フレームの進みは、一定速度の場合よりずっと小さい
            var transition = new MountTransition();
            transition.Start(0.35f);
            float dt = 1f / 90f;
            float linearStep = dt / 0.35f;

            float first = transition.Advance(dt);
            Assert.That(first, Is.LessThan(linearStep * 0.2f));

            float beforeLast = first;
            float last = first;
            while (transition.IsActive)
            {
                beforeLast = last;
                last = transition.Advance(dt);
            }
            Assert.That(last - beforeLast, Is.LessThan(linearStep * 0.5f));
        }

        [Test]
        public void ZeroDuration_FinishesImmediately()
        {
            var transition = new MountTransition();
            transition.Start(0f);

            Assert.That(transition.IsActive, Is.False);
            Assert.That(transition.Progress, Is.EqualTo(1f));
            Assert.That(transition.Advance(0.01f), Is.EqualTo(1f));
        }
    }
}
