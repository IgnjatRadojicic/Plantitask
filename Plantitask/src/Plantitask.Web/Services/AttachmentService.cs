using System.Net.Http.Headers;
using System.Net.Http.Json;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services;

public class AttachmentService : BaseApiService, IAttachmentService
{
    public AttachmentService(HttpClient http) : base(http) { }

    public async Task<ServiceResult<AttachmentDto>> UploadAsync(
        Guid taskId, Stream fileStream, string fileName, string contentType)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);

            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", fileName);

            var response = await Http.PostAsync($"api/tasks/{taskId}/attachments", content);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<AttachmentDto>();
                return data != null
                    ? ServiceResult<AttachmentDto>.Ok(data)
                    : ServiceResult<AttachmentDto>.Fail("Could not read server response.");
            }


            var error = await ReadErrorResponse(response);
            return ServiceResult<AttachmentDto>.Fail(error);
        }
        catch (HttpRequestException)
        {
            return ServiceResult<AttachmentDto>.Fail(
                "Cannot reach the server. Please check your connection.");
        }
    }

    public Task<ServiceResult<List<AttachmentDto>>> GetTaskAttachmentsAsync(Guid taskId)
        => GetAsync<List<AttachmentDto>>($"api/tasks/{taskId}/attachments");

    public Task<ServiceResult<AttachmentDto>> GetByIdAsync(Guid taskId, Guid attachmentId)
        => GetAsync<AttachmentDto>($"api/tasks/{taskId}/attachments/{attachmentId}");

    public Task<ServiceResult<bool>> DeleteAsync(Guid taskId, Guid attachmentId)
        => DeleteAsync<bool>($"api/tasks/{taskId}/attachments/{attachmentId}");

    private static async Task<string> ReadErrorResponse(HttpResponseMessage response)
    {
        try
        {
            var apiError = await response.Content.ReadFromJsonAsync<ApiError>();
            return apiError?.Message ?? $"Request failed ({(int)response.StatusCode})";
        }
        catch
        {
            return $"Request failed ({(int)response.StatusCode})";
        }
    }
}