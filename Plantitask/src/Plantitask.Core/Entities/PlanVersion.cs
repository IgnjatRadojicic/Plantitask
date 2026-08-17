using Plantitask.Core.Common;
using Plantitask.Core.Entities.Lookups;

namespace Plantitask.Core.Entities
{
    /// <summary>
    /// What a plan grants, as of a date. Append only: publishing a change means inserting the
    /// next version, never updating a published row.
    ///
    /// That is what makes grandfathering work. A grant pins to the version that was current when
    /// it was sold, so raising or lowering a limit tomorrow cannot retroactively reprice anyone
    /// who already bought. It is also the record when somebody disputes what they paid for.
    ///
    /// ImmutableEntity on purpose. A soft delete would hide a row that live grants still point
    /// at, and the global query filter would then break resolution rather than fail loudly.
    /// </summary>
    public class PlanVersion : ImmutableEntity
    {
        public int PlanId { get; set; }
        public int Version { get; set; }

        /// <summary>When this version starts applying. Future dates are legal.</summary>
        public DateTime EffectiveFrom { get; set; }

        /// <summary>Null means draft. Resolution ignores drafts entirely.</summary>
        public DateTime? PublishedAt { get; set; }

        public int MaxGroups { get; set; }
        public long MaxStorageBytes { get; set; }

        public PlanLookup Plan { get; set; } = null!;
    }
}
