using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Plantitask.Api.Extensions;
using Plantitask.Core.Interfaces;
using Plantitask.Core.DTO.Audit;
using System.IdentityModel.Tokens.Jwt;

namespace Plantitask.Api.Controllers
{
    // Inherited by every controller so a new one cannot be written without it. Without it a
    // binding failure is recorded in ModelState and never read so the action runs on
    // default values and answers 200.
    [ApiController]
    public abstract class BaseApiController : ControllerBase
    {
        protected Guid GetUserId()
        {
            var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("User ID not found in token");
            }
            return userId;
        }

        protected string GetClientIpAddress()
        {
            return HttpContext.GetClientIpAddress();
        }

        protected string GetUserAgent()
        {
            return HttpContext.GetUserAgent();
        }

        protected async Task LogAuditAsync(
            IAuditService auditService,
            string entityType,
            Guid entityId,
            string action,
            Guid? groupId = null,
            string? propertyName = null,
            string? oldValue = null,
            string? newValue = null,
            string? reason = null)
        {
            await auditService.LogAsync(new CreateAuditLogRequest
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                UserId = GetUserId(),
                // MapInboundClaims is false so inbound claims keep their JWT names and the
                // ClaimTypes aliases can never arrive. JwtTokenGenerator mints sub, email,
                // unique_name and jti and nothing else.
                UserName = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName) ?? "unknown",
                UserEmail = User.FindFirstValue(JwtRegisteredClaimNames.Email) ?? "unknown",
                GroupId = groupId,
                IpAddress = GetClientIpAddress(),
                UserAgent = GetUserAgent(),
                PropertyName = propertyName,
                OldValue = oldValue,
                NewValue = newValue,
                Reason = reason
            });
        }
    }
}
