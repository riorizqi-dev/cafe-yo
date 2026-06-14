using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using cafe_yo.Models;
using cafe_yo.Security;
using cafe_yo.Data;
using Microsoft.EntityFrameworkCore;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/Roles")]
    public class AdminRolesController : Controller
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;

        public AdminRolesController(RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager, ApplicationDbContext db)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _db = db;
        }

        [HttpGet("")]
        public IActionResult Index()
        {
            var model = new AdminRoleCreateVM
            {
                ExistingRoles = _roleManager.Roles.Select(r => r.Name ?? string.Empty).ToList()
            };
            return View("~/Views/Admin/Roles/Index.cshtml", model);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AdminRoleCreateVM model)
        {
            var input = (model.Name ?? string.Empty).Trim();
            model.Name = input;
            if (string.IsNullOrWhiteSpace(input))
            {
                ModelState.AddModelError(nameof(AdminRoleCreateVM.Name), "Role name is required.");
            }
            if (!string.IsNullOrWhiteSpace(input) && input.Length > 64)
            {
                ModelState.AddModelError(nameof(AdminRoleCreateVM.Name), "Role name maksimal 64 karakter.");
            }

            if (!ModelState.IsValid)
            {
                model.ExistingRoles = _roleManager.Roles.Select(r => r.Name ?? string.Empty).ToList();
                return View("~/Views/Admin/Roles/Index.cshtml", model);
            }

            var exists = await _roleManager.RoleExistsAsync(input);
            if (!exists)
            {
                var result = await _roleManager.CreateAsync(new IdentityRole(input));
                if (!result.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, string.Join(", ", result.Errors.Select(e => e.Description)));
                    model.ExistingRoles = _roleManager.Roles.Select(r => r.Name ?? string.Empty).ToList();
                    return View("~/Views/Admin/Roles/Index.cshtml", model);
                }
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("ResetAdminOnly")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetAdminOnly()
        {
            // Ensure canonical admin exists first.
            var admin = await _userManager.FindByNameAsync("admin");
            if (admin == null)
            {
                admin = new ApplicationUser
                {
                    UserName = "admin",
                    FullName = "Admin",
                    EmailConfirmed = true,
                    Role = "admin"
                };
                var create = await _userManager.CreateAsync(admin, "admin123");
                if (!create.Succeeded)
                {
                    TempData["RoleSuccess"] = "Gagal membuat akun admin default.";
                    return RedirectToAction(nameof(Index));
                }
            }

            var users = _userManager.Users.ToList();
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var nonAdminRoles = roles.Where(r => !string.Equals(r, AppRoles.Admin, StringComparison.OrdinalIgnoreCase)).ToList();
                if (nonAdminRoles.Count > 0)
                {
                    await _userManager.RemoveFromRolesAsync(user, nonAdminRoles);
                }
            }

            var allRoles = _roleManager.Roles.ToList();
            foreach (var role in allRoles)
            {
                if (!string.Equals(role.Name, AppRoles.Admin, StringComparison.OrdinalIgnoreCase))
                {
                    await _roleManager.DeleteAsync(role);
                }
            }

            if (!await _roleManager.RoleExistsAsync(AppRoles.Admin))
            {
                await _roleManager.CreateAsync(new IdentityRole(AppRoles.Admin));
            }
            if (!await _userManager.IsInRoleAsync(admin, AppRoles.Admin))
            {
                await _userManager.AddToRoleAsync(admin, AppRoles.Admin);
            }

            // Delete all users except username "admin".
            var allUsersAfterRoleReset = _userManager.Users.ToList();
            foreach (var user in allUsersAfterRoleReset)
            {
                if (string.Equals(user.UserName, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                await _userManager.DeleteAsync(user);
            }

            // Keep legacy table aligned for demo: leave only admin row.
            await _db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.Users WHERE LOWER(LTRIM(RTRIM(ISNULL(Username,'')))) <> 'admin';
END;");

            TempData["RoleSuccess"] = "Reset selesai. Hanya akun admin + role Admin yang tersisa.";
            return RedirectToAction(nameof(Index));
        }
    }
}
