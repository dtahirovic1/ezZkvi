using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ezZkvi.Data;
using ezZkvi.Models;
using ezZkvi.Services;

namespace ezZkvi.Controllers
{
    [Authorize(Roles = "Admin,Moderator")]
    public class PredmetController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PredmetController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Admin smije svaki predmet; moderator samo onaj koji je sam kreirao
        private bool SmijePredmet(Predmet predmet)
        {
            if (User.IsInRole("Admin")) return true;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return predmet.KreatorId == userId;
        }

        private async Task<bool> SmijeOblastAsync(int oblastId, int predmetId)
        {
            var oblast = await _context.Oblast
                .Include(o => o.Predmet)
                .FirstOrDefaultAsync(o => o.Id == oblastId && o.PredmetId == predmetId);

            return oblast != null && oblast.Predmet != null && SmijePredmet(oblast.Predmet);
        }

        private IActionResult RedirectNaContentPredmete()
        {
            return RedirectToAction("Index", "Content", new { tab = "subjects" });
        }

        private IActionResult RedirectNaContentPitanja()
        {
            return RedirectToAction("Index", "Content", new { tab = "questions" });
        }

        private async Task<bool> PostojiPredmetSaNazivomAsync(string naziv, int? ignorisiId = null)
        {
            var kljuc = ContentValidation.KljucZaPoredjenje(naziv);

            return await _context.Predmet
                .AnyAsync(p =>
                    (!ignorisiId.HasValue || p.Id != ignorisiId.Value) &&
                    p.Naziv.Trim().ToLower() == kljuc);
        }

        private async Task<bool> PostojiPitanjeAsync(int predmetId, int oblastId, string tekst, string[] answers)
        {
            var candidates = await _context.Pitanje.Where(p => p.PredmetId == predmetId && p.OblastId == oblastId && p.TekstPitanja == tekst).Select(p => p.Id).ToListAsync();
            var existing = await _context.Odgovor.Where(o => candidates.Contains(o.PitanjeId)).OrderBy(o => o.Id).ToListAsync();
            return existing.GroupBy(o => o.PitanjeId).Any(g => g.Select(o => o.Tekst).SequenceEqual(answers.Where(a => !string.IsNullOrWhiteSpace(a)), StringComparer.Ordinal));
        }

        // GET: /Predmet/ExportCsv/5  — skine sva pitanja predmeta kao CSV
        [Authorize(Roles = "Admin,Moderator")]
        public async Task<IActionResult> ExportCsv(int id)
        {
            var predmet = await _context.Predmet.FindAsync(id);
            if (predmet == null)
            {
                TempData["Error"] = "Odaberi predmet za preuzimanje CSV-a.";
                return RedirectNaContentPitanja();
            }

            if (!SmijePredmet(predmet))
            {
                TempData["Error"] = "Nemaš pristup ovom predmetu.";
                return RedirectNaContentPitanja();
            }

            var pitanja = await _context.Pitanje
                .Include(p => p.Oblast)
                .Where(p => p.PredmetId == id)
                .OrderBy(p => p.Oblast != null ? p.Oblast.Naziv : "")
                .ThenBy(p => p.Id)
                .ToListAsync();

            var pitanjeIds = pitanja.Select(p => p.Id).ToList();
            var sviOdgovori = await _context.Odgovor
                .Where(o => pitanjeIds.Contains(o.PitanjeId))
                .OrderBy(o => o.Id)
                .ToListAsync();

            var odgovoriPoPitanju = sviOdgovori
                .GroupBy(o => o.PitanjeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var sb = new StringBuilder();
            sb.AppendLine("TekstPitanja,OdgovorA,OdgovorB,OdgovorC,OdgovorD,Tacan,Tezina,Oblast");

            foreach (var p in pitanja)
            {
                odgovoriPoPitanju.TryGetValue(p.Id, out var ods);
                ods ??= new List<Odgovor>();

                var a = ods.ElementAtOrDefault(0);
                var b = ods.ElementAtOrDefault(1);
                var c = ods.ElementAtOrDefault(2);
                var d = ods.ElementAtOrDefault(3);

                var tacan = a?.IsTacan == true ? "A"
                          : b?.IsTacan == true ? "B"
                          : c?.IsTacan == true ? "C"
                          : d?.IsTacan == true ? "D" : "";

                sb.AppendLine(string.Join(",", new[]
                {
                    CsvPolje(p.TekstPitanja),
                    CsvPolje(a?.Tekst), CsvPolje(b?.Tekst), CsvPolje(c?.Tekst), CsvPolje(d?.Tekst),
                    tacan,
                    DifficultyNames.Csv(p.Tezina),
                    CsvPolje(p.Oblast?.Naziv)
                }));
            }

            // BOM + UTF-8 da Excel pravilno prikaže š, č, ć, ž, đ
            var bom = Encoding.UTF8.GetPreamble();
            var sadrzaj = Encoding.UTF8.GetBytes(sb.ToString());
            var bytes = bom.Concat(sadrzaj).ToArray();

            return File(bytes, "text/csv", $"pitanja_{predmet.Naziv}.csv");
        }

        // POST: /Predmet/ImportCsv  — učita pitanja iz CSV-a u bazu
        [HttpPost]
        [Authorize(Roles = "Admin,Moderator")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportCsv(int predmetId, int oblastId, IFormFile file)
        {
            var predmet = await _context.Predmet.FindAsync(predmetId);
            if (predmet == null)
            {
                TempData["Error"] = "Odaberi predmet za uvoz.";
                return RedirectNaContentPitanja();
            }

            if (!SmijePredmet(predmet))
            {
                TempData["Error"] = "Nemaš pristup ovom predmetu.";
                return RedirectNaContentPitanja();
            }

            if (!await SmijeOblastAsync(oblastId, predmetId))
            {
                TempData["Error"] = "Odabrana oblast ne pripada predmetu ili nemaš pristup.";
                return RedirectNaContentPitanja();
            }

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Odaberi CSV fajl.";
                return RedirectNaContentPitanja();
            }

            await using var importTx = await _context.Database.BeginTransactionAsync();
            var dodano = 0;
            var preskoceno = 0;
            var prviRed = true;

            using var reader = new StreamReader(file.OpenReadStream());
            string? linija;

            while ((linija = await ReadCsvRecordAsync(reader)) != null)
            {
                if (prviRed) { prviRed = false; continue; }   // preskoči zaglavlje
                if (string.IsNullOrWhiteSpace(linija)) continue;

                var polja = ParsirajCsvLiniju(linija);
                if (polja.Count < 7) { preskoceno++; continue; }

                var tekst = polja[0];
                var tacan = polja[5].Trim().ToUpper();
                var tezinaStr = polja[6].Trim().ToUpper();

                var kandidati = new[]
                {
                    ("A", polja[1]),
                    ("B", polja[2]),
                    ("C", polja[3]),
                    ("D", polja[4])
                };
                var validni = kandidati.Where(k => !string.IsNullOrWhiteSpace(k.Item2)).ToList();

                // mora imati tekst, bar 2 odgovora i tačan odgovor koji postoji
                if (tekst.Length < 5 || tekst.Length > 1000 || validni.Count < 2 || !validni.Any(k => k.Item1 == tacan))
                {
                    preskoceno++;
                    continue;
                }

                if (validni.Any(k => k.Item2.Length < 1 || k.Item2.Length > 500))
                {
                    preskoceno++;
                    continue;
                }

                if (!DifficultyNames.TryParse(tezinaStr, out var tezina))
                {
                    tezina = Tezina.SREDNJE;
                }

                if (await PostojiPitanjeAsync(predmetId, oblastId, tekst, kandidati.Select(k => k.Item2).ToArray()))
                {
                    preskoceno++;
                    continue;
                }

                var pitanje = new Pitanje
                {
                    TekstPitanja = tekst,
                    PredmetId = predmetId,
                    OblastId = oblastId,
                    Tezina = tezina
                };
                _context.Pitanje.Add(pitanje);
                await _context.SaveChangesAsync();

                var odgovori = validni.Select(k => new Odgovor
                {
                    Tekst = k.Item2,
                    PitanjeId = pitanje.Id,
                    IsTacan = k.Item1 == tacan
                });
                _context.Odgovor.AddRange(odgovori);
                await _context.SaveChangesAsync();

                dodano++;
            }

            await importTx.CommitAsync();
            TempData["Success"] = $"Uvezeno {dodano} pitanja."
                + (preskoceno > 0 ? $" Preskočeno {preskoceno} duplikata ili neispravnih redova." : "");
            return RedirectNaContentPitanja();
        }

        private static async Task<string?> ReadCsvRecordAsync(StreamReader reader)
        {
            var record = new StringBuilder();
            var inQuotes = false;
            while (true)
            {
                var value = reader.Read();
                if (value < 0) return record.Length == 0 ? null : record.ToString();
                var c = (char)value;
                if (c == '"') {
                    record.Append(c);
                    if (inQuotes && reader.Peek() == '"') record.Append((char)reader.Read());
                    else inQuotes = !inQuotes;
                }
                else if (!inQuotes && (c == '\r' || c == '\n')) {
                    if (c == '\r' && reader.Peek() == '\n') reader.Read();
                    return record.ToString();
                }
                else record.Append(c);
                if (record.Length > 1024 * 1024) throw new InvalidDataException("CSV red je prevelik.");
                if (record.Length % 4096 == 0) await Task.Yield();
            }
        }

        // Pretvori vrijednost u sigurno CSV polje (navodnici ako ima zarez/navodnik)
        private static string CsvPolje(string? vrijednost)
        {
            vrijednost ??= "";
            if (vrijednost.Contains(',') || vrijednost.Contains('"') || vrijednost.Contains('\n') || vrijednost.Contains('\r'))
            {
                return "\"" + vrijednost.Replace("\"", "\"\"") + "\"";
            }
            return vrijednost;
        }

        // Razbij jednu CSV liniju na polja (poštuje navodnike)
        private static List<string> ParsirajCsvLiniju(string linija)
        {
            var rezultat = new List<string>();
            var sb = new StringBuilder();
            var uNavodnicima = false;

            for (var i = 0; i < linija.Length; i++)
            {
                var ch = linija[i];

                if (uNavodnicima)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < linija.Length && linija[i + 1] == '"') { sb.Append('"'); i++; }
                        else uNavodnicima = false;
                    }
                    else sb.Append(ch);
                }
                else
                {
                    if (ch == '"') uNavodnicima = true;
                    else if (ch == ',') { rezultat.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(ch);
                }
            }
            rezultat.Add(sb.ToString());
            return rezultat;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFromContent([Bind("Naziv")] Predmet predmet)
        {
            var naziv = ContentValidation.NormalizujUnos(predmet.Naziv);

            if (!ModelState.IsValid || naziv.Length < 2 || naziv.Length > 100)
            {
                TempData["Error"] = "Predmet nije sačuvan. Naziv mora imati između 2 i 100 karaktera.";
                return RedirectNaContentPredmete();
            }

            if (!ContentValidation.NazivImaDozvoljeneZnakove(naziv))
            {
                TempData["Error"] = "Naziv predmeta smije sadržavati samo slova, brojeve i razmake.";
                return RedirectNaContentPredmete();
            }

            if (await PostojiPredmetSaNazivomAsync(naziv))
            {
                TempData["Error"] = "Predmet sa istim nazivom već postoji.";
                return RedirectNaContentPredmete();
            }

            predmet.Naziv = naziv;

            if (!User.IsInRole("Admin"))
            {
                predmet.KreatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            }

            _context.Predmet.Add(predmet);
            await _context.SaveChangesAsync();

            _context.Oblast.Add(new Oblast
            {
                Naziv = "Oblast 1",
                PredmetId = predmet.Id
            });
            await _context.SaveChangesAsync();

            TempData["Success"] = "Predmet je uspješno dodan.";
            return RedirectNaContentPredmete();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFromContent(int id, [Bind("Id,Naziv")] Predmet predmet)
        {
            if (id != predmet.Id)
            {
                return NotFound();
            }

            var postojeci = await _context.Predmet.FindAsync(id);
            if (postojeci == null)
            {
                return NotFound();
            }

            if (!SmijePredmet(postojeci))
            {
                return Forbid();
            }

            var naziv = ContentValidation.NormalizujUnos(predmet.Naziv);

            if (naziv.Length < 2 || naziv.Length > 100)
            {
                TempData["Error"] = "Naziv predmeta mora imati između 2 i 100 karaktera.";
                return RedirectNaContentPredmete();
            }

            if (!ContentValidation.NazivImaDozvoljeneZnakove(naziv))
            {
                TempData["Error"] = "Naziv predmeta smije sadržavati samo slova, brojeve i razmake.";
                return RedirectNaContentPredmete();
            }

            if (await PostojiPredmetSaNazivomAsync(naziv, postojeci.Id))
            {
                TempData["Error"] = "Predmet sa istim nazivom već postoji.";
                return RedirectNaContentPredmete();
            }

            postojeci.Naziv = naziv;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Predmet je ažuriran.";
            return RedirectNaContentPredmete();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFromContent(int id)
        {
            var predmet = await _context.Predmet.FindAsync(id);
            if (predmet == null)
            {
                return NotFound();
            }

            if (!SmijePredmet(predmet))
            {
                return Forbid();
            }

            await ContentDeletionService.ObrisiPredmetSaSadrzajemAsync(_context, id);
            _context.Predmet.Remove(predmet);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Predmet je obrisan zajedno sa povezanim oblastima, pitanjima, kviz sesijama, feedbackom i leaderboard/statistikom za taj predmet.";
            return RedirectNaContentPredmete();
        }

        public IActionResult Index()
        {
            return RedirectNaContentPredmete();
        }

        public IActionResult Details(int? id)
        {
            return Forbid();
        }

        public IActionResult Create()
        {
            return Forbid();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create([Bind("Id,Naziv")] Predmet predmet)
        {
            return Forbid();
        }

        public IActionResult Edit(int? id)
        {
            return Forbid();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, [Bind("Id,Naziv")] Predmet predmet)
        {
            return Forbid();
        }

        public IActionResult Delete(int? id)
        {
            return Forbid();
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            return Forbid();
        }
    }
}
