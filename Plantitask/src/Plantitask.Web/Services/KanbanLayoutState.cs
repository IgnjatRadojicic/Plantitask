namespace Plantitask.Web.Services;

public class KanbanLayoutState
{
    public string GroupName { get; private set; } = "";
    public int MemberCount { get; private set; }
    public Guid GroupId { get; private set; }

    public event Action? OnChanged;
    public event Action? OnCreateTaskRequested;
    public event Action<string>? OnSearchChanged;

    public void SetGroupInfo(Guid groupId, string name, int memberCount)
    {
        GroupId = groupId;
        GroupName = name;
        MemberCount = memberCount;
        OnChanged?.Invoke();
    }

    public void RequestCreateTask() => OnCreateTaskRequested?.Invoke();

    public void NotifySearchChanged(string term) => OnSearchChanged?.Invoke(term);
}
