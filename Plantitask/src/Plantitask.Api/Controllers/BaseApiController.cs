using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Plantitask.Api.Extensions;
using Plantitask.Core.Interfaces;
using Plantitask.Core.DTO.Audit;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore.Internal;

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
                UserName = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
                                            ?? User.FindFirstValue(ClaimTypes.Name) ?? "unknown",
                UserEmail = User.FindFirstValue(JwtRegisteredClaimNames.Email)
                                             ?? User.FindFirstValue(ClaimTypes.Email) ?? "unknown",
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
