using ezZkvi.Data;
using ezZkvi.Models;
using ezZkvi.ViewModels;
using ezZkvi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ezZkvi.Controllers
{
    [Authorize(Roles = "Admin,Moderator")]
    public class PitanjeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PitanjeController(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<bool> SmijePredmetAsync(int predmetId)
        {
            if (User.IsInRole("Admin"))
            {
                return true;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            return await _context.Predmet
                .AnyAsync(p => p.Id == predmetId && p.KreatorId == userId);
        }

        private async Task<bool> SmijeOblastAsync(int oblastId, int predmetId)
        {
            var oblast = await _context.Oblast
                .Include(o => o.Predmet)
                .FirstOrDefaultAsync(o => o.Id == oblastId && o.PredmetId == predmetId);

            if (oblast == null)
            {
                return false;
            }

            if (User.IsInRole("Admin"))
            {
                return true;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return oblast.Predmet != null && oblast.Predmet.KreatorId == userId;
        }

        private async Task<bool> SmijePitanjeAsync(int pitanjeId)
        {
            if (User.IsInRole("Admin"))
            {
                return true;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            return await _context.Pitanje
                .Include(p => p.Predmet)
                .AnyAsync(p => p.Id == pitanjeId && p.Predmet != null && p.Predmet.KreatorId == userId);
        }

        private IQueryable<Predmet> DozvoljeniPredmetiQuery()
        {
            var query = _context.Predmet.AsQueryable();

            if (!User.IsInRole("Admin"))
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                query = query.Where(p => p.KreatorId == userId);
            }

            return query.OrderBy(p => p.Naziv);
        }

        private IQueryable<Oblast> DozvoljeneOblastiQuery()
        {
            var query = _context.Oblast.Include(o => o.Predmet).AsQueryable();

            if (!User.IsInRole("Admin"))
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                query = query.Where(o => o.Predmet != null && o.Predmet.KreatorId == userId);
            }

            return query.OrderBy(o => o.Predmet!.Naziv).ThenBy(o => o.Naziv);
        }

        private IActionResult RedirectNaContentPitanja()
        {
            return RedirectToAction("Index", "Content", new { tab = "questions" });
        }

        private async Task<bool> PostojiPitanjeSaTekstomAsync(string tekstPitanja, int? ignorisiId = null)
        {
            var kljuc = ContentValidation.KljucZaPoredjenje(tekstPitanja);

            return await _context.Pitanje
                .AnyAsync(p =>
                    (!ignorisiId.HasValue || p.Id != ignorisiId.Value) &&
                    p.TekstPitanja.Trim().ToLower() == kljuc);
        }

        private async Task<bool> DuplicateContentAsync(PitanjeSaOdgovorimaViewModel model, int? exceptId = null)
        {
            var text = ContentValidation.NormalizujUnos(model.TekstPitanja);
            var ids = await _context.Pitanje.Where(p => p.PredmetId == model.PredmetId && p.OblastId == model.OblastId
                && p.TekstPitanja == text && (!exceptId.HasValue || p.Id != exceptId.Value)).Select(p => p.Id).ToListAsync();
            var answers = await _context.Odgovor.Where(o => ids.Contains(o.PitanjeId)).OrderBy(o => o.Id).ToListAsync();
            var proposed = new[] { model.Odgovor1, model.Odgovor2, model.Odgovor3, model.Odgovor4 }.Select(ContentValidation.NormalizujUnos);
            return answers.GroupBy(o => o.PitanjeId).Any(g => g.Select(o => o.Tekst).SequenceEqual(proposed, StringComparer.Ordinal));
        }

        private async Task NapuniSelectListeAsync(int? predmetId = null, int? oblastId = null)
        {
            ViewData["PredmetId"] = new SelectList(
                await DozvoljeniPredmetiQuery().ToListAsync(),
                "Id",
                "Naziv",
                predmetId
            );

            var oblasti = await DozvoljeneOblastiQuery()
                .Select(o => new
                {
                    o.Id,
                    Naziv = (o.Predmet != null ? o.Predmet.Naziv : "Predmet") + " / " + o.Naziv
                })
                .ToListAsync();

            ViewData["OblastId"] = new SelectList(oblasti, "Id", "Naziv", oblastId);
        }

        public IActionResult Index()
        {
            return RedirectNaContentPitanja();
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var pitanje = await _context.Pitanje
                .Include(p => p.Predmet)
                .Include(p => p.Oblast)
                .FirstOrDefaultAsync(p => p.Id == id.Value);

            if (pitanje == null)
            {
                return NotFound();
            }

            if (!await SmijePitanjeAsync(pitanje.Id))
            {
                return Forbid();
            }

            await NapuniSelectListeAsync(pitanje.PredmetId, pitanje.OblastId);
            return View(pitanje);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,TekstPitanja,Tezina,PredmetId,OblastId")] Pitanje pitanje)
        {
            if (id != pitanje.Id)
            {
                return NotFound();
            }

            var postojeci = await _context.Pitanje.FindAsync(id);

            if (postojeci == null)
            {
                return NotFound();
            }

            if (!await SmijePitanjeAsync(postojeci.Id))
            {
                return Forbid();
            }

            if (!await SmijePredmetAsync(pitanje.PredmetId) || !await SmijeOblastAsync(pitanje.OblastId, pitanje.PredmetId))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                await NapuniSelectListeAsync(pitanje.PredmetId, pitanje.OblastId);
                return View(pitanje);
            }

            var tekstPitanja = ContentValidation.NormalizujUnos(pitanje.TekstPitanja);

            if (tekstPitanja.Length < 5 || tekstPitanja.Length > 1000)
            {
                ModelState.AddModelError(nameof(Pitanje.TekstPitanja), "Tekst pitanja mora imati između 5 i 1000 karaktera.");
                await NapuniSelectListeAsync(pitanje.PredmetId, pitanje.OblastId);
                return View(pitanje);
            }

            if (await PostojiPitanjeSaTekstomAsync(tekstPitanja, postojeci.Id))
            {
                ModelState.AddModelError(nameof(Pitanje.TekstPitanja), "Pitanje sa istim tekstom već postoji.");
                await NapuniSelectListeAsync(pitanje.PredmetId, pitanje.OblastId);
                return View(pitanje);
            }

            postojeci.TekstPitanja = tekstPitanja;
            postojeci.Tezina = pitanje.Tezina;
            postojeci.PredmetId = pitanje.PredmetId;
            postojeci.OblastId = pitanje.OblastId;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Pitanje je ažurirano.";
            return RedirectNaContentPitanja();
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var pitanje = await _context.Pitanje.FindAsync(id);

            if (pitanje == null)
            {
                return NotFound();
            }

            if (!await SmijePitanjeAsync(pitanje.Id))
            {
                return Forbid();
            }

            await ContentDeletionService.ObrisiPitanjeSaSadrzajemAsync(_context, id);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Pitanje je obrisano zajedno sa povezanim odgovorima i stavkama kviz sesija.";
            return RedirectNaContentPitanja();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFromContent(PitanjeSaOdgovorimaViewModel model)
        {
            if (model.Id <= 0)
            {
                return NotFound();
            }

            var postojeci = await _context.Pitanje.FindAsync(model.Id);

            if (postojeci == null)
            {
                return NotFound();
            }

            if (!await SmijePitanjeAsync(postojeci.Id))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Pitanje nije ažurirano. Provjerite unesene podatke.";
                return RedirectNaContentPitanja();
            }

            if (!await SmijePredmetAsync(model.PredmetId) || !await SmijeOblastAsync(model.OblastId, model.PredmetId))
            {
                return Forbid();
            }

            var tekstPitanja = ContentValidation.NormalizujUnos(model.TekstPitanja);

            if (tekstPitanja.Length < 5 || tekstPitanja.Length > 1000)
            {
                TempData["Error"] = "Tekst pitanja mora imati između 5 i 1000 karaktera.";
                return RedirectNaContentPitanja();
            }

            if (await DuplicateContentAsync(model, postojeci.Id))
            {
                TempData["Error"] = "Pitanje s istim tekstom i odgovorima već postoji u ovoj oblasti.";
                return RedirectNaContentPitanja();
            }

            postojeci.TekstPitanja = tekstPitanja;
            postojeci.Tezina = model.Tezina;
            postojeci.PredmetId = model.PredmetId;
            postojeci.OblastId = model.OblastId;

            var tekstoviOdgovora = new[]
            {
                ContentValidation.NormalizujUnos(model.Odgovor1),
                ContentValidation.NormalizujUnos(model.Odgovor2),
                ContentValidation.NormalizujUnos(model.Odgovor3),
                ContentValidation.NormalizujUnos(model.Odgovor4)
            };

            if (tekstoviOdgovora.Any(o => o.Length < 1 || o.Length > 500))
            {
                TempData["Error"] = "Svaki odgovor mora imati između 1 i 500 karaktera.";
                return RedirectNaContentPitanja();
            }

            var odgovori = await _context.Odgovor
                .Where(o => o.PitanjeId == postojeci.Id)
                .OrderBy(o => o.Id)
                .ToListAsync();

            while (odgovori.Count < 4)
            {
                var noviOdgovor = new Odgovor
                {
                    PitanjeId = postojeci.Id,
                    Tekst = string.Empty,
                    IsTacan = false
                };

                _context.Odgovor.Add(noviOdgovor);
                odgovori.Add(noviOdgovor);
            }

            if (odgovori.Count > 4)
            {
                _context.Odgovor.RemoveRange(odgovori.Skip(4));
                odgovori = odgovori.Take(4).ToList();
            }

            for (var i = 0; i < 4; i++)
            {
                odgovori[i].Tekst = tekstoviOdgovora[i];
                odgovori[i].IsTacan = model.TacanOdgovor == i + 1;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Pitanje je ažurirano.";
            return RedirectNaContentPitanja();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFromContent(PitanjeSaOdgovorimaViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Pitanje nije sačuvano. Provjerite unesene podatke.";
                return RedirectNaContentPitanja();
            }

            if (!await SmijePredmetAsync(model.PredmetId) || !await SmijeOblastAsync(model.OblastId, model.PredmetId))
            {
                return Forbid();
            }

            var tekstPitanja = ContentValidation.NormalizujUnos(model.TekstPitanja);

            if (tekstPitanja.Length < 5 || tekstPitanja.Length > 1000)
            {
                TempData["Error"] = "Tekst pitanja mora imati između 5 i 1000 karaktera.";
                return RedirectNaContentPitanja();
            }

            if (await DuplicateContentAsync(model))
            {
                TempData["Error"] = "Pitanje s istim tekstom i odgovorima već postoji u ovoj oblasti.";
                return RedirectNaContentPitanja();
            }

            var tekstoviOdgovora = new[]
            {
                ContentValidation.NormalizujUnos(model.Odgovor1),
                ContentValidation.NormalizujUnos(model.Odgovor2),
                ContentValidation.NormalizujUnos(model.Odgovor3),
                ContentValidation.NormalizujUnos(model.Odgovor4)
            };

            if (tekstoviOdgovora.Any(o => o.Length < 1 || o.Length > 500))
            {
                TempData["Error"] = "Svaki odgovor mora imati između 1 i 500 karaktera.";
                return RedirectNaContentPitanja();
            }

            var pitanje = new Pitanje
            {
                TekstPitanja = tekstPitanja,
                PredmetId = model.PredmetId,
                OblastId = model.OblastId,
                Tezina = model.Tezina
            };

            _context.Pitanje.Add(pitanje);
            await _context.SaveChangesAsync();

            var odgovori = new List<Odgovor>
            {
                new Odgovor { Tekst = tekstoviOdgovora[0], PitanjeId = pitanje.Id, IsTacan = model.TacanOdgovor == 1 },
                new Odgovor { Tekst = tekstoviOdgovora[1], PitanjeId = pitanje.Id, IsTacan = model.TacanOdgovor == 2 },
                new Odgovor { Tekst = tekstoviOdgovora[2], PitanjeId = pitanje.Id, IsTacan = model.TacanOdgovor == 3 },
                new Odgovor { Tekst = tekstoviOdgovora[3], PitanjeId = pitanje.Id, IsTacan = model.TacanOdgovor == 4 }
            };

            _context.Odgovor.AddRange(odgovori);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Pitanje je uspješno dodano.";
            return RedirectNaContentPitanja();
        }
    }
}
