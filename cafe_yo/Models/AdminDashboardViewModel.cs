using System.Collections.Generic;

namespace cafe_yo.Models
{
    public class AdminUserDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
    }

    public class AdminDashboardViewModel
    {
        public List<AdminUserDto> Users { get; set; } = new List<AdminUserDto>();

        // summary
        public int TotalUsers { get; set; }
        public int AdminCount { get; set; }
        public int NonAdminCount { get; set; }
        public int OnlineCount { get; set; }
        public int TotalRoleAdmin { get; set; }
        public int TotalRoleKasir { get; set; }
    }
}