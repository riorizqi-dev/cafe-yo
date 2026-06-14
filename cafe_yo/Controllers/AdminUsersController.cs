using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using cafe_yo.Models;
using cafe_yo.Security;
using System;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [Route("Admin/Users")]
    public class AdminUsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminUsersController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var users = _userManager.Users.ToList();
            var viewModels = new List<AdminUserListItem>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                viewModels.Add(new AdminUserListItem
                {
                    Id = user.Id,
                    UserName = user.UserName,
                    FullName = user.FullName,
                    Roles = roles.Count > 0 ? string.Join(", ", roles) : "-",
                    IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow
                });
            }

            return View("~/Views/Admin/Users/Index.cshtml", viewModels);
        }

        [HttpGet("Create")]
        public IActionResult Create()
        {
            var model = new AdminCreateUserVM
            {
                AvailableRoles = _roleManager.Roles
                    .Select(r => r.Name ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .OrderBy(x => x)
                    .ToList()
            };
            return View("~/Views/Admin/Users/Create.cshtml", model);
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AdminCreateUserVM model)
        {
            model.AvailableRoles = _roleManager.Roles
                .Select(r => r.Name ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x)
                .ToList();

            var username = (model.UserName ?? string.Empty).Trim();
            var fullName = (model.FullName ?? string.Empty).Trim();
            var password = model.Password ?? string.Empty;
            var roleName = (model.RoleName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(username))
                ModelState.AddModelError(nameof(AdminCreateUserVM.UserName), "Username wajib diisi.");
            if (string.IsNullOrWhiteSpace(fullName))
                ModelState.AddModelError(nameof(AdminCreateUserVM.FullName), "Nama lengkap wajib diisi.");
            if (string.IsNullOrWhiteSpace(password))
                ModelState.AddModelError(nameof(AdminCreateUserVM.Password), "Password wajib diisi.");
            if (string.IsNullOrWhiteSpace(roleName))
                ModelState.AddModelError(nameof(AdminCreateUserVM.RoleName), "Role wajib dipilih.");

            if (!ModelState.IsValid)
                return View("~/Views/Admin/Users/Create.cshtml", model);

            var exists = await _userManager.FindByNameAsync(username);
            if (exists != null)
            {
                ModelState.AddModelError(nameof(AdminCreateUserVM.UserName), "Username sudah dipakai.");
                return View("~/Views/Admin/Users/Create.cshtml", model);
            }

            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                ModelState.AddModelError(nameof(AdminCreateUserVM.RoleName), "Role tidak ditemukan. Buat role dulu di halaman Roles.");
                return View("~/Views/Admin/Users/Create.cshtml", model);
            }

            var user = new ApplicationUser
            {
                UserName = username,
                FullName = fullName,
                EmailConfirmed = true,
                Role = roleName.ToLowerInvariant()
            };

            var create = await _userManager.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                ModelState.AddModelError(string.Empty, string.Join(", ", create.Errors.Select(e => e.Description)));
                return View("~/Views/Admin/Users/Create.cshtml", model);
            }

            await _userManager.AddToRoleAsync(user, roleName);
            TempData["UserSuccess"] = $"Akun staff {username} berhasil dibuat dengan role {roleName}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet("{id}/Roles")]
        public async Task<IActionResult> EditRoles(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var allRoles = _roleManager.Roles.Select(r => r.Name).Where(r => r != null).ToList();
            var userRoles = await _userManager.GetRolesAsync(user);

            var model = new AdminEditUserRolesVM
            {
                UserId = user.Id,
                UserName = user.UserName,
                FullName = user.FullName
            };

            foreach (var roleName in allRoles)
            {
                model.Roles.Add(new RoleCheckbox
                {
                    Name = roleName!,
                    IsSelected = userRoles.Contains(roleName!)
                });
            }

            return View("~/Views/Admin/Users/EditRoles.cshtml", model);
        }

        [HttpPost("{id}/Roles")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRoles(string id, AdminEditUserRolesVM model)
        {
            if (id != model.UserId)
            {
                return BadRequest();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            var selectedRoles = model.Roles.Where(r => r.IsSelected).Select(r => r.Name).ToList();

            var rolesToAdd = selectedRoles.Except(userRoles).ToList();
            var rolesToRemove = userRoles.Except(selectedRoles).ToList();

            if (rolesToAdd.Count > 0)
            {
                var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
                if (!addResult.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, string.Join(", ", addResult.Errors.Select(e => e.Description)));
                }
            }

            if (rolesToRemove.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, string.Join(", ", removeResult.Errors.Select(e => e.Description)));
                }
            }

            if (!ModelState.IsValid)
            {
                return View("~/Views/Admin/Users/EditRoles.cshtml", model);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("{id}/Toggle")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var isLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
            user.LockoutEnabled = true;
            user.LockoutEnd = isLocked ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddYears(100);
            await _userManager.UpdateAsync(user);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("{id}/Reset")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var defaultPassword = BuildDefaultPassword(user.UserName);
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, defaultPassword);
            if (!result.Succeeded)
            {
                TempData["UserError"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
            else
            {
                TempData["UserSuccess"] = $"Password {user.UserName} direset ke {defaultPassword}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("{id}/Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (string.Equals(user.UserName, User?.Identity?.Name, StringComparison.OrdinalIgnoreCase))
            {
                TempData["UserError"] = "Tidak bisa menghapus akun yang sedang dipakai login.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                TempData["UserError"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
            else
            {
                TempData["UserSuccess"] = $"User {user.UserName} berhasil dihapus.";
            }

            return RedirectToAction(nameof(Index));
        }

        private static string BuildDefaultPassword(string? username)
        {
            var key = (username ?? string.Empty).Trim().ToLowerInvariant();
            return key switch
            {
                "admin" => "admin123",
                "owner" => "owner123",
                "kasir" => "kasir123",
                "koki" => "koki123",
                _ => string.IsNullOrWhiteSpace(key) ? "staff123" : $"{key}123"
            };
        }
    }
}
