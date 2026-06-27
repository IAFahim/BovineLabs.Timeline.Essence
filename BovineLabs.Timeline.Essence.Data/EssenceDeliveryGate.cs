namespace BovineLabs.Timeline.Essence.Data
{
    /// <summary>
    /// Pure per-frame decision for the once-when-ready delivery latch, shared by the Event and Intrinsic gather
    /// jobs so the fire-exactly-once / retry-until-resolved / re-arm-on-loop invariants live in one tested place.
    /// The gather jobs only differ in HOW they compute <c>resolved</c> (which writer buffer the target needs).
    /// </summary>
    public static class EssenceDeliveryGate
    {
        public enum Outcome : byte
        {
            /// <summary>Nothing owed this frame (already delivered, or not yet armed).</summary>
            Skip,

            /// <summary>Owed but not resolvable yet — stay pending and retry next frame.</summary>
            Retry,

            /// <summary>Deliver exactly once, then clear the latch.</summary>
            Fire,

            /// <summary>Dead config (no payload key) — can never deliver; clear the latch, never fire.</summary>
            Drop,
        }

        /// <summary>Computes the outcome and next latch state for one active-clip frame.</summary>
        /// <param name="isRisingEdge">ClipActive is on this frame while the previous frame was off.</param>
        /// <param name="pending">Current latch state.</param>
        /// <param name="hasPayload">False for a dead config key (nothing can ever be delivered).</param>
        /// <param name="resolved">True when binding + target + writer all resolved this frame.</param>
        /// <param name="nextPending">The latch state the caller should write back.</param>
        /// <returns>What the caller should do this frame.</returns>
        public static Outcome Evaluate(bool isRisingEdge, bool pending, bool hasPayload, bool resolved, out bool nextPending)
        {
            // Arm on the rising edge; a real loop wrap (re-cleared ClipActivePrevious) re-arms the same way.
            if (isRisingEdge)
            {
                pending = true;
            }

            if (!pending)
            {
                nextPending = false;
                return Outcome.Skip;
            }

            if (!hasPayload)
            {
                nextPending = false;
                return Outcome.Drop;
            }

            if (!resolved)
            {
                nextPending = true;
                return Outcome.Retry;
            }

            nextPending = false;
            return Outcome.Fire;
        }
    }
}
