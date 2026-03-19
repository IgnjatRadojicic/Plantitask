using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services;

public class KanbanService : BaseApiService, IKanbanService
{
    public KanbanService(HttpClient http) : base(http) { }

    public Task<ServiceResult<KanbanBoardDto>> GetBoardAsync(Guid groupId)
        => GetAsync<KanbanBoardDto>($"api/kanban/{groupId}");

    public Task<ServiceResult<MessageResponse>> MoveTaskAsync(Guid taskId, MoveTaskDto model)
        => PutAsync<MessageResponse>($"api/kanban/{taskId}/move", model);
}