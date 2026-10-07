using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Plantitask.Api.Configuration;
using Plantitask.Api.Extensions;
using Plantitask.Api.Filters;
using Plantitask.Api.Hubs;
using Plantitask.Api.Interfaces;
using Plantitask.Api.Middleware;
using Plantitask.Api.Services;
using Plantitask.Core.Common;
using Plantitask.Core.Configuration;
using Plantitask.Core.DTO.Paypal;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Validation;
using Plantitask.Infrastructure.Data;
using Plantitask.Infrastructure.Services;
using Plantitask.Infrastructure.Services.Email;
using Plantitask.Infrastructure.Services.Storage;
using StackExchange.Redis;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Global backstop for request bodies. Per-endpoint [RequestSizeLimit] tightens this;
// no endpoint may exceed it. Framework default is 30 MB, well above anything we accept.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 8 * 1024 * 1024;
});

// Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")),
    ServiceLifetime.Scoped);


// Register DbContext as IApplicationDbContext for dependency injection
builder.Services.AddScoped<IApplicationDbContext>(provider =>
    provider.GetRequiredService<ApplicationDbContext>());

// Redis
var redisConnection = builder.Configuration.GetConnectionString("RedisConnection");
if (string.IsNullOrEmpty(redisConnection))
{
    throw new InvalidOperationException("Redis connection string not found!");
}
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddScoped<IRedisService, RedisService>();
builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection("JwtSettings"))
    .Validate(s => !string.IsNullOrEmpty(s.Secret) && s.Secret.Length >= 32, "JWT secret must be at least 32 characters")
    .Validate(s => !string.IsNullOrWhiteSpace(s.Issuer), "JwtSettings:Issuer must be set")
    .Validate(s => !string.IsNullOrWhiteSpace(s.Audience), "JwtSettings:Audience must be set")
    .Validate(s => s.RefreshTokenExpiryInDays > 0, "RefreshTokenExpiryInDays must be set")
    .Validate(s => s.AccessTokenExpiryInMinutes > 0, "AccessTokenExpiryInMinutes must be set")
    .ValidateOnStart();

builder.Services.AddOptions<AppSettings>()
    .BindConfiguration(AppSettings.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ForwardedHeadersSettings>()
    .BindConfiguration(ForwardedHeadersSettings.SectionName)
    .ValidateDataAnnotations()
    .Validate(s => s.KnownNetworks.All(n => System.Net.IPNetwork.TryParse(n, out _)),
        "ForwardedHeaders:KnownNetworks has an entry that is not valid CIDR")
    .ValidateOnStart();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    var settings = builder.Configuration
        .GetSection(ForwardedHeadersSettings.SectionName)
        .Get<ForwardedHeadersSettings>() ?? new();

    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardedForHeaderName = settings.ClientIpHeader;
    options.ForwardLimit = settings.ForwardLimit;

    // Both lists ship with loopback already trusted so they are emptied before the one
    // network we actually trust is added.
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    foreach (var cidr in settings.KnownNetworks)
    {
        var parsed = System.Net.IPNetwork.Parse(cidr);
        options.KnownIPNetworks.Add(new(parsed.BaseAddress, parsed.PrefixLength));
    }
});

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()
    ?? throw new InvalidOperationException("JwtSettings section is missing");


// Email
builder.Services.AddOptions<EmailSettings>()
    .Bind(builder.Configuration.GetSection("EmailSettings"))
    .Validate(s => !string.IsNullOrWhiteSpace(s.FromEmail), "EmailSettings:FromEmail must be set")
    .Validate(s => s.Provider is "Smtp" or "SendGrid", "EmailSettings:Provider must be Smtp or SendGrid")
    .Validate(s => s.Provider != "SendGrid" || !string.IsNullOrWhiteSpace(s.SendGridApiKey),
        "EmailSettings:SendGridApiKey must be set when the provider is SendGrid")
    .ValidateOnStart();

builder.Services.AddOptions<SmtpSettings>()
    .Bind(builder.Configuration.GetSection("Smtp"))
    .Validate(s => builder.Configuration["EmailSettings:Provider"] != "Smtp"
        || (!string.IsNullOrWhiteSpace(s.Host) && !string.IsNullOrWhiteSpace(s.UserName) && !string.IsNullOrWhiteSpace(s.Password)),
        "Smtp:Host, Smtp:UserName and Smtp:Password must be set when the provider is Smtp")
    .ValidateOnStart();

if (builder.Configuration["EmailSettings:Provider"] == "SendGrid")
    builder.Services.AddScoped<IEmailSender, SendGridEmailSender>();
else
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();


builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Without this the legacy handler rewrites inbound claim names, so "sub" arrives as
    // ClaimTypes.NameIdentifier and any lookup for the real name silently finds nothing.
    options.MapInboundClaims = false;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        ClockSkew = TimeSpan.Zero // Remove default 5 minute clock skew
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];

            
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        }

        // A rejected request short circuits before the controller so it writes no audit row.
        // Without this line the only record of a throttled caller is that nothing happened.
        context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiting")
            .LogWarning("Rate limit hit on {Path} by {Client}",
                context.HttpContext.Request.Path, UserOrClientKey(context.HttpContext));

        return ValueTask.CompletedTask;
    };

    options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 15,
            }));

    options.AddPolicy(RateLimitPolicies.Verification, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(5),
                PermitLimit = 10,
            }));

    // Authenticated callers are keyed by user and not by address because an office behind one
    // NAT shares an address and would otherwise throttle each other.
    options.AddPolicy(RateLimitPolicies.General, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: UserOrClientKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 60,
            }));
});

// Application Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddOptions<GoogleAuthSettings>()
    .Bind(builder.Configuration.GetSection("Google"))
    .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId), "Google:ClientId must be set")
    .Validate(s => !string.IsNullOrWhiteSpace(s.ClientSecret), "Google:ClientSecret must be set")
    .ValidateOnStart();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddOptions<FileStorageSettings>()
    .Bind(builder.Configuration.GetSection("FileStorage"))
    .Validate(s => s.AllowedExtensions.Count > 0,
        "FileStorage:AllowedExtensions must not be empty")
    .Validate(s => s.MaxFileSizeInMB > 0,
        "FileStorage:MaxFileSizeInMB must be > 0")
    .Validate(s => s.AllowedExtensions.All(FileUploadRules.CanVerify),
        "FileStorage:AllowedExtensions contains a type with no magic-byte signature")
    .ValidateOnStart();

var fileStorageSettings =
    builder.Configuration.GetSection("FileStorage").Get<FileStorageSettings>() ?? new();
if (fileStorageSettings.Provider == "Azure")
    builder.Services.AddScoped<IFileStorageService, AzureBlobStorageService>();
else
    builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddSignalR();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<INotificationBroadcaster, SignalRNotificationBroadcaster>();
builder.Services.AddScoped<IKanbanBroadcaster, KanbanBroadcaster>();
builder.Services.AddScoped<ITreeProgressBroadcaster, TreeProgressBroadcaster>();
builder.Services.AddScoped<IBackgroundJobService, BackgroundJobService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<NotificationBackgroundJob>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<AttachmentPurgeJob>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IGroupCodeGenerator, GroupCodeGenerator>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();

// PayPal
builder.Services.AddOptions<PayPalSettings>()
    .Bind(builder.Configuration.GetSection("PayPal"))
    .Validate(s => !string.IsNullOrWhiteSpace(s.ClientId), "PayPal:ClientId must be set")
    .Validate(s => !string.IsNullOrWhiteSpace(s.ClientSecret), "PayPal:ClientSecret must be set")
    .Validate(s => Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var url)
        && url.Scheme == Uri.UriSchemeHttps,
        "PayPal:BaseUrl must be an absolute https url")
    .Validate(s => s.OneTimePrice > 0, "PayPal:OneTimePrice must be > 0")
    .Validate(s => !string.IsNullOrWhiteSpace(s.Currency), "PayPal:Currency must be set")
    .ValidateOnStart();
builder.Services.AddHttpClient<IPayPalService, PayPalService>();


// Cache 
builder.Services.AddMemoryCache();

// HttpContext for accessing request information
builder.Services.AddHttpContextAccessor();

// Controllers
builder.Services.AddControllers();

// CORS 
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var frontendUrl = builder.Configuration["App:FrontendUrl"]!;
        policy.WithOrigins(frontendUrl)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});


// Hangfire

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
    {
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("HangfireConnection"));
    }, new PostgreSqlStorageOptions
    {
        QueuePollInterval = TimeSpan.FromSeconds(30)
    }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
    options.SchedulePollingInterval = TimeSpan.FromMinutes(1);
});

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Task Management API",
        Version = "v1",
        Description = "Enterprise Task Management System API"
    });

    // Add JWT Authentication to Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token in the format: Bearer {token}"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        }
    });
});

// Due dates and the dashboard resolve zones by name. An image without tzdata fails every lookup,
// so refuse to start instead of rejecting every group creation with a 400.
if (!TimeZoneRules.TryResolve("Europe/Belgrade", out _))
    throw new InvalidOperationException("Time zone data is missing. Install tzdata in the runtime image.");

var app = builder.Build();


// Middleware for Exception handlin
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseForwardedHeaders();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Task Management API v1");
        options.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();   // 1. HTTPS first
app.UseCors("AllowFrontend"); // 2. CORS before auth

// The provider root is the avatars folder and not the uploads root so attachments have no
// reachable path here. PhysicalFileProvider throws when the folder is missing so a fresh
// volume would fail to boot without the CreateDirectory.
var localStorage = app.Services
    .GetRequiredService<IOptions<FileStorageSettings>>().Value.LocalStorage;

var avatarsPath = Path.GetFullPath(
    Path.Combine(localStorage.BasePath, "avatars"), app.Environment.ContentRootPath);

Directory.CreateDirectory(avatarsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(avatarsPath),
    RequestPath = "/files/avatars",
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff"
});
app.UseAuthentication();      // 3. Auth
app.UseAuthorization();       // 4. Authorization
app.UseRateLimiter();
// Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter(app.Environment) }
});

app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<KanbanHub>("/hubs/kanban");
app.MapControllers();

// Hangfire Jobs
using (var scope = app.Services.CreateScope())
{
    var backgroundJobsService = scope.ServiceProvider.GetRequiredService<IBackgroundJobService>();
    backgroundJobsService.SetupRecurringJobs();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();

static string ClientKey(HttpContext context) =>
    "ip:" + context.GetClientIpAddress();

static string UserOrClientKey(HttpContext context) =>
    context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) is string sub
        ? "u:" + sub
        : ClientKey(context);
