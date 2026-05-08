namespace Plantitask.Core.Common.Interfaces
{
    public interface IEntity
    {
        Guid Id { get; set; }
        DateTime CreatedAt { get; set; }
    }
}
