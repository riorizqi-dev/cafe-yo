using Microsoft.AspNetCore.Identity;

namespace cafe_yo.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? FullName { get; set; }
        public string? Role { get; set; }
    }
}
