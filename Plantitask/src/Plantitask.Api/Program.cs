using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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

    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 15;
    });

    options.AddFixedWindowLimiter("verification", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(5);
        opt.PermitLimit = 10;
    });

    options.AddFixedWindowLimiter("general", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 60;
    });
});

// Application Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.Configure<GoogleAuthSettings>(
    builder.Configuration.GetSection("Google"));
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
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IGroupCodeGenerator, GroupCodeGenerator>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();

// PayPal
builder.Services.Configure<PayPalSettings>(
    builder.Configuration.GetSection("PayPal"));
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

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();


// Middleware for Exception handlin
app.UseMiddleware<ExceptionHandlingMiddleware>();

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

// Serve uploaded files
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(app.Environment.ContentRootPath, "uploads")),
    RequestPath = "/files/avatars"
});

app.Run();