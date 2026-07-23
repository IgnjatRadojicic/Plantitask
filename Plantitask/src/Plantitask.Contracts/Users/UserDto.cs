using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Users
{
    public class UserDto
    {

        public Guid Id { get; set; }
        public string UserName { get; set; } = string.Empty; 
        public string Email { get; set; } = string.Empty; 

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

    }
}
