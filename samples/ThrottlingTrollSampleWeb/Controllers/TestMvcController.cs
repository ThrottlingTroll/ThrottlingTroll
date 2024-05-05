using Microsoft.AspNetCore.Mvc;
using ThrottlingTroll;

namespace ThrottlingTrollSampleWeb.Controllers
{
    /// <summary>
    /// Demonstrates how to apply <see cref="ThrottlingTrollAttribute"/>s to an MVC controller.
    /// All methods have a shared limit of 15 requests per 15 seconds.
    /// </summary>
    [ThrottlingTroll(PermitLimit = 15, IntervalInSeconds = 15, ResponseBody = "Controller-level limit exceeded. Retry in 15 seconds.")]
    public class TestMvcController : Controller
    {
        /// <summary>
        /// Shares controller's limit
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Handles POST requests.
        /// Action-level limit - 2 requests per 4 seconds.
        /// </summary>
        [HttpPost]
        [ThrottlingTroll(PermitLimit = 2, IntervalInSeconds = 4, ResponseBody = "TestButtonPressed limit exceeded. Retry in 4 seconds.")]
        public IActionResult TestButtonPressed()
        {
            return RedirectToAction(nameof(Index));
        }
    }
}
