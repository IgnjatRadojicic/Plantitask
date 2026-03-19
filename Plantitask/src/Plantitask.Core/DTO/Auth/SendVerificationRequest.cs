using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Auth
{
    public class SendVerificationRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
    }
}
