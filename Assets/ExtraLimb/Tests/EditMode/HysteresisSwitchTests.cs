using ExtraLimb.Core;
using NUnit.Framework;

namespace ExtraLimb.Tests.EditMode
{
    /// <summary>トリガーの押し込み量のオン・オフ（Step 3-4）。</summary>
    public class HysteresisSwitchTests
    {
        [Test]
        public void TurnsOnAboveOn_OffBelowOff()
        {
            var trigger = new HysteresisSwitch(0.6f, 0.4f);

            Assert.That(trigger.Update(0.5f), Is.False);
            Assert.That(trigger.Update(0.6f), Is.True);
            // 閾値の間ではそのまま（ちらつかない）
            Assert.That(trigger.Update(0.5f), Is.True);
            Assert.That(trigger.Update(0.41f), Is.True);
            Assert.That(trigger.Update(0.4f), Is.False);
            Assert.That(trigger.Update(0.55f), Is.False);
        }

        [Test]
        public void Reset_TurnsOff()
        {
            var trigger = new HysteresisSwitch();
            trigger.Update(1f);

            trigger.Reset();

            Assert.That(trigger.State, Is.False);
        }

        [Test]
        public void GraspConfig_Defaults()
        {
            var config = new GraspTriggerConfig();

            Assert.That(config.UsesController, Is.True);
            Assert.That(config.UsesKey, Is.True);
            Assert.That(config.hand, Is.EqualTo(ControllerHand.Right));
            Assert.That(new GraspTriggerConfig { input = GraspInput.Key }.UsesController, Is.False);
            Assert.That(new GraspTriggerConfig { input = GraspInput.Controller }.UsesKey, Is.False);
        }
    }
}
