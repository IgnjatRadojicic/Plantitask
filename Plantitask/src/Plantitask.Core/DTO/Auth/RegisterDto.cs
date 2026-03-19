
using System.ComponentModel.DataAnnotations;
namespace Plantitask.Core.DTO.Auth
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "Username is required!")]
        [StringLength(24, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 24 characters!")]
        [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username can only contain letters, numbers, and underscores")]
        public string UserName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required!")]
        [EmailAddress(ErrorMessage = "Invalid Email Format")]
        [StringLength(100, ErrorMessage = "Email must be under 100 characters!")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required!")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be between 3 and 100 characters")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&#])[A-Za-z\d@$!%*?&#]{8,}$",
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string FirstName { get; set; }

        [Required]
        [StringLength(50)]
        public string LastName { get; set; }
    }
}
