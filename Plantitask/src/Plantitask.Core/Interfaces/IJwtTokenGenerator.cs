using Plantitask.Core.Entities;

namespace Plantitask.Core.Interfaces
{
    public interface IJwtTokenGenerator
    {
        string GenerateAccessToken(User user);
        string GenerateRefreshToken();
    }
}
