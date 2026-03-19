namespace Plantitask.Core.Interfaces
{
    public interface ITreeProgressBroadcaster
    {
        Task BroadcastTreeUpdateAsync(Guid groupId);
    }
}