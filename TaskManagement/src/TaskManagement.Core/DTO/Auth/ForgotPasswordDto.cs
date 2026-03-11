
using System.ComponentModel.DataAnnotations;
namespace TaskManagement.Core.DTO.Auth
{
    public class ForgotPasswordDto
    {
        [Required(ErrorMessage = "Email is required!")]
        [EmailAddress(ErrorMessage = "Invalid Email Address")]
        public string Email { get; set; } = string.Empty;
    }
}
