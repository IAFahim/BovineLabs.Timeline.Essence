namespace BovineLabs.Timeline.Essence.Data
{
    /// <summary>
    /// Per-clip policy for what happens when a clip's route carries a LinkKey but the EntityLink hop cannot be
    /// resolved (no link entry, or it resolves to Entity.Null). Default 0 = FallbackToTarget preserves the legacy
    /// behavior for existing bakes.
    /// </summary>
    public enum LinkMissBehavior : byte
    {
        /// <summary>Fire at the unlinked base target (legacy; can misdirect effects when the link is absent).</summary>
        FallbackToTarget = 0,

        /// <summary>Treat the link as not-yet-resolved and retry on later active frames.</summary>
        Retry = 1,

        /// <summary>Consume the clip's one-shot without firing anywhere.</summary>
        Drop = 2,
    }
}
