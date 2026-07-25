
using Plantitask.Core.Models;

namespace Plantitask.Core.Interfaces
{
    public interface IEmailSender
    {
        Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }
}
