namespace Plantitask.Core.Enums
{
    /// <summary>
    /// The plan identities code is allowed to name. The value is the PlanLookup row id, the same
    /// arrangement GroupRole has with GroupRoleLookup.
    ///
    /// Ordering is meaningful: when a user holds overlapping grants the higher tier wins, so a
    /// 30 day pass bought during a live subscription cannot downgrade anybody.
    /// </summary>
    public enum PlanTier
    {
        Free = 1,
        Premium = 2
    }
}
