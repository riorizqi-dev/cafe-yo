using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using cafe_yo.Security;

namespace cafe_yo.Controllers
{
    [Authorize(Policy = "SupervisorOnly")]
    [Route("Supervisor")]
    public class SupervisorController : Controller
    {
        [HttpGet("")]
        public IActionResult Index() => View("~/Views/Supervisor/Index.cshtml");

        [HttpGet("Orders")]
        public IActionResult Orders() => View("~/Views/Supervisor/Orders.cshtml");

        [HttpGet("Alerts")]
        public IActionResult Alerts() => View("~/Views/Supervisor/Alerts.cshtml");

        [HttpGet("Ingredients")]
        public IActionResult Ingredients() => View("~/Views/Supervisor/Ingredients.cshtml");

        [HttpGet("Recipes")]
        public IActionResult Recipes() => View("~/Views/Supervisor/Recipes.cshtml");

        [HttpGet("UsageLogs")]
        public IActionResult UsageLogs() => View("~/Views/Supervisor/UsageLogs.cshtml");

        [HttpGet("Inventory")]
        public IActionResult Inventory() => View("~/Views/Supervisor/Inventory.cshtml");
    }
}
