
using System.ComponentModel.DataAnnotations;


namespace TaskManagement.Core.DTO.Auth
{
    public class CheckEmailDto
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; } = string.Empty;
    }
}
