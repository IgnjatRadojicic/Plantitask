namespace Plantitask.Web.Interfaces
{
    public interface IFieldUIService
    {
        event Action? OnJoinTreeRequested;
        event Action? OnPlantTreeRequested;

        void RequestJoinTree();
        void RequestPlantTree();
    }
}