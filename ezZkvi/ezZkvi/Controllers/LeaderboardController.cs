using ezZkvi.Data;
using ezZkvi.Models;
using ezZkvi.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ezZkvi.Controllers
{
    [Authorize(Roles = "Student,Moderator,Admin")]
    public class LeaderboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ezZkvi.Services.ScoreService _score;

        public LeaderboardController(ApplicationDbContext context, ezZkvi.Services.ScoreService score)
        {
            _context = context;
            _score = score;
        }

        public async Task<IActionResult> Index(int? predmetId)
        {
            ViewBag.Predmeti = await _context.Predmet
                .OrderBy(p => p.Naziv)
                .Select(p => new { p.Id, p.Naziv })
                .ToListAsync();

            ViewBag.SelectedPredmetId = predmetId;
            ViewBag.BackController = GetBackController();

            var entries = await BuildLeaderboardAsync(predmetId);

            return View(new LeaderboardViewModel
            {
                Entries = entries
            });
        }

        public async Task<IActionResult> Data(int? predmetId)
        {
            var entries = await BuildLeaderboardAsync(predmetId);
            return Json(entries);
        }

        private string GetBackController()
        {
            if (User.IsInRole("Admin"))
            {
                return "Administrator";
            }

            if (User.IsInRole("Moderator"))
            {
                return "Moderator";
            }

            return "Student";
        }

        private Task<List<LeaderboardEntryViewModel>> BuildLeaderboardAsync(int? predmetId)
            => _score.LeaderboardAsync(User.FindFirstValue(ClaimTypes.NameIdentifier), predmetId);
    }
}
