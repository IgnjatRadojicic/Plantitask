using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Dashboard;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers
{
    [Authorize]
    [ApiController]
    [EnableRateLimiting("general")]
    [Route("api/[controller]")]
    public class DashboardController : BaseApiController
    {
        private readonly IDashboardService _dashboardService;

        public DashboardController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        [HttpGet("personal")]
        [ProducesResponseType(typeof(PersonalDashboardDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPersonalDashboard()
        {
            var userId = GetUserId();
            var result = await _dashboardService.GetPersonalDashboardAsync(userId);
            return result.ToActionResult();
        }

        [HttpGet("field")]
        [ProducesResponseType(typeof(List<FieldTreeDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetFieldData()
        {
            var userId = GetUserId();
            var result = await _dashboardService.GetFieldDataAsync(userId);
            return result.ToActionResult();
        }

        [HttpGet("groups/{groupId}")]
        [ProducesResponseType(typeof(GroupStatisticsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetGroupStatistics(Guid groupId)
        {
            var userId = GetUserId();
            var result = await _dashboardService.GetGroupStatisticsAsync(groupId, userId);
            return result.ToActionResult();
        }
    }
}