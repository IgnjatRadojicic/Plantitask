namespace Plantitask.Web.Models
{
    public class ApiError
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public int Status { get; set; }
        public string? ErrorType { get; set; }
        public string? TraceId { get; set; }
    }
}
