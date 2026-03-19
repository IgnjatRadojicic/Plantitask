
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Attachments;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers;

[Authorize]
[ApiController]
[EnableRateLimiting("general")]
[Route("api/tasks/{taskId}/attachments")]
public class AttachmentsController : BaseApiController
{
    private readonly IAttachmentService _attachmentService;
    private readonly IAuditService _auditService;
    private readonly ILogger<AttachmentsController> _logger;

    public AttachmentsController(
        IAttachmentService attachmentService,
        IAuditService auditService,
        ILogger<AttachmentsController> logger)
    {
        _attachmentService = attachmentService;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadAttachment(Guid taskId, [FromForm] IFormFile file)
    {
        var userId = GetUserId();
        var result = await _attachmentService.UploadAttachmentAsync(taskId, file, userId);

        if (result.IsFailure)
            return result.ToActionResult();

        var attachment = result.Value!;

        await LogAuditAsync(
            _auditService,
            entityType: "TaskAttachment",
            entityId: attachment.Id,
            action: "Uploaded",
            propertyName: "FileName",
            newValue: attachment.FileName);

        return CreatedAtAction(
            nameof(GetAttachment),
            new { taskId, attachmentId = attachment.Id },
            attachment);
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<AttachmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskAttachments(Guid taskId)
    {
        var userId = GetUserId();
        var result = await _attachmentService.GetTaskAttachmentsAsync(taskId, userId);
        return result.ToActionResult();
    }

    [HttpGet("{attachmentId}")]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachment(Guid taskId, Guid attachmentId)
    {
        var userId = GetUserId();
        var result = await _attachmentService.GetAttachmentByIdAsync(attachmentId, userId);
        return result.ToActionResult();
    }

    [HttpGet("{attachmentId}/download")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachment(Guid taskId, Guid attachmentId)
    {
        var userId = GetUserId();
        var result = await _attachmentService.DownloadAttachmentAsync(attachmentId, userId);

        if (result.IsFailure)
            return result.ToActionResult();

        var (fileStream, fileName, contentType) = result.Value!;
        return File(fileStream, contentType, fileName);
    }

    [HttpDelete("{attachmentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAttachment(Guid taskId, Guid attachmentId)
    {
        var userId = GetUserId();
        var result = await _attachmentService.DeleteAttachmentAsync(attachmentId, userId);

        if (result.IsFailure)
            return result.ToActionResult();

        await LogAuditAsync(
            _auditService,
            entityType: "TaskAttachment",
            entityId: attachmentId,
            action: "Deleted");

        return Ok(new { message = "Attachment deleted successfully" });
    }
}