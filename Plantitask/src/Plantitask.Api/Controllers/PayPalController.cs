
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Paypal;
using Plantitask.Core.Interfaces;
 
namespace Plantitask.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PremiumController : BaseApiController
    {

        private readonly IPayPalService _paypal;
        private readonly IAuditService _auditService;
        private readonly ILogger<PremiumController> _logger;

        public PremiumController(
            IPayPalService paypal,
            IAuditService auditService,
            ILogger<PremiumController> logger)
        {
            _paypal = paypal;
            _auditService = auditService;
            _logger = logger;
        }


        [HttpGet("status")]
        [ProducesResponseType(typeof(PremiumStatusDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetStatus()
        {
            var userId = GetUserId();
            var result = await _paypal.GetPremiumStatusAsync(userId);
            return result.ToActionResult();
        }

        [HttpPost("subscribe")]
        [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateSubscription([FromBody] CreateSubscriptionRequest request)
        {
            var userId = GetUserId();
            var result = await _paypal.CreateSubscriptionAsync(userId, request);
            return result.ToActionResult();
        }

        [HttpPost("subscribe/activate")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ActivateSubscription([FromQuery] string subscriptionId)
        {
            var userId = GetUserId();
            var result = await _paypal.ActivateSubscriptionAsync(userId, subscriptionId);

            if (result.IsFailure)
                return result.ToActionResult();

            await LogAuditAsync(
                _auditService,
                entityType: "User",
                entityId: userId,
                action: "PremiumActivated",
                propertyName: "SubscriptionType",
                newValue: "recurring");

            _logger.LogInformation("User {UserId} activated recurring subscription {SubId}",
                userId, subscriptionId);

            return Ok(new { message = "Premium activated!" });
        }
        [HttpPost("onetime")]
        [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreateOneTimeOrder([FromBody] CreateOrderRequest request)
        {
            var userId = GetUserId();
            var result = await _paypal.CreateOneTimeOrderAsync(userId, request);
            return result.ToActionResult();
        }


        [HttpPost("onetime/capture")]
        [ProducesResponseType(typeof(CaptureOrderResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CaptureOrder([FromQuery] string orderId)
        {
            var userId = GetUserId();
            var result = await _paypal.CaptureOrderAsync(userId, orderId);

            if (result.IsFailure)
                return result.ToActionResult();

            var capture = result.Value!;

            if (capture.Success)
            {
                await LogAuditAsync(
                    _auditService,
                    entityType: "User",
                    entityId: userId,
                    action: "PremiumActivated",
                    propertyName: "SubscriptionType",
                    newValue: "onetime");

                _logger.LogInformation("User {UserId} captured one-time order {OrderId}",
                    userId, orderId);
            }

            return Ok(capture);
        }

        [HttpPost("cancel")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CancelPremium()
        {
            var userId = GetUserId();
            var result = await _paypal.CancelSubscriptionAsync(userId);

            if (result.IsFailure)
                return result.ToActionResult();

            await LogAuditAsync(
                _auditService,
                entityType: "User",
                entityId: userId,
                action: "PremiumCancelled",
                propertyName: "IsPremium",
                oldValue: "true",
                newValue: "false");

            _logger.LogInformation("User {UserId} cancelled premium", userId);

            return Ok(new { message = "Premium cancelled" });
        }
        /// <summary>
        /// The status code returned here is a protocol with PayPal's retry system. A 2xx means
        /// never send this again so it must only be returned once the event is committed. A
        /// processing failure throws out of the service and the middleware turns it into a 500
        /// which makes PayPal redeliver, and that is the behaviour we want.
        /// </summary>
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            var headers = new Dictionary<string, string>();
            foreach (var key in new[]
            {
                "PAYPAL-AUTH-ALGO", "PAYPAL-CERT-URL",
                "PAYPAL-TRANSMISSION-ID", "PAYPAL-TRANSMISSION-SIG",
                "PAYPAL-TRANSMISSION-TIME"
            })
            {
                if (Request.Headers.TryGetValue(key, out var val))
                    headers[key] = val.ToString();
            }

            var result = await _paypal.HandleWebhookAsync(body, headers);

            return result.ToActionResult();
        }
    }
}
