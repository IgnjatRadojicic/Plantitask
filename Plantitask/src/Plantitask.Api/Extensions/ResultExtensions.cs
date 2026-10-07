using Microsoft.AspNetCore.Mvc;
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

        public static IActionResult ToCreatedResult<T>(
            this Result<T> result, string routeName, Func<T, object> routeValues)
        {
            if (result.IsFailure)
                return ToErrorResponse(result.Error!);

            return new CreatedAtRouteResult(routeName, routeValues(result.Value!), result.Value);
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

            var body = new { status, message = error.Message };
            return new ObjectResult(body) { StatusCode = status };
        }
    }
}