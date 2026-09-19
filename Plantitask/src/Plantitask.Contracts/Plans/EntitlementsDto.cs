namespace Plantitask.Core.DTO.Plans
{
    /// <summary>
    /// Plan limits plus current usage. The one payload that carries policy, so identity DTOs do
    /// not have to. Usage ships alongside the limits because a quota the user cannot see is a
    /// quota that surprises them at upload time.
    /// </summary>
    public class EntitlementsDto
    {
        public string PlanKey { get; set; } = string.Empty;
        public string PlanDisplayName { get; set; } = string.Empty;
        public bool IsPremium { get; set; }

        public string? SubscriptionType { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }

        public int MaxGroups { get; set; }
        public int GroupsUsed { get; set; }

        public long MaxStorageBytes { get; set; }
        public long StorageUsedBytes { get; set; }
    }
}
