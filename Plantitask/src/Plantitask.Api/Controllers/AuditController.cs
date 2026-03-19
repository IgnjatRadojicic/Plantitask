using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Audit;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [EnableRateLimiting("general")]
    [Produces("application/json")]
    public class AuditController : BaseApiController
    {
        private readonly IAuditService _auditService;
        private readonly ILogger<AuditController> _logger;

        public AuditController(IAuditService auditService, ILogger<AuditController> logger)
        {
            _auditService = auditService;
            _logger = logger;
        }

        [HttpGet("groups/{groupId}")]
        [ProducesResponseType(typeof(List<AuditLogDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetGroupHistory(Guid groupId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50)
        {
            var userId = GetUserId();
            var result = await _auditService.GetGroupHistoryAsync(groupId, userId, pageNumber, pageSize);
            return result.ToActionResult();
        }

        [HttpGet("tasks/{taskId}")]
        [ProducesResponseType(typeof(List<AuditLogDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetTaskHistory(Guid taskId)
        {
            var userId = GetUserId();
            var result = await _auditService.GetTaskHistoryAsync(taskId, userId);
            return result.ToActionResult();
        }

        [HttpGet("users/{userId}")]
        [ProducesResponseType(typeof(List<AuditLogDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetUserHistory(Guid userId,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50)
        {
            var requestingUserId = GetUserId();
            var result = await _auditService.GetUserHistoryAsync(userId, requestingUserId, pageNumber, pageSize);
            return result.ToActionResult();
        }
    }
}