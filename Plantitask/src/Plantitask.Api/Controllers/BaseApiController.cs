using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Plantitask.Api.Extensions;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers
{
    public abstract class BaseApiController : ControllerBase
    {
        protected Guid GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

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
            await auditService.LogAsync(
                entityType: entityType,
                entityId: entityId,
                action: action,
                userId: GetUserId(),
                groupId: groupId,
                ipAddress: GetClientIpAddress(),
                userAgent: GetUserAgent(),
                propertyName: propertyName,
                oldValue: oldValue,
                newValue: newValue,
                reason: reason);
        }
    }
}
