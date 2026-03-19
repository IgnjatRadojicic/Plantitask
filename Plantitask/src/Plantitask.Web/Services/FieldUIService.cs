using Plantitask.Web.Interfaces;

namespace Plantitask.Web.Services
{
    public class FieldUIService : IFieldUIService
    {
        public event Action? OnPlantTreeRequested;
        public event Action? OnJoinTreeRequested;

        public void RequestPlantTree() => OnPlantTreeRequested?.Invoke();
        public void RequestJoinTree() => OnJoinTreeRequested?.Invoke();
    }
}
