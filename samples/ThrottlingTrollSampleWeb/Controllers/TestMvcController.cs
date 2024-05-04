using Microsoft.AspNetCore.Mvc;
using ThrottlingTroll;

namespace ThrottlingTrollSampleWeb.Controllers
{
    /// <summary>
    /// Demonstrates how to apply <see cref="ThrottlingTrollAttribute"/>s to an MVC controller
    /// </summary>
    [ThrottlingTroll(PermitLimit = 15, IntervalInSeconds = 15, ResponseBody = "Controller-level limit exceeded. Retry in 15 seconds.")]
    public class TestMvcController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ThrottlingTroll(PermitLimit = 2, IntervalInSeconds = 4, ResponseBody = "TestButtonPressed limit exceeded. Retry in 4 seconds.")]
        public IActionResult TestButtonPressed()
        {
            return RedirectToAction(nameof(Index));
        }
    }
}
