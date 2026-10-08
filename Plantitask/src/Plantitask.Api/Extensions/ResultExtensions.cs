using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Plantitask.Core.Common;

namespace Plantitask.Api.Extensions
{
    public static class ResultExtensions
    {
        public static IActionResult ToActionResult<T>(this Result<T> result)
        {
            if (result.IsSuccess)
            {
                return result.Value is null
                    ? new NoContentResult()
                    : new OkObjectResult(result.Value);
            }

            return ToErrorResponse(result.Error!);
        }

        public static IActionResult ToActionResult(this Result result)
        {
            if (result.IsSuccess)
                return new NoContentResult();

            return ToErrorResponse(result.Error!);
        }

        private static IActionResult ToErrorResponse(Error error)
        {
            // An unmapped ErrorType becomes a 500 so adding one means updating this switch by hand.
            var status = error.Type switch
            {
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                ErrorType.BadRequest => StatusCodes.Status400BadRequest,
                ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status500InternalServerError
            };

            return new ProblemResult(status, error.Message, error.Code);
        }

        // A static extension method cannot reach DI, but a result object can because MVC
        // executes it later with the ActionContext in hand. That is the only reason this
        // type exists rather than building the ProblemDetails inline.
        private sealed class ProblemResult : IActionResult
        {
            private readonly int _status;
            private readonly string _detail;
            private readonly string _errorType;

            public ProblemResult(int status, string detail, string errorType)
            {
                _status = status;
                _detail = detail;
                _errorType = errorType;
            }
            public async Task ExecuteResultAsync(ActionContext context) {
                var factory = context.HttpContext.RequestServices
                    .GetRequiredService<ProblemDetailsFactory>();

                var problem = factory.CreateProblemDetails(
                    context.HttpContext, statusCode: _status, detail: _detail);

                problem.Extensions["errorType"] = _errorType;

                await new ObjectResult(problem) { StatusCode = _status}
                .ExecuteResultAsync(context); 
            }
        }
    }
}