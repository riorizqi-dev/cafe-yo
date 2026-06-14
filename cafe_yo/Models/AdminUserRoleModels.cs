using System.Collections.Generic;

namespace cafe_yo.Models
{
    public sealed class AdminUserListItem
    {
        public string Id { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public string Roles { get; set; } = string.Empty;
        public bool IsLockedOut { get; set; }
    }

    public sealed class AdminEditUserRolesVM
    {
        public string UserId { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public string? FullName { get; set; }
        public List<RoleCheckbox> Roles { get; set; } = new();
    }

    public sealed class RoleCheckbox
    {
        public string Name { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public sealed class AdminCreateUserVM
    {
        public string? FullName { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? RoleName { get; set; }
        public List<string> AvailableRoles { get; set; } = new();
    }
}
