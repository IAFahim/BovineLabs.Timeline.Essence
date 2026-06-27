using BovineLabs.Timeline.Essence.Data;
using NUnit.Framework;
using static BovineLabs.Timeline.Essence.Data.EssenceDeliveryGate;

namespace BovineLabs.Timeline.Essence.Tests
{
    /// <summary>
    /// Verifies the once-when-ready delivery latch shared by the Event and Intrinsic gather jobs. The Sim mirrors
    /// the gather job's per-frame call: feed (isEdge, hasPayload, resolved) and track the latch + fire count.
    /// </summary>
    public class EssenceDeliveryGateTests
    {
        private struct Sim
        {
            public bool Pending;
            public int Fires;

            public Outcome Frame(bool isEdge, bool hasPayload, bool resolved)
            {
                var o = Evaluate(isEdge, this.Pending, hasPayload, resolved, out var next);
                this.Pending = next;
                if (o == Outcome.Fire)
                {
                    this.Fires++;
                }

                return o;
            }
        }

        [Test]
        public void HappyPath_FiresExactlyOnce_OnEdgeWhenResolved()
        {
            var s = new Sim();
            Assert.AreEqual(Outcome.Fire, s.Frame(isEdge: true, hasPayload: true, resolved: true));
            Assert.IsFalse(s.Pending);
            Assert.AreEqual(Outcome.Skip, s.Frame(false, true, true)); // still active, no re-fire
            Assert.AreEqual(1, s.Fires);
        }

        [Test]
        public void Retry_DefersAcrossUnresolvedFrames_ThenFiresOnce()
        {
            var s = new Sim();
            Assert.AreEqual(Outcome.Retry, s.Frame(isEdge: true, hasPayload: true, resolved: false)); // edge, target not ready
            Assert.IsTrue(s.Pending);
            Assert.AreEqual(Outcome.Retry, s.Frame(false, true, false)); // still not ready -> keep owing
            Assert.AreEqual(Outcome.Fire, s.Frame(false, true, true)); // streamed in -> deliver late but delivered
            Assert.AreEqual(Outcome.Skip, s.Frame(false, true, true)); // no double fire
            Assert.AreEqual(1, s.Fires);
            Assert.IsFalse(s.Pending);
        }

        [Test]
        public void DeadConfig_NeverFires_AndStopsRetrying()
        {
            var s = new Sim();
            Assert.AreEqual(Outcome.Drop, s.Frame(isEdge: true, hasPayload: false, resolved: false));
            Assert.IsFalse(s.Pending);
            Assert.AreEqual(Outcome.Skip, s.Frame(false, false, false));
            Assert.AreEqual(0, s.Fires);
        }

        [Test]
        public void LoopWrap_ReArmsAndFiresAgain_OncePerActivation()
        {
            var s = new Sim();
            s.Frame(true, true, true); // first activation fires
            Assert.AreEqual(1, s.Fires);

            // Director loops: LoopRefireSystem re-clears ClipActivePrevious -> a fresh rising edge for the still-active clip.
            Assert.AreEqual(Outcome.Fire, s.Frame(isEdge: true, hasPayload: true, resolved: true));
            Assert.AreEqual(2, s.Fires);
            Assert.AreEqual(Outcome.Skip, s.Frame(false, true, true));
            Assert.AreEqual(2, s.Fires);
        }

        [Test]
        public void NotPending_NoEdge_IsSkip()
        {
            var s = new Sim();
            Assert.AreEqual(Outcome.Skip, s.Frame(isEdge: false, hasPayload: true, resolved: true));
            Assert.AreEqual(0, s.Fires);
        }

        [Test]
        public void TruthTable_SingleArmedFrame()
        {
            Assert.AreEqual(Outcome.Drop, Evaluate(false, true, false, false, out _)); // pending, no payload
            Assert.AreEqual(Outcome.Retry, Evaluate(false, true, true, false, out _)); // pending, unresolved
            Assert.AreEqual(Outcome.Fire, Evaluate(false, true, true, true, out _)); // pending, resolved
            Assert.AreEqual(Outcome.Skip, Evaluate(false, false, true, true, out _)); // not pending, no edge
            Assert.AreEqual(Outcome.Fire, Evaluate(true, false, true, true, out _)); // edge always arms
        }
    }
}
