using System.Net;
using System.Text.Json;

namespace TaskManagement.Api.Middleware
{
    public class ExceptionHandlingMiddleware
    {

        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;


        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }


        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            } 
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var (statusCode, message) = exception switch
            {
                KeyNotFoundException ex => (HttpStatusCode.NotFound, ex.Message),
                FileNotFoundException ex => (HttpStatusCode.NotFound, ex.Message),
                UnauthorizedAccessException ex => (HttpStatusCode.Forbidden, ex.Message),
                InvalidOperationException ex => (HttpStatusCode.BadRequest, ex.Message),
                _ => (HttpStatusCode.InternalServerError, "An unexpected error occured")
            };

            if (statusCode == HttpStatusCode.InternalServerError)
            {
                _logger.LogError(exception, "Unhandled exception for {Method} {Path}",
                    context.Request.Method, context.Request.Path);
            }
            else
            {
                _logger.LogWarning("Handled exception for {Method} {Path}: {ExceptionType} - {Message}",
                    context.Request.Method, context.Request.Path,
                    exception.GetType().Name, exception.Message);
            }

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";


            var response = new
            {
                status = (int)statusCode,
                message = message
            };

            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}
