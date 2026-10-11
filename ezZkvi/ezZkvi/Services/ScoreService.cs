using ezZkvi.Data;
using ezZkvi.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ezZkvi.Services;

public class ScoreService(ApplicationDbContext context)
{
    public static double Calculate(long correct, long total)
    {
        if (total <= 0) return 0;
        var p = Math.Clamp((double)correct / total, 0, 1);
        const double z = 2.33;
        var z2 = z * z;
        var w = Math.Clamp((p + z2 / (2 * total) - z * Math.Sqrt(p * (1 - p) / total + z2 / (4 * total * (double)total))) / (1 + z2 / total), 0, 1);
        return 1000 * w * w * (1 + 0.25 * Math.Log2(1 + total / 18.0));
    }

    public static long Display(double score) => (long)Math.Round(score, MidpointRounding.AwayFromZero);

    public async Task<double> ForUserAsync(string userId, int? predmetId = null)
    {
        var rows = await context.StudentStatistike.AsNoTracking()
            .Where(s => s.KorisnikId == userId && (!predmetId.HasValue || s.PredmetId == predmetId))
            .ToListAsync();
        return Calculate(rows.Sum(s => (long)s.TacniOdgovori), rows.Sum(s => (long)s.UkupnoPitanja));
    }

    public async Task<List<LeaderboardEntryViewModel>> LeaderboardAsync(string? currentUserId, int? predmetId = null)
    {
        var rows = await context.StudentStatistike.AsNoTracking()
            .Where(s => !predmetId.HasValue || s.PredmetId == predmetId).ToListAsync();
        var users = await context.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.UserName);
        return rows.GroupBy(s => s.KorisnikId).Where(g => users.ContainsKey(g.Key))
            .Select(g => {
                var total = g.Sum(s => (long)s.UkupnoPitanja);
                var correct = g.Sum(s => (long)s.TacniOdgovori);
                var name = users[g.Key] ?? "Student";
                return new LeaderboardEntryViewModel {
                    StudentId = g.Key, Ime = name,
                    Inicijali = name[..Math.Min(2, name.Length)].ToUpperInvariant(),
                    Score = Calculate(correct, total),
                    Accuracy = total == 0 ? 0 : (double)correct / total,
                    Correct = correct, JeTrenutni = g.Key == currentUserId
                };
            }).OrderByDescending(e => e.Score).ThenByDescending(e => e.Accuracy)
            .ThenByDescending(e => e.Correct).ThenBy(e => e.StudentId, StringComparer.Ordinal).ToList();
    }
}
