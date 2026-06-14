using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using cafe_yo.Models;
using cafe_yo.Security;

namespace cafe_yo.Data
{
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            await EnsureRoleColumnAsync(db);

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            // Keep existing operational roles (Supervisor/Kasir/Koki/Owner/etc) intact.
            // Removing non-admin roles can break dashboard API authorization and cause
            // supervisor pages to stay in perpetual "Memuat..." state on failed fetches.

            // Ensure all operational roles exist.
            var requiredRoles = new[]
            {
                AppRoles.Admin,
                AppRoles.Owner,
                AppRoles.Supervisor,
                AppRoles.Kasir,
                AppRoles.Koki,
                AppRoles.DapurLegacy,
                AppRoles.Customer
            };
            foreach (var role in requiredRoles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            await SyncRolesFromUserColumnAsync(userManager, roleManager, requiredRoles);

            await EnsureStaffAsync(userManager, "admin", "admin123", "Admin", AppRoles.Admin);
        }

        private static async Task EnsureRoleColumnAsync(ApplicationDbContext db)
        {
            const string sql = @"
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.AspNetUsers', 'Role') IS NULL
    BEGIN
        ALTER TABLE dbo.AspNetUsers ADD Role NVARCHAR(20) NULL;
    END;
END;";
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        private static async Task EnsureStaffAsync(
            UserManager<ApplicationUser> userManager,
            string username,
            string password,
            string fullName,
            string role)
        {
            var user = await userManager.FindByNameAsync(username);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = username,
                    FullName = fullName,
                    EmailConfirmed = true,
                    Role = role.ToLowerInvariant()
                };

                var create = await userManager.CreateAsync(user, password);
                if (!create.Succeeded)
                {
                    var msg = string.Join(", ", create.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Failed to seed user {username}: {msg}");
                }
            }
            else
            {
                user.FullName = string.IsNullOrWhiteSpace(user.FullName) ? fullName : user.FullName;
                user.EmailConfirmed = true;
                user.Role = role.ToLowerInvariant();
                await userManager.UpdateAsync(user);
            }

            var currentRoles = await userManager.GetRolesAsync(user);
            var removeRoles = currentRoles.Where(r => !string.Equals(r, role, StringComparison.OrdinalIgnoreCase)).ToList();
            if (removeRoles.Count > 0)
            {
                await userManager.RemoveFromRolesAsync(user, removeRoles);
            }

            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }
        }

        private static async Task RemoveNonAdminRolesAsync(RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager)
        {
            var users = userManager.Users.ToList();
            foreach (var user in users)
            {
                var roles = await userManager.GetRolesAsync(user);
                var nonAdminRoles = roles.Where(r =>
                    !string.Equals(r, AppRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(r, AppRoles.Customer, StringComparison.OrdinalIgnoreCase)).ToList();
                if (nonAdminRoles.Count > 0)
                {
                    await userManager.RemoveFromRolesAsync(user, nonAdminRoles);
                }
            }

            var allRoles = roleManager.Roles.ToList();
            foreach (var role in allRoles)
            {
                if (!string.Equals(role.Name, AppRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(role.Name, AppRoles.Customer, StringComparison.OrdinalIgnoreCase))
                {
                    await roleManager.DeleteAsync(role);
                }
            }
        }

        private static async Task SyncRolesFromUserColumnAsync(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IEnumerable<string> knownRoles)
        {
            var knownRoleMap = knownRoles.ToDictionary(x => x.ToLowerInvariant(), x => x);
            foreach (var user in userManager.Users.ToList())
            {
                var legacy = (user.Role ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(legacy)) continue;
                if (!knownRoleMap.TryGetValue(legacy, out var canonicalRole)) continue;
                if (!await roleManager.RoleExistsAsync(canonicalRole)) continue;
                if (await userManager.IsInRoleAsync(user, canonicalRole)) continue;
                await userManager.AddToRoleAsync(user, canonicalRole);
            }
        }
    }
}
