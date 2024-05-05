using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ThrottlingTroll;

namespace ThrottlingTrollSampleWeb.Pages
{
    /// <summary>
    /// Demonstrates how to apply <see cref="ThrottlingTrollAttribute"/>s to Razor Pages.
    /// All methods have a shared limit of 10 requests per 10 seconds.
    /// </summary>
    [ThrottlingTroll(PermitLimit = 15, IntervalInSeconds = 15, ResponseBody = "Page-level limit exceeded. Retry in 10 seconds.")]
    public class TestRazorPageModel : PageModel
    {
        /// <summary>
        /// Shares page's limit
        /// </summary>
        public IActionResult OnGet()
        {
            return Page();
        }

        /// <summary>
        /// Handles POST requests.
        /// Handler-level limit - 3 requests per 6 seconds.
        /// </summary>
        [ThrottlingTroll(PermitLimit = 3, IntervalInSeconds = 6, ResponseBody = "POST limit exceeded. Retry in 6 seconds.")]
        public IActionResult OnPost()
        {
            return Page();
        }

        /// <summary>
        /// Handles POST requests to /TestRazorPage?handler=MyCustomHandler.
        /// Handler-level limit - 2 requests per 5 seconds.
        /// NOTE that this handler is still subject to the above limit as well (because the URL path is the same)
        /// </summary>
        [ThrottlingTroll(PermitLimit = 2, IntervalInSeconds = 5, ResponseBody = "POST MyCustomHandler limit exceeded. Retry in 5 seconds.")]
        public async Task<IActionResult> OnPostMyCustomHandlerAsync()
        {
            await Task.Yield();
            return Page();
        }
    }
}
