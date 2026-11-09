using System;
using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>届く範囲の制限（Step 1-2）。</summary>
    public class WorkspaceLimiterTests
    {
        static readonly Vector3 Root = new Vector3(0.2f, 1.4f, 0.1f);

        static WorkspaceLimiter Create() => new WorkspaceLimiter(Root, 0.1f, 0.6f);

        [Test]
        public void InsideRange_IsUnchanged()
        {
            var limiter = Create();
            var point = Root + new Vector3(0.1f, -0.2f, 0.3f);

            TestMath.AreClose(point, limiter.Clamp(point));
            Assert.That(limiter.Contains(point), Is.True);
        }

        [Test]
        public void BeyondMaxRadius_IsProjectedOntoSurfaceKeepingDirection()
        {
            var limiter = Create();
            var direction = new Vector3(1f, 2f, -2f).normalized;

            var clamped = limiter.Clamp(Root + direction * 2f);

            TestMath.AreClose(Root + direction * 0.6f, clamped);
            Assert.That(limiter.Contains(Root + direction * 2f), Is.False);
        }

        [Test]
        public void InsideMinRadius_IsPushedOut()
        {
            var limiter = Create();
            var direction = new Vector3(0f, -1f, 1f).normalized;

            TestMath.AreClose(Root + direction * 0.1f, limiter.Clamp(Root + direction * 0.03f));
        }

        [Test]
        public void AtCenter_IsPushedOutAlongFallbackDirection()
        {
            var limiter = Create();

            TestMath.AreClose(Root + Vector3.forward * 0.1f, limiter.Clamp(Root));
        }

        [Test]
        public void InvalidRadii_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorkspaceLimiter(Root, -0.1f, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorkspaceLimiter(Root, 0.5f, 0.5f));
        }
    }
}
