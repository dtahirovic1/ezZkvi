using System.Security.Claims;
using ezZkvi.Data;
using ezZkvi.Models;
using ezZkvi.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ezZkvi.Controllers
{
    [Authorize(Roles = "Admin,Moderator")]
    public class ModeratorController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ModeratorController(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<List<int>?> DozvoljeniPredmetiIdAsync()
        {
            if (User.IsInRole("Admin"))
            {
                return null;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return await _context.Predmet
                .Where(p => p.KreatorId == userId)
                .Select(p => p.Id)
                .ToListAsync();
        }

        [Authorize(Roles = "Moderator")]
        public async Task<IActionResult> Dashboard()
        {
            var dozvoljeni = await DozvoljeniPredmetiIdAsync();

            var predmetiQuery = _context.Predmet.AsQueryable();
            var pitanjaQuery = _context.Pitanje.AsQueryable();
            var feedbackQuery = _context.Feedback.AsQueryable();

            if (dozvoljeni != null)
            {
                predmetiQuery = predmetiQuery.Where(p => dozvoljeni.Contains(p.Id));
                pitanjaQuery = pitanjaQuery.Where(p => dozvoljeni.Contains(p.PredmetId));
                feedbackQuery = feedbackQuery.Where(f => f.PredmetId == null || dozvoljeni.Contains(f.PredmetId.Value));
            }

            ViewBag.BrojPitanja = await pitanjaQuery.CountAsync();
            ViewBag.BrojPredmeta = await predmetiQuery.CountAsync();
            ViewBag.NeobradeniFeedback = await feedbackQuery.CountAsync(f => f.Status == StatusFeedbacka.NA_CEKANJU);
            ViewBag.ObradeniFeedback = await feedbackQuery.CountAsync(f => f.Status != StatusFeedbacka.NA_CEKANJU);

            ViewBag.NajnovijiFeedback = (await feedbackQuery
                .Where(f => f.Status == StatusFeedbacka.NA_CEKANJU)
                .OrderByDescending(f => f.DatumSlanja)
                .Take(5)
                .ToListAsync())
                .Select(f => new FeedbackItem
                {
                    Sadrzaj = f.Sadrzaj,
                    Tip = f.TipFeedbacka,
                    Datum = f.DatumSlanja
                })
                .ToList();

            var pitanjaPoPredmetu = await pitanjaQuery
                .GroupBy(p => p.PredmetId)
                .Select(g => new { PredmetId = g.Key, Broj = g.Count() })
                .ToDictionaryAsync(x => x.PredmetId, x => x.Broj);

            var predmeti = await predmetiQuery.ToListAsync();

            ViewBag.PitanjaPoPredmetu = predmeti
                .Select(p => new PredmetAktivnostItem
                {
                    Naziv = p.Naziv,
                    BrojPitanja = pitanjaPoPredmetu.TryGetValue(p.Id, out var b) ? b : 0
                })
                .OrderByDescending(p => p.BrojPitanja)
                .ToList();

            ViewBag.ObradjeniFeedback = (await feedbackQuery
                .Where(f => f.Status != StatusFeedbacka.NA_CEKANJU)
                .OrderByDescending(f => f.DatumSlanja)
                .Take(5)
                .ToListAsync())
                .Select(f => new FeedbackItem
                {
                    Sadrzaj = f.Sadrzaj,
                    Tip = f.TipFeedbacka,
                    Datum = f.DatumSlanja
                })
                .ToList();

            return View();
        }

        public IActionResult Content(string? search, int? predmetId, int? oblastId, Tezina? tezina, string? areaSearch, int? areaPredmetId, string? subjectSearch, string? tab)
        {
            var activeTab = string.IsNullOrWhiteSpace(tab)
                ? (!string.IsNullOrWhiteSpace(subjectSearch) ? "subjects" : (!string.IsNullOrWhiteSpace(areaSearch) || areaPredmetId.HasValue ? "areas" : "questions"))
                : tab;

            return RedirectToAction("Index", "Content", new { search, predmetId, oblastId, tezina, areaSearch, areaPredmetId, subjectSearch, tab = activeTab });
        }

        public IActionResult Feedback()
        {
            return RedirectToAction("Index", "Feedback");
        }

        public IActionResult Index()
        {
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "Content", new { tab = "questions" });
            }

            return RedirectToAction(nameof(Dashboard));
        }

    }
}
