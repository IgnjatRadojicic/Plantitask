using System.Net.Http.Headers;
using System.Net.Http.Json;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

using Plantitask.Core.DTO.Users;
using Plantitask.Core.DTO.Paypal;
namespace Plantitask.Web.Services;

public class UserProfileService : BaseApiService, IUserProfileService
{
    public UserProfileService(HttpClient http) : base(http) { }

    public Task<ServiceResult<UserProfileDto>> GetProfileAsync()
        => GetAsync<UserProfileDto>("api/user/profile");

    public Task<ServiceResult<UserProfileDto>> UpdateProfileAsync(UpdateUserProfileDto dto)
        => PutAsync<UserProfileDto>("api/user/profile", dto);

    public async Task<ServiceResult<ProfilePictureResponse>> UploadProfilePictureAsync(
        Stream fileStream, string fileName, string contentType)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "file", fileName);

            var response = await Http.PostAsync("api/user/profile/picture", content);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<ProfilePictureResponse>();
                return data is not null
                    ? ServiceResult<ProfilePictureResponse>.Ok(data)
                    : ServiceResult<ProfilePictureResponse>.Fail("Could not read server response.");
            }

            var error = await ReadErrorResponse(response);
            return ServiceResult<ProfilePictureResponse>.Fail(error);
        }
        catch (HttpRequestException)
        {
            return ServiceResult<ProfilePictureResponse>.Fail(
                "Cannot reach the server. Please check your connection.");
        }
    }

    public Task<ServiceResult<MessageResponse>> RemoveProfilePictureAsync()
        => DeleteAsync<MessageResponse>("api/user/profile/picture");

    public Task<ServiceResult<MessageResponse>> ChangePasswordAsync(ChangePasswordDto dto)
        => PostAsync<MessageResponse>("api/user/profile/change-password", dto);

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