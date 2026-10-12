using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Plantitask.Api.Filters;
using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Services
{
    /// <summary>
    /// Answers ICurrentActor from request. No HttpContext means a Hangfire worker raised
    /// the event. An endpoint marked [EventSource] names its own door and every other request is a
    /// Request.
    /// </summary>
    public class HttpCurrentActor : ICurrentActor
    {
        private readonly IHttpContextAccessor _accessor;

        public HttpCurrentActor(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public EventSource Source
        {
            get
            {
                var context = _accessor.HttpContext;
                if (context == null)
                    return EventSource.Job;

                return context.GetEndpoint()?.Metadata.GetMetadata<EventSourceAttribute>()?.Source
                    ?? EventSource.Request;
            }
        }

        public EventActor? Actor
        {
            get
            {
                var user = _accessor.HttpContext?.User;
                if (user?.Identity?.IsAuthenticated != true)
                    return null;

                var id = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
                var userName = user.FindFirstValue(JwtRegisteredClaimNames.UniqueName);
                var email = user.FindFirstValue(JwtRegisteredClaimNames.Email);

                if (!Guid.TryParse(id, out var userId) || userName == null || email == null)
                    throw new UnauthorizedAccessException("Token is missing sub unique_name or email");

                return new EventActor(userId, userName, email);
            }
        }
    }
}
