using System.Net;
using System.Net.Http.Json;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services;

public abstract class BaseApiService
{
    protected readonly HttpClient Http;

    protected BaseApiService(HttpClient http)
    {
        Http = http;
    }

    protected async Task<ServiceResult<T>> GetAsync<T>(string url)
    {
        try
        {
            var response = await Http.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return ServiceResult<T>.Ok(default!);

                var data = await response.Content.ReadFromJsonAsync<T>();
                return data != null
                    ? ServiceResult<T>.Ok(data)
                    : ServiceResult<T>.Fail("Could not read server response.");
            }

            var error = await ReadError(response);
            return ServiceResult<T>.Fail(error);
        }
        catch (HttpRequestException)
        {
            return ServiceResult<T>.Fail(
                "Cannot reach the server. Please check your connection.");
        }
    }

    protected async Task<ServiceResult<T>> PostAsync<T>(string url, object payload)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(url, payload);

            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return ServiceResult<T>.Ok(default!);

                var data = await response.Content.ReadFromJsonAsync<T>();
                return data != null
                    ? ServiceResult<T>.Ok(data)
                    : ServiceResult<T>.Fail("Could not read server response.");
            }

            var error = await ReadError(response);
            return ServiceResult<T>.Fail(error);
        }
        catch (HttpRequestException)
        {
            return ServiceResult<T>.Fail(
                "Cannot reach the server. Please check your connection.");
        }
    }

    protected async Task<ServiceResult<T>> PutAsync<T>(string url, object payload)
    {
        try
        {
            var response = await Http.PutAsJsonAsync(url, payload);

            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return ServiceResult<T>.Ok(default!);

                var data = await response.Content.ReadFromJsonAsync<T>();
                return data != null
                    ? ServiceResult<T>.Ok(data)
                    : ServiceResult<T>.Fail("Could not read server response.");
            }

            var error = await ReadError(response);
            return ServiceResult<T>.Fail(error);
        }
        catch (HttpRequestException)
        {
            return ServiceResult<T>.Fail(
                "Cannot reach the server. Please check your connection.");
        }
    }


    protected async Task<ServiceResult<T>> PatchAsync<T>(string url, object? data = null)
    {
        try
        {
            var response = data is not null
                ? await Http.PatchAsJsonAsync(url, data)
                : await Http.PatchAsync(url, null);

            if(!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return ServiceResult<T>.Fail(error);
            }

            var result = await response.Content.ReadFromJsonAsync<T>();
            return ServiceResult<T>.Ok(result!);
        }
        catch (Exception ex)
        {
            return ServiceResult<T>.Fail(ex.Message);
        }
    }

    protected async Task<ServiceResult<T>> DeleteAsync<T>(string url)
    {
        try
        {
            var response = await Http.DeleteAsync(url);

            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return ServiceResult<T>.Ok(default!);

                var data = await response.Content.ReadFromJsonAsync<T>();
                return data != null
                    ? ServiceResult<T>.Ok(data)
                    : ServiceResult<T>.Fail("Could not read server response.");
            }

            var error = await ReadError(response);
            return ServiceResult<T>.Fail(error);
        }
        catch (HttpRequestException)
        {
            return ServiceResult<T>.Fail(
                "Cannot reach the server. Please check your connection.");
        }
    }

    private static async Task<string> ReadError(HttpResponseMessage response)
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