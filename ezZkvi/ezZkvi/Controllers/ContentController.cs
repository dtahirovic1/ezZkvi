using System.Security.Claims;
using ezZkvi.Data;
using ezZkvi.Models;
using ezZkvi.ViewModels;
using ezZkvi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ezZkvi.Controllers
{
    [Authorize(Roles = "Admin,Moderator")]
    [Route("[controller]/[action]")]
    public class ContentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ContentController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string kind, int[] ids, bool confirmed)
        {
            ids = (ids ?? Array.Empty<int>()).Distinct().ToArray();
            if (!confirmed || ids.Length == 0 || ids.Any(id => id <= 0) || ids.Length > 10000)
                return BadRequest("Neispravan odabir ili nedostaje potvrda brisanja.");
            await using var tx = await _context.Database.BeginTransactionAsync();
            switch (kind)
            {
                case "questions":
                    var questions = await _context.Pitanje.Where(p => ids.Contains(p.Id)).ToListAsync();
                    if (questions.Count != ids.Length) return BadRequest("Odabir više nije važeći. Osvježite stranicu.");
                    foreach (var p in questions) await ContentDeletionService.ObrisiPitanjeSaSadrzajemAsync(_context, p.Id);
                    break;
                case "areas":
                    var areas = await _context.Oblast.Where(o => ids.Contains(o.Id)).ToListAsync();
                    if (areas.Count != ids.Length) return BadRequest("Odabir više nije važeći. Osvježite stranicu.");
                    foreach (var o in areas) await ContentDeletionService.ObrisiOblastSaSadrzajemAsync(_context, o.Id);
                    _context.Oblast.RemoveRange(areas);
                    break;
                case "subjects":
                    var subjects = await _context.Predmet.Where(p => ids.Contains(p.Id)).ToListAsync();
                    if (subjects.Count != ids.Length) return BadRequest("Odabir više nije važeći. Osvježite stranicu.");
                    foreach (var p in subjects) await ContentDeletionService.ObrisiPredmetSaSadrzajemAsync(_context, p.Id);
                    _context.Predmet.RemoveRange(subjects);
                    break;
                default: return BadRequest();
            }
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            TempData["Success"] = $"Obrisano stavki: {ids.Length}.";
            return RedirectToAction(nameof(Index), new { tab = kind });
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletionPreview(string kind, int[] ids)
        {
            ids = (ids ?? Array.Empty<int>()).Distinct().ToArray();
            if (ids.Length == 0 || ids.Length > 10000 || ids.Any(id => id <= 0)) return BadRequest();
            var questions = _context.Pitanje.AsQueryable();
            var areas = 0;
            if (kind == "questions") questions = questions.Where(p => ids.Contains(p.Id));
            else if (kind == "areas") questions = questions.Where(p => ids.Contains(p.OblastId));
            else if (kind == "subjects") {
                questions = questions.Where(p => ids.Contains(p.PredmetId));
                areas = await _context.Oblast.CountAsync(o => ids.Contains(o.PredmetId));
            } else return BadRequest();
            var questionIds = await questions.Select(p => p.Id).ToListAsync();
            var sessionIds = await _context.KvizSesijaPitanja.Where(x => questionIds.Contains(x.PitanjeId)).Select(x => x.KvizSesijaId).Distinct().ToListAsync();
            var sessions = kind == "questions" ? 0 : await _context.KvizSesije.CountAsync(s => sessionIds.Contains(s.ID)
                || (kind == "subjects" && s.PredmetId.HasValue && ids.Contains(s.PredmetId.Value))
                || (kind == "areas" && s.OblastId.HasValue && ids.Contains(s.OblastId.Value)));
            var feedback = kind == "subjects" ? await _context.Feedback.CountAsync(f => f.PredmetId.HasValue && ids.Contains(f.PredmetId.Value)) : 0;
            return Json(new { questions = questionIds.Count, areas, sessions, feedback });
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

        [HttpGet("/Content")]
        public IActionResult Root()
        {
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? search, int? predmetId, int? oblastId, Tezina? tezina, string? areaSearch, int? areaPredmetId, string? subjectSearch, string? tab)
        {
            var activeTab = (tab ?? "questions").Trim().ToLowerInvariant();
            if (activeTab != "questions" && activeTab != "areas" && activeTab != "subjects")
            {
                activeTab = "questions";
            }

            ViewData["ActiveTab"] = activeTab;

            var dozvoljeni = await DozvoljeniPredmetiIdAsync();

            var pitanjaQuery = _context.Pitanje
                .Include(p => p.Predmet)
                .Include(p => p.Oblast)
                .AsQueryable();

            if (dozvoljeni != null)
            {
                pitanjaQuery = pitanjaQuery.Where(p => dozvoljeni.Contains(p.PredmetId));
            }

            var predmetiQuery = _context.Predmet.AsQueryable();
            if (dozvoljeni != null)
            {
                predmetiQuery = predmetiQuery.Where(p => dozvoljeni.Contains(p.Id));
            }

            var predmeti = await predmetiQuery.OrderBy(p => p.Naziv).ToListAsync();
            var predmetiIds = predmeti.Select(p => p.Id).ToList();

            var oblasti = await _context.Oblast
                .Include(o => o.Predmet)
                .Where(o => predmetiIds.Contains(o.PredmetId))
                .OrderBy(o => o.Predmet!.Naziv)
                .ThenBy(o => o.Naziv)
                .ToListAsync();

            ViewBag.OblastiListaZaSelect = oblasti.Select(o => new
            {
                o.Id,
                o.Naziv,
                o.PredmetId,
                PredmetNaziv = o.Predmet != null ? o.Predmet.Naziv : "Predmet"
            }).ToList();

            var brojPitanjaPoPredmetu = await _context.Pitanje
                .Where(p => predmetiIds.Contains(p.PredmetId))
                .GroupBy(p => p.PredmetId)
                .Select(g => new { PredmetId = g.Key, Broj = g.Count() })
                .ToDictionaryAsync(x => x.PredmetId, x => x.Broj);

            var predmetiZaPrikaz = predmeti.AsEnumerable();

            ViewBag.PredmetiLista = predmetiZaPrikaz
                .Select(p => new PredmetAktivnostItem
                {
                    Id = p.Id,
                    Naziv = p.Naziv,
                    BrojPitanja = brojPitanjaPoPredmetu.TryGetValue(p.Id, out var b) ? b : 0
                    , BrojOblasti = oblasti.Count(o => o.PredmetId == p.Id)
                })
                .ToList();

            var brojPitanjaPoOblasti = await _context.Pitanje
                .Where(p => predmetiIds.Contains(p.PredmetId))
                .GroupBy(p => p.OblastId)
                .Select(g => new { OblastId = g.Key, Broj = g.Count() })
                .ToDictionaryAsync(x => x.OblastId, x => x.Broj);

            var oblastiZaPrikaz = oblasti.AsEnumerable();

            ViewBag.OblastiLista = oblastiZaPrikaz
                .Select(o => new OblastAktivnostItem
                {
                    Id = o.Id,
                    Naziv = o.Naziv,
                    PredmetId = o.PredmetId,
                    PredmetNaziv = o.Predmet != null ? o.Predmet.Naziv : "Predmet",
                    BrojPitanja = brojPitanjaPoOblasti.TryGetValue(o.Id, out var b) ? b : 0
                })
                .ToList();

            var pitanja = await pitanjaQuery
                .OrderByDescending(p => p.Id)
                .ToListAsync();

            var pitanjaIds = pitanja.Select(p => p.Id).ToList();
            var odgovori = await _context.Odgovor
                .Where(o => pitanjaIds.Contains(o.PitanjeId))
                .OrderBy(o => o.Id)
                .ToListAsync();

            ViewBag.OdgovoriPoPitanju = odgovori
                .GroupBy(o => o.PitanjeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            return View(pitanja);
        }
    }
}
