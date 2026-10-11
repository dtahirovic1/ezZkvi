using ezZkvi.Models;

namespace ezZkvi.Services;

public static class DifficultyNames
{
    public static string Display(Tezina value) => value switch {
        Tezina.LAKO => "Lako", Tezina.SREDNJE => "Srednje", Tezina.TESKO => "Teško", _ => "Nepoznato"
    };
    public static string Csv(Tezina value) => value == Tezina.TESKO ? "Tesko" : Display(value);
    public static bool TryParse(string value, out Tezina difficulty) =>
        Enum.TryParse(value.Trim().Replace("š", "s", StringComparison.OrdinalIgnoreCase), true, out difficulty)
        && Enum.IsDefined(difficulty);
}
