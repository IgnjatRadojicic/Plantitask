
using System.ComponentModel.DataAnnotations;
namespace Plantitask.Core.DTO.Auth
{
    public class RefreshTokenDto
    {
        [Required(ErrorMessage = "Refresh token is required!")]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
