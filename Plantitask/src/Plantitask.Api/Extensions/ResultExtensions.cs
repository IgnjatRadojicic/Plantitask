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
            var body = new { status = (int)error.StatusCode, message = error.Message };
            return new ObjectResult(body) { StatusCode = (int)error.StatusCode };
        }
    }
}