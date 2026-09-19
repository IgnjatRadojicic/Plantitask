namespace Plantitask.Web.Interfaces
{
    public interface IThemeService
    {
        bool IsDark { get; }

        event Action? OnChanged;

        void Initialize();
        Task SetAsync(bool dark);
        Task ToggleAsync();
    }
}
