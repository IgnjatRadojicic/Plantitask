namespace Plantitask.Core.Constants
{
    /// <summary>
    /// The words an audit row can record in its Action column. Stored forever and shown in the
    /// dashboard feed, so the values are written as literals and never renamed. A new audited
    /// event adds one constant here.
    /// </summary>
    public static class AuditActions
    {
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string Deleted = "Deleted";

        public const string Moved = "Moved";
        public const string StatusChanged = "StatusChanged";
        public const string PriorityChanged = "PriorityChanged";
        public const string Assigned = "Assigned";
        public const string Unassigned = "Unassigned";

        public const string Uploaded = "Uploaded";

        public const string JoinedGroup = "JoinedGroup";
        public const string LeftGroup = "LeftGroup";
        public const string RemovedFromGroup = "RemovedFromGroup";
        public const string RoleChanged = "RoleChanged";

        public const string Registered = "Registered";
        public const string Login = "Login";
        public const string Logout = "Logout";
        public const string PasswordReset = "PasswordReset";

        public const string PremiumActivated = "PremiumActivated";
        public const string PremiumCancelled = "PremiumCancelled";
    }
}
