using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using cafe_yo.Models;
using cafe_yo.Security;

namespace restoran_rpl1.Controllers
{
    public class AuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly UserManager<ApplicationUser> _users;
        private readonly RoleManager<IdentityRole> _roles;

        public AuthController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles)
        {
            _signIn = signIn;
            _users = users;
            _roles = roles;
        }

        [HttpGet("/Auth")]
        [AllowAnonymous]
        public IActionResult Index()
        {
            // Halaman opsional (jika user klik tombol Login/Register di navbar)
            return View("Auth");
        }

        [HttpGet("/staff")]
        [AllowAnonymous]
        public IActionResult Staff()
        {
            return View("Staff");
        }

        [HttpGet("/forbidden")]
        [AllowAnonymous]
        public IActionResult Forbidden()
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("Forbidden");
        }

        [HttpGet("/staff/dashboard-unavailable")]
        [Authorize]
        public IActionResult DashboardUnavailable([FromQuery] string? role = null)
        {
            ViewBag.RoleName = string.IsNullOrWhiteSpace(role) ? "unknown" : role.Trim();
            return View("DashboardUnavailable");
        }

        public sealed class AjaxLoginRequest
        {
            public string? Username { get; set; }
            public string? Password { get; set; }
            public string? ReturnUrl { get; set; }
        }

        public sealed class AjaxRegisterRequest
        {
            public string? FullName { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AjaxLogin([FromBody] AjaxLoginRequest req)
        {
            try
            {
                var username = (req.Username ?? string.Empty).Trim();
                var password = req.Password ?? string.Empty;
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    return Json(new { success = false, error = "Username & password wajib." });

                var signInTask = _signIn.PasswordSignInAsync(username, password, isPersistent: false, lockoutOnFailure: false);
                var completedSignIn = await Task.WhenAny(signInTask, Task.Delay(TimeSpan.FromSeconds(12)));
                if (completedSignIn != signInTask)
                {
                    return Json(new { success = false, error = "Login timeout. Cek koneksi database/server lalu coba lagi." });
                }

                var result = await signInTask;
                if (!result.Succeeded)
                    return Json(new { success = false, error = "Username atau password salah." });

                var findUserTask = _users.FindByNameAsync(username);
                var completedFindUser = await Task.WhenAny(findUserTask, Task.Delay(TimeSpan.FromSeconds(8)));
                if (completedFindUser != findUserTask)
                {
                    return Json(new { success = false, error = "Login berhasil, tapi server lambat mengambil data role." });
                }

                var user = await findUserTask;
                var redirectUrl = "/";
                if (!string.IsNullOrWhiteSpace(req.ReturnUrl) && Url.IsLocalUrl(req.ReturnUrl))
                {
                    redirectUrl = req.ReturnUrl;
                }

                if (user != null && redirectUrl == "/")
                {
                    var roles = await _users.GetRolesAsync(user);
                    redirectUrl = ResolveRoleHome(roles);
                }

                return Json(new { success = true, redirectUrl });
            }
            catch (Exception ex)
            {
                _ = ex;
                return Json(new
                {
                    success = false,
                    error = "Login gagal karena error server. Cek koneksi database lalu coba lagi."
                });
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> AjaxRegister([FromBody] AjaxRegisterRequest req)
        {
            var fullName = (req.FullName ?? string.Empty).Trim();
            var username = (req.Username ?? string.Empty).Trim();
            var password = req.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return Json(new { success = false, error = "Full name, username, password wajib." });

            var existing = await _users.FindByNameAsync(username);
            if (existing != null)
                return Json(new { success = false, error = "Username sudah dipakai." });

            var user = new ApplicationUser
            {
                FullName = fullName,
                UserName = username,
                EmailConfirmed = true
            };

            var create = await _users.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                var msg = string.Join(", ", create.Errors.Select(e => e.Description));
                return Json(new { success = false, error = msg });
            }

            if (!await _roles.RoleExistsAsync(AppRoles.Customer))
            {
                await _roles.CreateAsync(new IdentityRole(AppRoles.Customer));
            }
            if (!await _users.IsInRoleAsync(user, AppRoles.Customer))
            {
                await _users.AddToRoleAsync(user, AppRoles.Customer);
            }
            user.Role = AppRoles.Customer.ToLowerInvariant();
            await _users.UpdateAsync(user);

            await _signIn.SignInAsync(user, isPersistent: false);

            return Json(new { success = true, redirectUrl = "/" });
        }

        [HttpPost("/Auth/Logout")]
        public async Task<IActionResult> Logout()
        {
            // Reset table & membership cookies on logout
            Response.Cookies.Delete("nr_tableNumber");
            Response.Cookies.Delete("nr_membershipStatus");

            await _signIn.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        private static string ResolveRoleHome(IEnumerable<string> roles)
        {
            var normalized = roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim().ToLowerInvariant())
                .ToHashSet();

            // Customer/member accounts should always land on customer app.
            if (normalized.Count == 0) return "/";
            if (normalized.Contains("member") || normalized.Contains("customer")) return "/";

            if (normalized.Contains("admin")) return "/admin";
            if (normalized.Contains("owner")) return "/owner";
            if (normalized.Contains("supervisor")) return "/supervisor";
            if (normalized.Contains("kasir")) return "/kasir";
            if (normalized.Contains("koki") || normalized.Contains("dapur")) return "/kitchen";

            var firstRole = normalized.FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstRole)
                ? "/staff/dashboard-unavailable?role=none"
                : $"/staff/dashboard-unavailable?role={Uri.EscapeDataString(firstRole)}";
        }
    }
}
