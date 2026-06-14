using System.Collections.Generic;

namespace cafe_yo.Models
{
    public sealed class AdminRoleCreateVM
    {
        public string Name { get; set; } = string.Empty;
        public List<string> ExistingRoles { get; set; } = new();
    }
}
