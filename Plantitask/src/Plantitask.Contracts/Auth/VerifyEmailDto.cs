
using System.ComponentModel.DataAnnotations;

namespace Plantitask.Core.DTO.Auth
{
    public class VerifyEmailDto
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Verification code is required")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits")]
        public string Code { get; set; } = string.Empty;
    }
}
