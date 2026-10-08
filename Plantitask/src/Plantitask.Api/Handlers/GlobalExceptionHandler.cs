using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Plantitask.Api.Handlers
{
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;
        private readonly IProblemDetailsService _problemDetailsService;

        public GlobalExceptionHandler(
            ILogger<GlobalExceptionHandler> logger,
            IProblemDetailsService problemDetailsService)
        {
            _logger = logger;
            _problemDetailsService = problemDetailsService;
        }

        public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is not UnauthorizedAccessException)
                return false;
            _logger.LogWarning("401 for {Method} {Path}: {Reason}",
                context.Request.Method, context.Request.Path, exception.Message);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            { 
                HttpContext = context,
                ProblemDetails = new ProblemDetails { Status = StatusCodes.Status401Unauthorized}
            }


            );
        }
    }
}
