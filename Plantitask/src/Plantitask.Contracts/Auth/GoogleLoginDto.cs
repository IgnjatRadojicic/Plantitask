
using System.ComponentModel.DataAnnotations;


namespace Plantitask.Core.DTO.Auth
{
    public class GoogleLoginDto
    {
        [Required(ErrorMessage = "Google ID token is required")]
        public string IdToken { get; set; } = string.Empty;
    }
}
