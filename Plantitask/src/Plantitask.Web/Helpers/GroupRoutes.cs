namespace Plantitask.Web.Helpers;

/// <summary>
/// Builds group page URLs.
///
/// The route is "/groups/{Slug}/{GroupId:guid}" (KanbanBoard.razor). The slug is cosmetic -
/// the page keys off GroupId alone - but it is part of the route template, so a URL without
/// it does not match and the user lands on Page Not Found. Every caller has to supply both
/// segments, which is why this lives in one place instead of being reassembled at each
/// navigation site.
/// </summary>
public static class GroupRoutes
{
    public static string Board(Guid groupId, string? groupName) =>
        $"/groups/{Slug(groupId, groupName)}/{groupId}";

    private static string Slug(Guid groupId, string? groupName) =>
        string.IsNullOrWhiteSpace(groupName)
            ? groupId.ToString()
            : Uri.EscapeDataString(groupName.ToLower().Replace(" ", "-"));
}
