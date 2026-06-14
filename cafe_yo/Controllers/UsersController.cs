using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [Route("Users")]
    public class UsersController : Controller
    {
        [HttpGet("")]
        [HttpGet("Index")]
        public IActionResult Index() => Redirect("/Admin/Users");

        [HttpGet("Create")]
        [HttpPost("Create")]
        [HttpGet("Edit/{id?}")]
        [HttpPost("Edit")]
        [HttpGet("Delete/{id?}")]
        [HttpGet("ToggleStatus/{id?}")]
        [HttpGet("ResetPassword/{id?}")]
        public IActionResult LegacyRedirect() => Redirect("/Admin/Users");
    }
}
