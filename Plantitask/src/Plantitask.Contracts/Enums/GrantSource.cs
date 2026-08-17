namespace Plantitask.Core.Enums
{
    /// <summary>
    /// Why a grant exists. Stored as text so a new source never needs a migration, and read back
    /// only for display and for deciding which grants a cancel is allowed to touch.
    /// </summary>
    public static class GrantSource
    {
        public const string PayPalSubscription = "paypal_subscription";
        public const string PayPalOneTime = "paypal_onetime";
        public const string AdminGrant = "admin_grant";
    }
}
