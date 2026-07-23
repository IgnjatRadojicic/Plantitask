

namespace Plantitask.Core.DTO.Auth
{
    public class AuthResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public DateTime AccessTokenExpiresAt { get; set; } 
        public DateTime RefreshTokenExpiresAt { get; set; } 
    }
}
