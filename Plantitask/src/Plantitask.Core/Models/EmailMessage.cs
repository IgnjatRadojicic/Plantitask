
namespace Plantitask.Core.Models
{
    public record EmailMessage(string ToEmail, string Subject, string HtmlContent, string EmailType);
}
