using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Plantitask.Web;
using Plantitask.Web.Helpers;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"]
    ?? "http://localhost:5212";

// Where uploaded files are served from. In development that is the API's own static file
// route (see UseStaticFiles with RequestPath "/files" in the API Program.cs). In production
// it is whatever host serves the storage container.
FileUrls.Configure(builder.Configuration["FileSettings:BaseUrl"]
    ?? $"{apiBaseUrl}/files");

// Register the DelegatingHandler
builder.Services.AddScoped<AuthTokenHandler>();

// Register HttpClient WITH the handler in the pipeline
builder.Services.AddHttpClient("PlantitaskApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
}).AddHttpMessageHandler<AuthTokenHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("PlantitaskApi"));

// MudBlazor
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass =
        MudBlazor.Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = true;
    config.SnackbarConfiguration.NewestOnTop = true;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 4000;
    config.SnackbarConfiguration.SnackbarVariant = MudBlazor.Variant.Filled;
});

builder.Services.AddHttpClient(SessionService.AuthClientName, client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddScoped<ISessionService, SessionService>();

// Local storage
builder.Services.AddBlazoredLocalStorage();

// Auth
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddSingleton<IFieldUIService, FieldUIService>();
builder.Services.AddScoped<IFieldPositionService, FieldPositionService>();
builder.Services.AddScoped<IFieldSignalRService, FieldSignalRService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IKanbanService, KanbanService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IKanbanSignalRService, KanbanSignalRService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
builder.Services.AddScoped<INotificationSignalRService, NotificationSignalRService>();
builder.Services.AddScoped<KanbanLayoutState>();
builder.Services.AddScoped<IPayPalService, PayPalService>();
builder.Services.AddScoped<ISettingsUIService, SettingsUIService>();

await builder.Build().RunAsync();
