using Plantitask.Web.Models;

using Plantitask.Core.DTO.Dashboard;
namespace Plantitask.Web.Interfaces
{
    public interface IFieldSignalRService
    {
        public ValueTask DisposeAsync();
        public Task ConnectAsync();

        Task JoinGroupRoomsAsync(IEnumerable<string> groupIds);

        public event Func<string, int, double, Task>? OnTreeUpdated;
        public event Func<FieldTreeDto, Task>? OnTreeAdded;
    }
}
