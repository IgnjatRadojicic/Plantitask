namespace Plantitask.Web.Helpers;

public static class DeadlineDisplay
{
    // A deadline is the midnight that ends the due day. On the viewer's clock that midnight reads
    // as the next day at 00:00, so it is pulled back a minute to read as the due day at 23:59.
    public static DateTime ToLocal(DateTime dueAt)
    {
        var local = DateTime.SpecifyKind(dueAt, DateTimeKind.Utc).ToLocalTime();
        return local.TimeOfDay == TimeSpan.Zero ? local.AddMinutes(-1) : local;
    }
}
