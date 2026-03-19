using System.Net.NetworkInformation;

namespace Plantitask.Web.Models
{
    public class ServiceResult<T>
    {

        public bool Success { get; private set; }
        public T? Data { get; private set; }
        public string Error { get; private set; } = string.Empty;

        public static ServiceResult<T> Ok(T data) => new()
        {
            Success = true,
            Data = data
        };

        public static ServiceResult<T> Fail(string error) => new()
        {
            Success = false,
            Error = error
        };


    }
    public class ServiceResult
    {
        public bool Success { get; private set; }
        public string Error { get; private set; } = string.Empty;

        public static ServiceResult Ok() => new() { Success = true };

        public static ServiceResult Fail(string error) => new()
        {
            Success = false,
            Error = error
        };
    }
}