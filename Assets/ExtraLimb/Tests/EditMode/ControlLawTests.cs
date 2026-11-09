using ExtraLimb.Core;
using NUnit.Framework;
using UnityEngine;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>制御則（Step 1-2）。90 Hz で1フレームずつ進める。</summary>
    public class ControlLawTests
    {
        const float Dt = 1f / 90f;

        // G_p = 2、G_v = 3 /s、不感帯 d = 2 cm、ハイブリッドの切替半径 r = 8 cm
        static readonly ControlParams Params = new ControlParams(2f, 3f, 0.02f, 0.08f);

        // 胴体フレームでの追加肢の根元と、既定の手先位置（基準位置）
        static readonly Vector3 Root = new Vector3(0.2f, 1.4f, 0.1f);
        static readonly Vector3 Base = new Vector3(0.3f, 1.2f, 0.4f);

        static Vector3 Run(IControlLaw law, Vector3 u, int frames)
        {
            var target = law.Target;
            for (int i = 0; i < frames; i++) target = law.Step(new ControlInput(u), Params, Dt);
            return target;
        }

        static T Create<T>(bool withWorkspace = false) where T : IControlLaw, new()
        {
            var law = new T();
            if (withWorkspace) law.Workspace = new WorkspaceLimiter(Root, 0.1f, 0.6f);
            law.Reset(Base);
            return law;
        }

        // ---- 位置制御 ----

        [Test]
        public void Position_ZeroInput_TargetIsBase()
        {
            var law = Create<PositionControlLaw>();

            TestMath.AreClose(Base, Run(law, Vector3.zero, 10));
        }

        [Test]
        public void Position_TargetIsBasePlusGainTimesInput()
        {
            var law = Create<PositionControlLaw>();
            var u = new Vector3(0.05f, -0.02f, 0.1f);

            TestMath.AreClose(Base + 2f * u, Run(law, u, 1));
        }

        [Test]
        public void Position_DependsOnlyOnCurrentInput()
        {
            var law = Create<PositionControlLaw>();
            Run(law, new Vector3(0.3f, 0f, 0f), 90);

            var u = new Vector3(0f, 0.01f, 0.02f);

            TestMath.AreClose(Base + 2f * u, Run(law, u, 1));
        }

        [Test]
        public void Position_SaturatesAtWorkspaceEdgeAndRecoversImmediately()
        {
            var law = Create<PositionControlLaw>(withWorkspace: true);

            var saturated = Run(law, new Vector3(0f, 0f, 1f), 1);
            Assert.That(Vector3.Distance(saturated, Root), Is.EqualTo(0.6f).Within(1e-4f));

            var u = new Vector3(0f, 0f, 0.02f);
            TestMath.AreClose(Base + 2f * u, Run(law, u, 1));
        }

        // ---- 速度制御 ----

        [Test]
        public void Rate_InsideDeadzone_TargetDoesNotMove()
        {
            var law = Create<RateControlLaw>();
            var random = new System.Random(3);

            for (int i = 0; i < 200; i++)
            {
                var direction = TestMath.RandomPosition(random, 1f).normalized;
                float magnitude = (float)random.NextDouble() * 0.0199f;
                law.Step(new ControlInput(direction * magnitude), Params, Dt);
            }

            TestMath.AreClose(Base, law.Target);
        }

        [Test]
        public void Rate_OutsideDeadzone_MovesAtExpectedSpeed()
        {
            var law = Create<RateControlLaw>();

            // |u| = 7 cm → 不感帯を5 cm超える → 3 /s × 0.05 m = 0.15 m/s
            var target = Run(law, new Vector3(0f, 0f, 0.07f), 90);

            TestMath.AreClose(Base + new Vector3(0f, 0f, 0.15f), target, 1e-3f);
        }

        [Test]
        public void Rate_BackInDeadzone_HoldsPosition()
        {
            var law = Create<RateControlLaw>();
            var moved = Run(law, new Vector3(0.07f, 0f, 0f), 45);
            Assert.That(Vector3.Distance(moved, Base), Is.GreaterThan(0.05f));

            var held = Run(law, new Vector3(0.01f, 0f, 0f), 90);

            TestMath.AreClose(moved, held);
        }

        [Test]
        public void Rate_SpeedIsContinuousAtDeadzoneEdge()
        {
            float SpeedAt(float magnitude)
            {
                var law = Create<RateControlLaw>();
                var target = Run(law, new Vector3(magnitude, 0f, 0f), 1);
                return Vector3.Distance(target, Base) / Dt;
            }

            Assert.That(SpeedAt(0.02f - 1e-4f), Is.EqualTo(0f));
            Assert.That(SpeedAt(0.02f + 1e-4f), Is.LessThan(1e-3f));
        }

        [Test]
        public void Rate_ZeroDt_DoesNotMove()
        {
            var law = Create<RateControlLaw>();

            TestMath.AreClose(Base, law.Step(new ControlInput(new Vector3(0.2f, 0f, 0f)), Params, 0f));
        }

        [Test]
        public void Rate_PushingAgainstWorkspaceEdge_DoesNotWindUp()
        {
            var law = Create<RateControlLaw>(withWorkspace: true);
            var outward = (Base - Root).normalized;

            // 端に向かって5秒押し続ける
            var atEdge = Run(law, outward * 0.1f, 450);
            Assert.That(Vector3.Distance(atEdge, Root), Is.EqualTo(0.6f).Within(1e-4f), "範囲の外へ流れない");

            // 逆向きに入れた最初のフレームから戻り始める（0.08 m × 3 /s × Δt）
            var back = Run(law, -outward * 0.1f, 1);
            Assert.That(Vector3.Distance(back, Root), Is.EqualTo(0.6f - 0.24f * Dt).Within(1e-4f));
        }

        // ---- ハイブリッド制御 ----

        [Test]
        public void Hybrid_InsideRadius_BehavesLikePositionControl()
        {
            var law = Create<HybridControlLaw>();
            var u = new Vector3(0.05f, 0f, 0.03f); // |u| ≈ 5.8 cm < 8 cm

            var target = Run(law, u, 90);

            TestMath.AreClose(Base + 2f * u, target);
            TestMath.AreClose(Base, law.BasePosition);
        }

        [Test]
        public void Hybrid_OutsideRadius_MovesBasePosition()
        {
            var law = Create<HybridControlLaw>();
            var u = new Vector3(0f, 0.1f, 0f); // 切替半径を2 cm超える → 基準位置が 0.06 m/s で動く

            var target = Run(law, u, 90);

            var expectedBase = Base + new Vector3(0f, 0.06f, 0f);
            TestMath.AreClose(expectedBase, law.BasePosition, 1e-3f);
            TestMath.AreClose(expectedBase + 2f * u, target, 1e-3f);
        }

        [Test]
        public void Hybrid_CrossingRadius_TargetDoesNotJump()
        {
            var law = Create<HybridControlLaw>();
            const int frames = 45;          // 0.5秒かけて
            const float maxInput = 0.24f;   // 切替半径の3倍まで足を出す
            float du = maxInput / frames;

            var previous = law.Target;
            for (int i = 1; i <= frames; i++)
            {
                var u = new Vector3(du * i, 0f, 0f);
                var target = law.Step(new ControlInput(u), Params, Dt);

                // 1フレームの変化は、位置制御の分と基準位置が動く分の和を超えない
                float bound = 2f * du + 3f * Mathf.Max(0f, u.x - 0.08f) * Dt;
                Assert.That(Vector3.Distance(previous, target), Is.LessThanOrEqualTo(bound + 1e-5f), $"frame {i}");
                previous = target;
            }
        }

        [Test]
        public void Hybrid_ReturningInside_KeepsShiftedBase()
        {
            var law = Create<HybridControlLaw>();
            Run(law, new Vector3(0f, 0f, 0.12f), 90);
            var shiftedBase = law.BasePosition;
            Assert.That(Vector3.Distance(shiftedBase, Base), Is.GreaterThan(0.03f));

            var target = Run(law, Vector3.zero, 30);

            TestMath.AreClose(shiftedBase, target);
            TestMath.AreClose(shiftedBase, law.BasePosition);
        }

        [Test]
        public void Hybrid_PushingAgainstWorkspaceEdge_DoesNotWindUp()
        {
            var law = Create<HybridControlLaw>(withWorkspace: true);
            var outward = (Base - Root).normalized;

            // 端に向かって10秒押し続ける
            var atEdge = Run(law, outward * 0.1f, 900);
            Assert.That(Vector3.Distance(atEdge, Root), Is.EqualTo(0.6f).Within(1e-4f), "範囲の外へ流れない");

            // 足を少し戻した（切替半径の内側に入れた）最初のフレームから手先が戻る
            var back = Run(law, outward * 0.05f, 1);
            Assert.That(Vector3.Distance(back, Root), Is.EqualTo(0.6f - 2f * 0.05f).Within(1e-3f));
        }

        [Test]
        public void Hybrid_InsideRadiusAtEdge_SaturatesWithoutMovingBase()
        {
            var law = Create<HybridControlLaw>(withWorkspace: true);
            var outward = (Base - Root).normalized;
            var baseNearEdge = Root + outward * 0.55f; // 端まで5 cm
            law.Reset(baseNearEdge);

            // 切替半径の内側（7 cm）でも、ゲイン倍（14 cm）すると端を越える入力
            var target = Run(law, outward * 0.07f, 30);

            Assert.That(Vector3.Distance(target, Root), Is.EqualTo(0.6f).Within(1e-4f), "端で止まる");
            TestMath.AreClose(baseNearEdge, law.BasePosition, message: "切替半径の内側では基準位置は動かない");

            // 足を戻すと、元の基準位置に戻る（位置制御と同じ）
            TestMath.AreClose(baseNearEdge, Run(law, Vector3.zero, 1));
        }

        // ---- 共通 ----

        [Test]
        public void Reset_ReturnsTargetToNewBase()
        {
            foreach (var mode in new[] { ControlModeKind.Position, ControlModeKind.Rate, ControlModeKind.Hybrid })
            {
                var law = ControlLaws.Create(mode);
                law.Reset(Base);
                Run(law, new Vector3(0.15f, 0f, 0f), 90);

                var newBase = new Vector3(-0.3f, 1.1f, 0.35f);
                law.Reset(newBase);

                TestMath.AreClose(newBase, law.Target, message: mode.ToString());
                TestMath.AreClose(newBase, law.BasePosition, message: mode.ToString());
            }
        }

        [Test]
        public void Create_ReturnsLawForEachMode()
        {
            Assert.That(ControlLaws.Create(ControlModeKind.Position), Is.TypeOf<PositionControlLaw>());
            Assert.That(ControlLaws.Create(ControlModeKind.Rate), Is.TypeOf<RateControlLaw>());
            Assert.That(ControlLaws.Create(ControlModeKind.Hybrid), Is.TypeOf<HybridControlLaw>());
        }

        [Test]
        public void ParamsFromConfig_ConvertsCentimeters()
        {
            var config = new LimbRuntimeConfig { positionGain = 1.5f, velocityGain = 2f, deadzoneCm = 3f, hybridRadiusCm = 10f };

            var p = ControlParams.From(config);

            Assert.That(p.positionGain, Is.EqualTo(1.5f));
            Assert.That(p.velocityGain, Is.EqualTo(2f));
            Assert.That(p.deadzone, Is.EqualTo(0.03f).Within(1e-6f));
            Assert.That(p.hybridRadius, Is.EqualTo(0.1f).Within(1e-6f));
        }
    }
}
