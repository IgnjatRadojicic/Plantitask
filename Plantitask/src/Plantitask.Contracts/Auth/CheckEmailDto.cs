
using System.ComponentModel.DataAnnotations;


namespace Plantitask.Core.DTO.Auth
{
    public class CheckEmailDto
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; } = string.Empty;
    }
}
