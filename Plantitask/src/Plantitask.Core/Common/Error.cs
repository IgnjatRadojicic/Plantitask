
using System.Net;

namespace Plantitask.Core.Common
{
    public record Error(string Code, string Message, HttpStatusCode StatusCode)
    {
        public static Error NotFound(string message) =>
            new("NotFound", message, HttpStatusCode.NotFound);

        public static Error BadRequest(string message) =>
            new("BadRequest", message, HttpStatusCode.BadRequest);

        public static Error Forbidden(string message) =>
            new("Forbidden", message, HttpStatusCode.Forbidden);

        public static Error Validation(string message) =>
            new("Validation", message, HttpStatusCode.UnprocessableEntity);

        public static Error Conflict(string message) =>
            new("Conflict", message, HttpStatusCode.Conflict);

        public static Error Internal(string message) =>
            new("Internal", message, HttpStatusCode.InternalServerError);
    }
}