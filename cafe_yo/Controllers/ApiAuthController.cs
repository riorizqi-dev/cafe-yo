using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using cafe_yo.Models;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class ApiAuthController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly UserManager<ApplicationUser> _users;

        public ApiAuthController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users)
        {
            _signIn = signIn;
            _users = users;
        }

        public sealed class LoginRequest
        {
            public string? Username { get; set; }
            public string? Password { get; set; }
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            var username = (req.Username ?? string.Empty).Trim();
            var password = req.Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return BadRequest(new { success = false, error = "Username dan password wajib." });
            }

            var result = await _signIn.PasswordSignInAsync(username, password, false, false);
            if (!result.Succeeded)
            {
                return Unauthorized(new { success = false, error = "Username atau password salah." });
            }

            var user = await _users.FindByNameAsync(username);
            var role = "-";
            if (user != null)
            {
                if (await _users.IsInRoleAsync(user, AppRoles.Admin)) role = AppRoles.Admin;
                else if (await _users.IsInRoleAsync(user, AppRoles.Owner)) role = AppRoles.Owner;
                else if (await _users.IsInRoleAsync(user, AppRoles.Supervisor)) role = AppRoles.Supervisor;
                else if (await _users.IsInRoleAsync(user, AppRoles.Kasir)) role = AppRoles.Kasir;
                else if (await _users.IsInRoleAsync(user, AppRoles.Koki) || await _users.IsInRoleAsync(user, AppRoles.DapurLegacy)) role = AppRoles.Koki;
            }

            return Ok(new { success = true, username, role });
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var username = User?.Identity?.Name ?? string.Empty;
            var user = await _users.FindByNameAsync(username);
            if (user == null)
            {
                return Unauthorized(new { success = false });
            }

            var roles = await _users.GetRolesAsync(user);
            return Ok(new
            {
                success = true,
                user = new
                {
                    id = user.Id,
                    username = user.UserName,
                    fullName = user.FullName,
                    roles
                }
            });
        }
    }
}
