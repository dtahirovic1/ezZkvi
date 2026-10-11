using ezZkvi.Data;
using ezZkvi.Services;
using ezZkvi.Models;
using ezZkvi.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ezZkvi.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ScoreService _score;

        private const int SistemskiBrojPitanja = 18;
        private const int VremenskoOgranicenjeMinuta = 15;
        private const decimal PoeniZaTacanOdgovor = 0.15m;
        private const decimal PoeniZaNetacanOdgovor = -0.05m;
        private const double OznakaPreskocenogPitanja = -1;

        public StudentController(ApplicationDbContext context, ScoreService score)
        {
            _context = context;
            _score = score;
        }

        private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> LockSimulationAsync()
        {
            var tx = await _context.Database.BeginTransactionAsync();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({userId}))");
            return tx;
        }

        private static bool JeZavrsenaSesija(StatusSesije status)
        {
            return status == StatusSesije.ZAVRSEN || status == StatusSesije.ISTEKAO;
        }

        private static bool JePitanjeZakljucano(KvizSesijaPitanje stavka)
        {
            return stavka.OdgovorId.HasValue || stavka.Tacno == OznakaPreskocenogPitanja;
        }

        private async Task<List<SelectListItem>> GetPredmetiZaSimulacijuAsync(int? selectedPredmetId = null)
        {
            return await _context.Predmet
                .OrderBy(p => p.Naziv)
                .Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = p.Naziv,
                    Selected = selectedPredmetId.HasValue && p.Id == selectedPredmetId.Value
                })
                .ToListAsync();
        }

        private async Task<List<OblastSelectItemViewModel>> GetOblastiZaSimulacijuAsync(int? selectedOblastId = null)
        {
            return await _context.Oblast
                .Include(o => o.Predmet)
                .OrderBy(o => o.Predmet!.Naziv)
                .ThenBy(o => o.Naziv)
                .Select(o => new OblastSelectItemViewModel
                {
                    Id = o.Id,
                    Naziv = o.Naziv,
                    PredmetId = o.PredmetId,
                    PredmetNaziv = o.Predmet != null ? o.Predmet.Naziv : "Predmet",
                    Selected = selectedOblastId.HasValue && o.Id == selectedOblastId.Value
                })
                .ToListAsync();
        }

        private async Task<SimulacijaKvizaViewModel> BuildSimulationAsync(int predmetId, int oblastId)
        {
            var predmet = await _context.Predmet.FindAsync(predmetId);
            var oblast = await _context.Oblast.FirstOrDefaultAsync(o => o.Id == oblastId && o.PredmetId == predmetId);

            var model = new SimulacijaKvizaViewModel
            {
                PredmetId = predmetId,
                PredmetNaziv = predmet?.Naziv ?? "Odabrani predmet",
                OblastId = oblastId,
                OblastNaziv = oblast?.Naziv,
                BrojPitanja = SistemskiBrojPitanja,
                VremenskoOgranicenjeMinuta = VremenskoOgranicenjeMinuta,
                StartedAtUtcTicks = DateTime.UtcNow.Ticks,
                Predmeti = await GetPredmetiZaSimulacijuAsync(predmetId),
                Oblasti = await GetOblastiZaSimulacijuAsync(oblastId)
            };

            if (predmet == null)
            {
                model.ErrorMessage = "Odabrani predmet ne postoji.";
                return model;
            }

            if (oblast == null)
            {
                model.ErrorMessage = "Odabrana oblast ne pripada predmetu.";
                return model;
            }

            var svaPitanja = await _context.Pitanje
                .Where(p => p.PredmetId == predmetId && p.OblastId == oblastId)
                .ToListAsync();

            var pitanjeIds = svaPitanja.Select(p => p.Id).ToList();

            var sviOdgovori = await _context.Odgovor
                .Where(o => pitanjeIds.Contains(o.PitanjeId))
                .ToListAsync();

            var odgovoriPoPitanju = sviOdgovori
                .GroupBy(o => o.PitanjeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var validnaPitanja = svaPitanja
                .Where(p => odgovoriPoPitanju.ContainsKey(p.Id)
                            && odgovoriPoPitanju[p.Id].Count == 4
                            && odgovoriPoPitanju[p.Id].Count(o => o.IsTacan) == 1)
                .ToList();

            var odabranaPitanja = SelectQuestionsByDifficulty(validnaPitanja);

            if (odabranaPitanja.Count < SistemskiBrojPitanja)
            {
                model.ErrorMessage = "Za simulaciju je potrebno najmanje 18 validnih ABCD pitanja u odabranoj oblasti. Svako pitanje mora imati tačno 4 ponuđena odgovora i tačno jedan tačan odgovor.";
                return model;
            }

            model.BrojPitanja = odabranaPitanja.Count;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var now = DateTime.UtcNow;

            var sesija = new KvizSesija
            {
                TraziBrojPitanja = odabranaPitanja.Count,
                VremenskoOgranicenje = VremenskoOgranicenjeMinuta,
                Status = StatusSesije.U_TOKU,
                StudentId = userId,
                PredmetId = predmetId,
                OblastId = oblastId,
                BrojTacnih = 0,
                Procenat = 0,
                DatumPocetka = now,
                DatumZavrsetka = now
            };

            _context.KvizSesije.Add(sesija);
            await _context.SaveChangesAsync();

            _context.KvizSesijaPitanja.AddRange(
                odabranaPitanja.Select((p, index) => new KvizSesijaPitanje
                {
                    KvizSesijaId = sesija.ID,
                    PitanjeId = p.Id,
                    RedniBroj = index + 1,
                    BrojBodova = 0,
                    Tacno = 0
                })
            );

            await _context.SaveChangesAsync();

            model.KvizSesijaId = sesija.ID;
            model.PreostaloSekundi = VremenskoOgranicenjeMinuta * 60;
            model.DeadlineUnixMilliseconds = new DateTimeOffset(DateTime.SpecifyKind(now.AddMinutes(VremenskoOgranicenjeMinuta), DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            model.PocetniIndex = 0;

            model.Questions = odabranaPitanja
                .Select(p => new SimulacijaPitanjeViewModel
                {
                    Id = p.Id,
                    TekstPitanja = p.TekstPitanja,
                    Tezina = p.Tezina,
                    Odgovori = Shuffle(odgovoriPoPitanju[p.Id])
                        .Select(o => new SimulacijaOdgovorViewModel
                        {
                            Id = o.Id,
                            Tekst = o.Tekst
                        })
                        .ToList()
                })
                .ToList();

            return model;
        }

        private static List<Pitanje> SelectQuestionsByDifficulty(List<Pitanje> svaPitanja)
        {
            var quotas = new List<(Tezina Tezina, int Broj)>
            {
                (Tezina.TESKO, 4),
                (Tezina.SREDNJE, 7),
                (Tezina.LAKO, 7)
            };

            var selected = new List<Pitanje>();
            var selectedIds = new HashSet<int>();

            foreach (var quota in quotas)
            {
                var pitanjaZaTezinu = Shuffle(svaPitanja.Where(p => p.Tezina == quota.Tezina))
                    .Take(quota.Broj)
                    .ToList();

                foreach (var pitanje in pitanjaZaTezinu)
                {
                    if (selectedIds.Add(pitanje.Id))
                    {
                        selected.Add(pitanje);
                    }
                }
            }

            if (selected.Count < SistemskiBrojPitanja)
            {
                var preostalaPitanja = Shuffle(svaPitanja.Where(p => !selectedIds.Contains(p.Id)))
                    .Take(SistemskiBrojPitanja - selected.Count)
                    .ToList();

                foreach (var pitanje in preostalaPitanja)
                {
                    if (selectedIds.Add(pitanje.Id))
                    {
                        selected.Add(pitanje);
                    }
                }
            }

            return Shuffle(selected).Take(SistemskiBrojPitanja).ToList();
        }

        private static List<T> Shuffle<T>(IEnumerable<T> source)
        {
            return source
                .OrderBy(_ => Random.Shared.Next())
                .ToList();
        }

        private static int CalculateElapsedSeconds(long startedAtUtcTicks, int totalSeconds)
        {
            if (startedAtUtcTicks <= 0)
            {
                return 0;
            }

            var startedAt = new DateTime(startedAtUtcTicks, DateTimeKind.Utc);
            var elapsed = (int)Math.Round((DateTime.UtcNow - startedAt).TotalSeconds);

            if (elapsed < 0)
            {
                return 0;
            }

            if (totalSeconds > 0 && elapsed > totalSeconds)
            {
                return totalSeconds;
            }

            return elapsed;
        }

        private async Task<SimulacijaKvizaViewModel> BuildActiveSimulationModelAsync(int kvizSesijaId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var sesija = await _context.KvizSesije
                .Include(s => s.Predmet)
                .Include(s => s.Oblast)
                .FirstOrDefaultAsync(s =>
                    s.ID == kvizSesijaId &&
                    s.StudentId == userId &&
                    s.Status == StatusSesije.U_TOKU);

            if (sesija == null)
            {
                return new SimulacijaKvizaViewModel
                {
                    ErrorMessage = "Aktivna simulacija nije pronađena.",
                    Predmeti = await GetPredmetiZaSimulacijuAsync(),
                    Oblasti = await GetOblastiZaSimulacijuAsync()
                };
            }

            var deadline = sesija.DatumPocetka.AddMinutes(sesija.VremenskoOgranicenje);

            if (DateTime.UtcNow >= deadline)
            {
                var rezultat = await FinishSimulationAndBuildResultAsync(sesija.ID);

                return new SimulacijaKvizaViewModel
                {
                    KvizSesijaId = sesija.ID,
                    PredmetId = sesija.PredmetId,
                    PredmetNaziv = sesija.Predmet?.Naziv ?? "Odabrani predmet",
                    OblastId = sesija.OblastId,
                    OblastNaziv = sesija.Oblast?.Naziv,
                    BrojPitanja = rezultat?.UkupnoPitanja ?? 0,
                    VremenskoOgranicenjeMinuta = sesija.VremenskoOgranicenje,
                    Result = rezultat,
                    Predmeti = await GetPredmetiZaSimulacijuAsync(sesija.PredmetId),
                    Oblasti = await GetOblastiZaSimulacijuAsync(sesija.OblastId)
                };
            }

            var stavke = await _context.KvizSesijaPitanja
                .Include(x => x.Pitanje)
                .Where(x => x.KvizSesijaId == sesija.ID)
                .OrderBy(x => x.RedniBroj)
                .ToListAsync();

            var pitanjeIds = stavke.Select(x => x.PitanjeId).ToList();

            var odgovori = await _context.Odgovor
                .Where(o => pitanjeIds.Contains(o.PitanjeId))
                .ToListAsync();

            var odgovoriPoPitanju = odgovori
                .GroupBy(o => o.PitanjeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var pocetniIndex = stavke.FindIndex(x => !JePitanjeZakljucano(x));

            if (pocetniIndex < 0)
            {
                pocetniIndex = stavke.Count - 1;
            }

            return new SimulacijaKvizaViewModel
            {
                DeadlineUnixMilliseconds = new DateTimeOffset(DateTime.SpecifyKind(deadline, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                KvizSesijaId = sesija.ID,
                PredmetId = sesija.PredmetId,
                PredmetNaziv = sesija.Predmet?.Naziv ?? "Odabrani predmet",
                OblastId = sesija.OblastId,
                OblastNaziv = sesija.Oblast?.Naziv,
                BrojPitanja = stavke.Count,
                VremenskoOgranicenjeMinuta = sesija.VremenskoOgranicenje,
                PreostaloSekundi = (int)(deadline - DateTime.UtcNow).TotalSeconds,
                PocetniIndex = pocetniIndex,
                Predmeti = await GetPredmetiZaSimulacijuAsync(sesija.PredmetId),
                Oblasti = await GetOblastiZaSimulacijuAsync(sesija.OblastId),

                Questions = stavke.Select(x => new SimulacijaPitanjeViewModel
                {
                    Id = x.PitanjeId,
                    TekstPitanja = x.Pitanje?.TekstPitanja ?? "Nepoznato pitanje",
                    Tezina = x.Pitanje?.Tezina ?? Tezina.LAKO,
                    OdabraniOdgovorId = x.OdgovorId,
                    JeOdgovoreno = x.OdgovorId.HasValue,
                    JePreskoceno = x.Tacno == OznakaPreskocenogPitanja,
                    JeZakljucano = JePitanjeZakljucano(x),
                    Odgovori = odgovoriPoPitanju.TryGetValue(x.PitanjeId, out var ods)
                        ? ods.Select(o => new SimulacijaOdgovorViewModel { Id = o.Id, Tekst = o.Tekst }).ToList()
                        : new List<SimulacijaOdgovorViewModel>()
                }).ToList()
            };
        }

        private async Task AzurirajStudentStatistikuAsync(KvizSesija sesija, int ukupnoPitanja, int tacno)
        {
            if (string.IsNullOrWhiteSpace(sesija.StudentId) || !sesija.PredmetId.HasValue)
            {
                return;
            }

            var statistika = await _context.StudentStatistike
                .FirstOrDefaultAsync(s =>
                    s.KorisnikId == sesija.StudentId &&
                    s.PredmetId == sesija.PredmetId.Value);

            if (statistika == null)
            {
                statistika = new StudentStatistika
                {
                    KorisnikId = sesija.StudentId,
                    PredmetId = sesija.PredmetId.Value
                };

                _context.StudentStatistike.Add(statistika);
            }

            statistika.BrojKvizova += 1;
            statistika.UkupnoPitanja += ukupnoPitanja;
            statistika.TacniOdgovori += tacno;
        }

        private async Task<SimulacijaRezultatViewModel?> FinishSimulationAndBuildResultAsync(int kvizSesijaId)
        {
            if (_context.Database.CurrentTransaction == null)
            {
                await using var tx = await LockSimulationAsync();
                var result = await FinishSimulationAndBuildResultAsync(kvizSesijaId);
                await tx.CommitAsync();
                return result;
            }
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var sesija = await _context.KvizSesije
                .FirstOrDefaultAsync(s => s.ID == kvizSesijaId && s.StudentId == userId);

            if (sesija == null)
            {
                return null;
            }

            var vecObradjena = JeZavrsenaSesija(sesija.Status);

            // A GET may have loaded this entity before waiting for another request's lock.
            await _context.Entry(sesija).ReloadAsync();
            vecObradjena = JeZavrsenaSesija(sesija.Status);

            var deadline = sesija.DatumPocetka.AddMinutes(sesija.VremenskoOgranicenje);
            var vrijemeIsteklo = DateTime.UtcNow >= deadline;

            var stavke = await _context.KvizSesijaPitanja
                .Include(x => x.Pitanje)
                .Include(x => x.Odgovor)
                .Where(x => x.KvizSesijaId == kvizSesijaId)
                .OrderBy(x => x.RedniBroj)
                .ToListAsync();

            if (stavke.Count == 0)
            {
                if (!vecObradjena) return null;
                return new SimulacijaRezultatViewModel {
                    UkupnoPitanja = sesija.TraziBrojPitanja, TacnihOdgovora = sesija.BrojTacnih,
                    NetacnihOdgovora = sesija.BrojNetacnih, Neodgovorenih = sesija.BrojPreskocenih,
                    Procenat = sesija.Procenat, OsvojeniPoeni = sesija.OsvojeniPoeni,
                    MaksimalniPoeni = sesija.MaksimalniPoeni,
                    PrethodniScore = sesija.PrethodniScore, NoviScore = sesija.NoviScore,
                    PrethodniScorePredmeta = sesija.PrethodniScorePredmeta, NoviScorePredmeta = sesija.NoviScorePredmeta,
                    UtrosenoSekundi = (int)Math.Clamp((sesija.DatumZavrsetka - sesija.DatumPocetka).TotalSeconds, 0, sesija.VremenskoOgranicenje * 60)
                };
            }

            var pitanjeIds = stavke.Select(x => x.PitanjeId).ToList();

            var sviOdgovori = await _context.Odgovor
                .Where(o => pitanjeIds.Contains(o.PitanjeId))
                .ToListAsync();

            var odgovoriPoPitanju = sviOdgovori
                .GroupBy(o => o.PitanjeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var pregled = new List<SimulacijaPregledOdgovoraViewModel>();

            foreach (var stavka in stavke)
            {
                odgovoriPoPitanju.TryGetValue(stavka.PitanjeId, out var ponudjeniOdgovori);
                ponudjeniOdgovori ??= new List<Odgovor>();

                var tacanOdgovor = ponudjeniOdgovori.FirstOrDefault(o => o.IsTacan);

                pregled.Add(new SimulacijaPregledOdgovoraViewModel
                {
                    TekstPitanja = stavka.Pitanje?.TekstPitanja ?? "Nepoznato pitanje",
                    KorisnickiOdgovor = stavka.Odgovor?.Tekst,
                    TacanOdgovor = tacanOdgovor?.Tekst ?? "Nije definisan tačan odgovor",
                    JeTacno = stavka.Tacno == 1,
                    JeOdgovoreno = stavka.OdgovorId.HasValue,
                    JePreskoceno = !stavka.OdgovorId.HasValue,
                    OsvojeniPoeni = stavka.BrojBodova
                });
            }

            var ukupno = vecObradjena ? sesija.TraziBrojPitanja : stavke.Count;
            var tacno = vecObradjena ? sesija.BrojTacnih : stavke.Count(x => x.Tacno == 1);
            var netacno = vecObradjena ? sesija.BrojNetacnih : stavke.Count(x => x.OdgovorId.HasValue && x.Tacno == 0);
            var neodgovoreno = vecObradjena ? sesija.BrojPreskocenih : stavke.Count(x => !x.OdgovorId.HasValue);
            var ostvareniPoeni = vecObradjena ? sesija.OsvojeniPoeni : Math.Max(0m, Math.Round(stavke.Sum(x => x.BrojBodova), 2));
            var maksimalniPoeniZaSesiju = vecObradjena ? sesija.MaksimalniPoeni : Math.Round(sesija.TraziBrojPitanja * (sesija.VremenskoOgranicenje == 3 ? 0.1m : PoeniZaTacanOdgovor), 2);

            var procenat = maksimalniPoeniZaSesiju == 0
                ? 0
                : (int)Math.Round(ostvareniPoeni / maksimalniPoeniZaSesiju * 100);

            var rezultat = new SimulacijaRezultatViewModel
            {
                UkupnoPitanja = ukupno,
                TacnihOdgovora = tacno,
                NetacnihOdgovora = netacno,
                Neodgovorenih = neodgovoreno,
                Procenat = procenat,
                OsvojeniPoeni = ostvareniPoeni,
                MaksimalniPoeni = maksimalniPoeniZaSesiju,
                PrethodniScore = sesija.PrethodniScore, NoviScore = sesija.NoviScore,
                PrethodniScorePredmeta = sesija.PrethodniScorePredmeta, NoviScorePredmeta = sesija.NoviScorePredmeta,
                UtrosenoSekundi = vecObradjena ? (int)Math.Clamp((sesija.DatumZavrsetka - sesija.DatumPocetka).TotalSeconds, 0, sesija.VremenskoOgranicenje * 60) : CalculateElapsedSeconds(
                    sesija.DatumPocetka.Ticks,
                    sesija.VremenskoOgranicenje * 60
                ),
                Pregled = pregled
            };

            if (!vecObradjena)
            {
                sesija.PrethodniScore = await _score.ForUserAsync(userId!);
                sesija.PrethodniScorePredmeta = await _score.ForUserAsync(userId!, sesija.PredmetId);
                sesija.BrojNetacnih = netacno;
                sesija.BrojPreskocenih = neodgovoreno;
                sesija.OsvojeniPoeni = ostvareniPoeni;
                sesija.MaksimalniPoeni = maksimalniPoeniZaSesiju;
                sesija.Status = vrijemeIsteklo
                    ? StatusSesije.ISTEKAO
                    : StatusSesije.ZAVRSEN;

                sesija.BrojTacnih = tacno;
                sesija.Procenat = procenat;
                sesija.DatumZavrsetka = DateTime.UtcNow;

                await AzurirajStudentStatistikuAsync(sesija, ukupno, tacno);

                await _context.SaveChangesAsync();
                sesija.NoviScore = await _score.ForUserAsync(userId!);
                sesija.NoviScorePredmeta = await _score.ForUserAsync(userId!, sesija.PredmetId);
                rezultat.PrethodniScore = sesija.PrethodniScore;
                rezultat.NoviScore = sesija.NoviScore;
                rezultat.PrethodniScorePredmeta = sesija.PrethodniScorePredmeta;
                rezultat.NoviScorePredmeta = sesija.NoviScorePredmeta;
                var oldIds = await _context.KvizSesije.Where(s => s.StudentId == userId && (s.Status == StatusSesije.ZAVRSEN || s.Status == StatusSesije.ISTEKAO))
                    .OrderByDescending(s => s.DatumZavrsetka).ThenByDescending(s => s.ID).Skip(5).Select(s => s.ID).ToListAsync();
                _context.KvizSesijaPitanja.RemoveRange(await _context.KvizSesijaPitanja.Where(x => oldIds.Contains(x.KvizSesijaId)).ToListAsync());
                _context.KvizSesije.RemoveRange(await _context.KvizSesije.Where(s => oldIds.Contains(s.ID)).ToListAsync());
                await _context.SaveChangesAsync();
            }

            return rezultat;
        }

        private async Task SpasiOdgovoreIzFormeAsync(SimulacijaSubmitViewModel submitModel)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var sesija = await _context.KvizSesije
                .FirstOrDefaultAsync(s =>
                    s.ID == submitModel.KvizSesijaId &&
                    s.StudentId == userId &&
                    s.Status == StatusSesije.U_TOKU);

            if (sesija == null || submitModel.Odgovori == null || submitModel.Odgovori.Count == 0)
            {
                return;
            }

            var deadline = sesija.DatumPocetka.AddMinutes(sesija.VremenskoOgranicenje);

            if (DateTime.UtcNow >= deadline)
            {
                await FinishSimulationAndBuildResultAsync(sesija.ID);
                return;
            }

            var validniOdgovori = submitModel.Odgovori
                .Where(o => o.OdgovorId.HasValue)
                .GroupBy(o => o.PitanjeId)
                .Select(g => g.Last())
                .ToList();

            if (validniOdgovori.Count == 0)
            {
                return;
            }

            var stavke = await _context.KvizSesijaPitanja
                .Where(x => x.KvizSesijaId == sesija.ID)
                .OrderBy(x => x.RedniBroj)
                .ToListAsync();

            foreach (var item in validniOdgovori)
            {
                var sesijaPitanje = stavke.FirstOrDefault(x => x.PitanjeId == item.PitanjeId);

                if (sesijaPitanje == null || JePitanjeZakljucano(sesijaPitanje))
                {
                    continue;
                }

                var prvoOtvorenoPitanje = stavke.FirstOrDefault(x => !JePitanjeZakljucano(x));

                if (prvoOtvorenoPitanje == null || prvoOtvorenoPitanje.PitanjeId != item.PitanjeId)
                {
                    continue;
                }

                var odgovor = await _context.Odgovor
                    .FirstOrDefaultAsync(o =>
                        o.Id == item.OdgovorId.Value &&
                        o.PitanjeId == item.PitanjeId);

                if (odgovor == null)
                {
                    continue;
                }

                sesijaPitanje.OdgovorId = odgovor.Id;
                sesijaPitanje.Tacno = odgovor.IsTacan ? 1 : 0;
                sesijaPitanje.BrojBodova = odgovor.IsTacan ? (sesija.VremenskoOgranicenje == 3 ? 0.1m : PoeniZaTacanOdgovor) : (sesija.VremenskoOgranicenje == 3 ? -0.03m : PoeniZaNetacanOdgovor);
                break;
            }

            await _context.SaveChangesAsync();
        }

        // GET: /Student/Dashboard
        public async Task<IActionResult> Dashboard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var model = new DashboardViewModel();

            var aktivnaSesija = await _context.KvizSesije
                                                .Include(s => s.Predmet)
                                                .Include(s => s.Oblast)
                                                .Where(s => s.StudentId == userId && s.Status == StatusSesije.U_TOKU)
                                                .OrderByDescending(s => s.DatumPocetka)
                                                .FirstOrDefaultAsync();

            if (aktivnaSesija != null)
            {
                var deadline = aktivnaSesija.DatumPocetka.AddMinutes(aktivnaSesija.VremenskoOgranicenje);

                if (DateTime.UtcNow >= deadline)
                {
                    await FinishSimulationAndBuildResultAsync(aktivnaSesija.ID);
                }
                else
                {
                    model.AktivnaSesijaId = aktivnaSesija.ID;
                    model.AktivnaSesijaPredmetNaziv = aktivnaSesija.Predmet?.Naziv ?? "Odabrani predmet";
                    model.AktivnaSesijaPreostaloSekundi = (int)(deadline - DateTime.UtcNow).TotalSeconds;
                }
            }

            // Sve završene/istekle sesije ovog studenta (najnovije prvo)
            var sesije = await _context.KvizSesije
                .Include(s => s.Predmet)
                .Where(s => s.StudentId == userId && (s.Status == StatusSesije.ZAVRSEN || s.Status == StatusSesije.ISTEKAO))
                .OrderByDescending(s => s.DatumZavrsetka)
                .ToListAsync();

            model.KvizoviZavrseni = sesije.Count;
            model.ProsjecanRezultat = sesije.Count > 0
                ? (int)Math.Round(sesije.Average(s => s.Procenat))
                : 0;

            var totals = await _context.StudentStatistike.Where(s => s.KorisnikId == userId).ToListAsync();
            model.KvizoviZavrseni = totals.Sum(s => s.BrojKvizova);
            var totalQuestions = totals.Sum(s => (long)s.UkupnoPitanja);
            model.ProsjecanRezultat = totalQuestions == 0 ? 0 : (int)Math.Round(100.0 * totals.Sum(s => (long)s.TacniOdgovori) / totalQuestions);
            model.Score = await _score.ForUserAsync(userId!);
            var ranking = await _score.LeaderboardAsync(userId);
            model.UkupnoStudenata = ranking.Count;
            var position = ranking.FindIndex(s => s.JeTrenutni);
            model.Rang = position < 0 ? null : position + 1;

            model.NedavneAktivnosti = sesije
                .Take(5)
                .Select(s => new NedavnaAktivnostViewModel
                {
                    PredmetNaziv = s.Predmet != null ? s.Predmet.Naziv : "Nepoznat predmet",
                    Procenat = s.Procenat,
                    Datum = s.DatumZavrsetka,
                    SesijaId = s.ID, Poeni = s.OsvojeniPoeni, MaksimalniPoeni = s.MaksimalniPoeni,
                    PrethodniScore = s.PrethodniScore, NoviScore = s.NoviScore
                })
                .ToList();

            var brojPitanjaPoPredmetu = await _context.Pitanje
                .GroupBy(p => p.PredmetId)
                .Select(g => new { PredmetId = g.Key, Broj = g.Count() })
                .ToDictionaryAsync(x => x.PredmetId, x => x.Broj);

            var predmetNames = await _context.Predmet.ToDictionaryAsync(p => p.Id, p => p.Naziv);
            model.MojiPredmeti = totals.Where(t => predmetNames.ContainsKey(t.PredmetId)).Select(t => new PredmetNapredakViewModel {
                Naziv = predmetNames[t.PredmetId],
                Procenat = t.UkupnoPitanja == 0 ? 0 : (int)Math.Round(100.0 * t.TacniOdgovori / t.UkupnoPitanja),
                BrojPitanja = brojPitanjaPoPredmetu.GetValueOrDefault(t.PredmetId)
            }).ToList();

            return View(model);
        }

        // GET: /Student/Prepare
        public async Task<IActionResult> Prepare()
        {
            ViewBag.Predmeti = await _context.Predmet
                .OrderBy(p => p.Naziv)
                .Select(p => new { p.Id, p.Naziv })
                .ToListAsync();

            ViewBag.Oblasti = await _context.Oblast
                .Include(o => o.Predmet)
                .OrderBy(o => o.Predmet!.Naziv)
                .ThenBy(o => o.Naziv)
                .Select(o => new
                {
                    o.Id,
                    o.Naziv,
                    o.PredmetId,
                    PredmetNaziv = o.Predmet != null ? o.Predmet.Naziv : "Predmet"
                })
                .ToListAsync();

            return View();
        }

        // GET: /Student/PrepareQuestions?predmetId=5&oblastId=2 — JSON pitanja za vježbu (bez tajmera)
        public async Task<IActionResult> PrepareQuestions(int? predmetId, int? oblastId)
        {
            var pitanjaQuery = _context.Pitanje.AsQueryable();

            if (predmetId.HasValue)
            {
                pitanjaQuery = pitanjaQuery.Where(p => p.PredmetId == predmetId.Value);
            }

            if (oblastId.HasValue)
            {
                pitanjaQuery = pitanjaQuery.Where(p => p.OblastId == oblastId.Value);
            }

            var pitanja = await pitanjaQuery.ToListAsync();

            var ids = pitanja.Select(p => p.Id).ToList();
            var sviOdgovori = await _context.Odgovor
                .Where(o => ids.Contains(o.PitanjeId))
                .ToListAsync();

            var poPitanju = sviOdgovori
                .GroupBy(o => o.PitanjeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var rezultat = new List<object>();

            foreach (var p in pitanja)
            {
                if (!poPitanju.TryGetValue(p.Id, out var ods)) continue;

                if (ods.Count < 2 || ods.Count(o => o.IsTacan) != 1) continue;

                var promijesani = Shuffle(ods);
                var tacanIndex = promijesani.FindIndex(o => o.IsTacan);

                rezultat.Add(new
                {
                    tekst = p.TekstPitanja,
                    opcije = promijesani.Select(o => o.Tekst).ToList(),
                    tacan = tacanIndex
                });
            }

            return Json(Shuffle(rezultat));
        }

        // GET: /Student/Simulate
        public async Task<IActionResult> Simulate()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var model = new SimulacijaKvizaViewModel
            {
                Predmeti = await GetPredmetiZaSimulacijuAsync(),
                Oblasti = await GetOblastiZaSimulacijuAsync()
            };

            var aktivnaSesija = await _context.KvizSesije
                .Include(s => s.Predmet)
                .Include(s => s.Oblast)
                .Where(s => s.StudentId == userId && s.Status == StatusSesije.U_TOKU)
                .OrderByDescending(s => s.DatumPocetka)
                .FirstOrDefaultAsync();

            if (aktivnaSesija != null)
            {
                var deadline = aktivnaSesija.DatumPocetka.AddMinutes(aktivnaSesija.VremenskoOgranicenje);

                if (DateTime.UtcNow >= deadline)
                {
                    var rezultat = await FinishSimulationAndBuildResultAsync(aktivnaSesija.ID);

                    model.Result = rezultat;
                    model.PredmetId = aktivnaSesija.PredmetId;
                    model.PredmetNaziv = aktivnaSesija.Predmet?.Naziv ?? "Odabrani predmet";
                    model.OblastId = aktivnaSesija.OblastId;
                    model.OblastNaziv = aktivnaSesija.Oblast?.Naziv;
                    model.BrojPitanja = rezultat?.UkupnoPitanja ?? 0;

                    return View(model);
                }

                model.AktivnaSesijaId = aktivnaSesija.ID;
                model.AktivnaSesijaPredmetNaziv = aktivnaSesija.Predmet?.Naziv ?? "Odabrani predmet";
                model.AktivnaSesijaOblastNaziv = aktivnaSesija.Oblast?.Naziv;
                model.PreostaloSekundi = (int)(deadline - DateTime.UtcNow).TotalSeconds;
            }

            if (TempData["SimulationError"] is string error)
            {
                model.ErrorMessage = error;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartSimulation(SimulacijaKvizaViewModel model)
        {
            await using var tx = await LockSimulationAsync();
            var result = await StartSimulationLocked(model);
            await tx.CommitAsync();
            return result;
        }

        private async Task<IActionResult> StartSimulationLocked(SimulacijaKvizaViewModel model)
        {
            if (!model.PredmetId.HasValue)
            {
                TempData["SimulationError"] = "Moraš odabrati predmet prije pokretanja simulacije.";
                return RedirectToAction(nameof(Simulate));
            }

            if (!model.OblastId.HasValue)
            {
                TempData["SimulationError"] = "Moraš odabrati oblast prije pokretanja simulacije.";
                return RedirectToAction(nameof(Simulate));
            }

            var oblastValidna = await _context.Oblast.AnyAsync(o => o.Id == model.OblastId.Value && o.PredmetId == model.PredmetId.Value);
            if (!oblastValidna)
            {
                TempData["SimulationError"] = "Odabrana oblast ne pripada odabranom predmetu.";
                return RedirectToAction(nameof(Simulate));
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var now = DateTime.UtcNow;

            var aktivneSesije = await _context.KvizSesije
                .Where(s =>
                    s.StudentId == userId &&
                    s.Status == StatusSesije.U_TOKU)
                .OrderByDescending(s => s.DatumPocetka)
                .ToListAsync();

            foreach (var sesija in aktivneSesije)
            {
                var deadline = sesija.DatumPocetka.AddMinutes(sesija.VremenskoOgranicenje);

                if (now < deadline)
                {
                    TempData["SimulationError"] = "Već imaš aktivnu simulaciju kviza. Prvo nastavi postojeći kviz ili sačekaj da vrijeme istekne.";
                    return RedirectToAction(nameof(Simulate));
                }

                await FinishSimulationAndBuildResultAsync(sesija.ID);
            }

            var simulationModel = await BuildSimulationAsync(model.PredmetId.Value, model.OblastId.Value);

            if (!string.IsNullOrWhiteSpace(simulationModel.ErrorMessage))
            {
                TempData["SimulationError"] = simulationModel.ErrorMessage;
                return RedirectToAction(nameof(Simulate));
            }

            return View("Simulate", simulationModel);
        }

        public async Task<IActionResult> ContinueSimulation(int kvizSesijaId)
        {
            var model = await BuildActiveSimulationModelAsync(kvizSesijaId);

            if (model.Result != null)
            {
                return View("Simulate", model);
            }

            if (!string.IsNullOrWhiteSpace(model.ErrorMessage))
            {
                TempData["SimulationError"] = model.ErrorMessage;
                return RedirectToAction(nameof(Simulate));
            }

            return View("Simulate", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitSimulation(SimulacijaSubmitViewModel submitModel)
        {
            await using var tx = await LockSimulationAsync();
            var result = await SubmitSimulationLocked(submitModel);
            await tx.CommitAsync();
            return result;
        }

        private async Task<IActionResult> SubmitSimulationLocked(SimulacijaSubmitViewModel submitModel)
        {
            if (submitModel.KvizSesijaId <= 0)
            {
                TempData["SimulationError"] = "Simulacija nije validna.";
                return RedirectToAction(nameof(Simulate));
            }

            await SpasiOdgovoreIzFormeAsync(submitModel);

            var rezultat = await FinishSimulationAndBuildResultAsync(submitModel.KvizSesijaId);

            if (rezultat == null)
            {
                TempData["SimulationError"] = "Sesija simulacije nije pronađena.";
                return RedirectToAction(nameof(Simulate));
            }

            var sesija = await _context.KvizSesije
                .Include(s => s.Predmet)
                .Include(s => s.Oblast)
                .FirstAsync(s => s.ID == submitModel.KvizSesijaId);

            return RedirectToAction(nameof(SimulationResult), new { id = sesija.ID });
        }

        [HttpGet]
        public async Task<IActionResult> SimulationResult(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sesija = await _context.KvizSesije.Include(s => s.Predmet).Include(s => s.Oblast)
                .FirstOrDefaultAsync(s => s.ID == id && s.StudentId == userId &&
                    (s.Status == StatusSesije.ZAVRSEN || s.Status == StatusSesije.ISTEKAO));
            if (sesija == null) return NotFound("Rezultat nije među posljednjih 5 simulacija.");
            var result = await FinishSimulationAndBuildResultAsync(id);
            return View("Simulate", new SimulacijaKvizaViewModel {
                KvizSesijaId = id, PredmetId = sesija.PredmetId, OblastId = sesija.OblastId,
                PredmetNaziv = sesija.Predmet?.Naziv, OblastNaziv = sesija.Oblast?.Naziv,
                BrojPitanja = sesija.TraziBrojPitanja, VremenskoOgranicenjeMinuta = sesija.VremenskoOgranicenje,
                Result = result
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSimulationAnswer(SaveSimulationAnswerViewModel answerModel)
        {
            await using var tx = await LockSimulationAsync();
            var result = await SaveSimulationAnswerLocked(answerModel);
            await tx.CommitAsync();
            return result;
        }

        private async Task<IActionResult> SaveSimulationAnswerLocked(SaveSimulationAnswerViewModel answerModel)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var sesija = await _context.KvizSesije
                .FirstOrDefaultAsync(s =>
                    s.ID == answerModel.KvizSesijaId &&
                    s.StudentId == userId &&
                    s.Status == StatusSesije.U_TOKU);

            if (sesija == null)
            {
                return BadRequest("Aktivna sesija nije pronađena.");
            }

            var deadline = sesija.DatumPocetka.AddMinutes(sesija.VremenskoOgranicenje);

            if (DateTime.UtcNow >= deadline)
            {
                await FinishSimulationAndBuildResultAsync(sesija.ID);
                return BadRequest("Vrijeme je isteklo.");
            }

            var stavke = await _context.KvizSesijaPitanja
                .Where(x => x.KvizSesijaId == sesija.ID)
                .OrderBy(x => x.RedniBroj)
                .ToListAsync();

            var sesijaPitanje = stavke.FirstOrDefault(x => x.PitanjeId == answerModel.PitanjeId);

            if (sesijaPitanje == null)
            {
                return BadRequest("Pitanje nije dio ove sesije.");
            }

            if (JePitanjeZakljucano(sesijaPitanje))
            {
                return BadRequest("Pitanje je već potvrđeno ili preskočeno.");
            }

            var prvoOtvorenoPitanje = stavke.FirstOrDefault(x => !JePitanjeZakljucano(x));

            if (prvoOtvorenoPitanje == null || prvoOtvorenoPitanje.PitanjeId != answerModel.PitanjeId)
            {
                return BadRequest("Navigacija je sekvencijalna. Nije dozvoljen povratak ili preskakanje redoslijeda pitanja.");
            }

            if (!answerModel.OdgovorId.HasValue)
            {
                sesijaPitanje.OdgovorId = null;
                sesijaPitanje.Tacno = OznakaPreskocenogPitanja;
                sesijaPitanje.BrojBodova = 0;

                await _context.SaveChangesAsync();

                return Json(new { saved = true, skipped = true });
            }

            var odgovor = await _context.Odgovor
                .FirstOrDefaultAsync(o =>
                    o.Id == answerModel.OdgovorId.Value &&
                    o.PitanjeId == answerModel.PitanjeId);

            if (odgovor == null)
            {
                return BadRequest("Odgovor nije validan.");
            }

            sesijaPitanje.OdgovorId = odgovor.Id;
            sesijaPitanje.Tacno = odgovor.IsTacan ? 1 : 0;
            sesijaPitanje.BrojBodova = odgovor.IsTacan ? (sesija.VremenskoOgranicenje == 3 ? 0.1m : PoeniZaTacanOdgovor) : (sesija.VremenskoOgranicenje == 3 ? -0.03m : PoeniZaNetacanOdgovor);

            await _context.SaveChangesAsync();

            return Json(new { saved = true, skipped = false });
        }

        public IActionResult Index()
        {
            return RedirectToAction(nameof(Dashboard));
        }

    }
}
