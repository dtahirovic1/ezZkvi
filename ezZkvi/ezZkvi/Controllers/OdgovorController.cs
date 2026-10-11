using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ezZkvi.Controllers
{
    [Authorize(Roles = "Admin,Moderator")]
    public class OdgovorController : Controller
    {
        public IActionResult Index()
        {
            return RedirectToAction("Index", "Content", new { tab = "questions" });
        }

    }
}
