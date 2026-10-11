using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ezZkvi.Data;
using ezZkvi.Models;

namespace ezZkvi.Controllers
{
    public class FeedbackController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FeedbackController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids, bool confirmed)
        {
            ids = (ids ?? Array.Empty<int>()).Distinct().ToArray();
            if (!confirmed || ids.Length == 0 || ids.Length > 10000 || ids.Any(id => id <= 0)) return BadRequest();
            var rows = await _context.Feedback.Where(f => ids.Contains(f.ID)).ToListAsync();
            if (rows.Count != ids.Length) return BadRequest("Odabir više nije važeći. Osvježite stranicu.");
            _context.Feedback.RemoveRange(rows);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Obrisano feedbackova: {rows.Count}.";
            return RedirectToAction(nameof(Index));
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

        private async Task<bool> SmijeFeedbackAsync(Feedback feedback)
        {
            if (User.IsInRole("Admin"))
            {
                return true;
            }

            if (!User.IsInRole("Moderator"))
            {
                return false;
            }

            if (feedback.PredmetId == null)
            {
                return true;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            return await _context.Predmet
                .AnyAsync(p => p.Id == feedback.PredmetId.Value && p.KreatorId == userId);
        }

        [Authorize(Roles = "Admin,Moderator")]
        public async Task<IActionResult> Index()
        {
            var query = _context.Feedback
                .Include(f => f.Predmet)
                .AsQueryable();

            var dozvoljeni = await DozvoljeniPredmetiIdAsync();

            if (dozvoljeni != null)
            {
                query = query.Where(f => f.PredmetId == null || dozvoljeni.Contains(f.PredmetId.Value));
            }

            var sviFeedback = await query
                .OrderByDescending(f => f.DatumSlanja)
                .ToListAsync();

            var autorIds = sviFeedback
                .Where(f => f.KorisnikId != null)
                .Select(f => f.KorisnikId!)
                .Distinct()
                .ToList();

            ViewBag.Autori = await _context.Users
                .Where(u => autorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName);

            ViewBag.Neobradjenih = sviFeedback.Count(f => f.Status == StatusFeedbacka.NA_CEKANJU);
            ViewBag.Obradjenih = sviFeedback.Count(f => f.Status != StatusFeedbacka.NA_CEKANJU);
            ViewBag.Prijedloga = sviFeedback.Count(f => f.TipFeedbacka == TipFeedbacka.PRIJEDLOG_PITANJA);
            ViewBag.PrijavaGresaka = sviFeedback.Count(f => f.TipFeedbacka == TipFeedbacka.PRIJAVA_GRESKE);

            return View(sviFeedback);
        }

        [Authorize(Roles = "Admin,Moderator")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var feedback = await _context.Feedback
                .Include(f => f.Predmet)
                .FirstOrDefaultAsync(m => m.ID == id.Value);

            if (feedback == null)
            {
                return NotFound();
            }

            if (!await SmijeFeedbackAsync(feedback))
            {
                return Forbid();
            }

            return View(feedback);
        }

        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Create()
        {
            ViewBag.Predmeti = await _context.Predmet
                .OrderBy(p => p.Naziv)
                .Select(p => new { p.Id, p.Naziv })
                .ToListAsync();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Create([Bind("TipFeedbacka,Sadrzaj,PredmetId")] Feedback feedback)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Feedback nije poslan. Provjeri da si unio/la tekst.";
                return RedirectToAction(nameof(Create));
            }

            if (feedback.PredmetId.HasValue)
            {
                var predmetPostoji = await _context.Predmet.AnyAsync(p => p.Id == feedback.PredmetId.Value);

                if (!predmetPostoji)
                {
                    TempData["Error"] = "Odabrani predmet nije validan.";
                    return RedirectToAction(nameof(Create));
                }
            }

            feedback.Status = StatusFeedbacka.NA_CEKANJU;
            feedback.DatumSlanja = DateTime.UtcNow;
            feedback.KorisnikId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            _context.Add(feedback);
            await _context.SaveChangesAsync();

            TempData["FeedbackPoslan"] = "1";
            return RedirectToAction(nameof(Create));
        }

        [Authorize(Roles = "Admin,Moderator")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var feedback = await _context.Feedback
                .Include(f => f.Predmet)
                .FirstOrDefaultAsync(f => f.ID == id.Value);

            if (feedback == null)
            {
                return NotFound();
            }

            if (!await SmijeFeedbackAsync(feedback))
            {
                return Forbid();
            }

            return View(feedback);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Moderator")]
        public async Task<IActionResult> Edit(int id, [Bind("ID,Status")] Feedback feedback)
        {
            if (id != feedback.ID)
            {
                return NotFound();
            }

            var postojeci = await _context.Feedback.FindAsync(id);

            if (postojeci == null)
            {
                return NotFound();
            }

            if (!await SmijeFeedbackAsync(postojeci))
            {
                return Forbid();
            }

            postojeci.Status = feedback.Status;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var feedback = await _context.Feedback
                .Include(f => f.Predmet)
                .FirstOrDefaultAsync(m => m.ID == id.Value);

            if (feedback == null)
            {
                return NotFound();
            }

            if (!await SmijeFeedbackAsync(feedback))
            {
                return Forbid();
            }

            return View(feedback);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var feedback = await _context.Feedback.FindAsync(id);

            if (feedback == null)
            {
                return NotFound();
            }

            if (!await SmijeFeedbackAsync(feedback))
            {
                return Forbid();
            }

            _context.Feedback.Remove(feedback);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Moderator")]
        public async Task<IActionResult> PrihvatiFeedback(int id)
        {
            var feedback = await _context.Feedback.FindAsync(id);

            if (feedback == null)
            {
                return NotFound();
            }

            if (!await SmijeFeedbackAsync(feedback))
            {
                return Forbid();
            }

            feedback.Status = StatusFeedbacka.ODOBREN;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Feedback je prihvaćen.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Moderator")]
        public async Task<IActionResult> OdbijFeedback(int id)
        {
            var feedback = await _context.Feedback.FindAsync(id);

            if (feedback == null)
            {
                return NotFound();
            }

            if (!await SmijeFeedbackAsync(feedback))
            {
                return Forbid();
            }

            feedback.Status = StatusFeedbacka.ODBIJEN;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Feedback je odbijen.";
            return RedirectToAction(nameof(Index));
        }
    }
}
