namespace Plantitask.Core.Entities.Lookups
{
    /// <summary>
    /// A plan's stable identity and display metadata. Freely editable because nothing here
    /// changes what a plan grants. The limits live in PlanVersion, which is append only.
    ///
    /// The row id is the PlanTier enum value and Name is the key code hardcodes ("free",
    /// "premium"). Same arrangement as GroupRoleLookup.
    /// </summary>
    public class PlanLookup : BaseLookupEntity
    {
    }
}
