
using System.ComponentModel.DataAnnotations;
namespace Plantitask.Contracts.DTO.Auth
{
    public class RefreshTokenDto
    {
        [Required(ErrorMessage = "Refresh token is required!")]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
