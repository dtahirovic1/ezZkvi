using System.ComponentModel.DataAnnotations;
using ezZkvi.Models;

namespace ezZkvi.ViewModels
{
    public class PitanjeSaOdgovorimaViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tekst pitanja je obavezan.")]
        [StringLength(1000, MinimumLength = 5, ErrorMessage = "Tekst pitanja mora imati između 5 i 1000 karaktera.")]
        public string TekstPitanja { get; set; } = string.Empty;

        [Required(ErrorMessage = "Predmet je obavezan.")]
        public int PredmetId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Oblast je obavezna.")]
        public int OblastId { get; set; }

        [EnumDataType(typeof(Tezina), ErrorMessage = "Odaberite važeću težinu pitanja.")]
        public Tezina Tezina { get; set; }

        [Required(ErrorMessage = "Odgovor 1 je obavezan.")]
        [StringLength(500, MinimumLength = 1, ErrorMessage = "Tekst odgovora mora imati između 1 i 500 karaktera.")]
        public string Odgovor1 { get; set; } = string.Empty;

        [Required(ErrorMessage = "Odgovor 2 je obavezan.")]
        [StringLength(500, MinimumLength = 1, ErrorMessage = "Tekst odgovora mora imati između 1 i 500 karaktera.")]
        public string Odgovor2 { get; set; } = string.Empty;

        [Required(ErrorMessage = "Odgovor 3 je obavezan.")]
        [StringLength(500, MinimumLength = 1, ErrorMessage = "Tekst odgovora mora imati između 1 i 500 karaktera.")]
        public string Odgovor3 { get; set; } = string.Empty;

        [Required(ErrorMessage = "Odgovor 4 je obavezan.")]
        [StringLength(500, MinimumLength = 1, ErrorMessage = "Tekst odgovora mora imati između 1 i 500 karaktera.")]
        public string Odgovor4 { get; set; } = string.Empty;

        [Range(1, 4, ErrorMessage = "Mora biti odabran tačan odgovor.")]
        public int TacanOdgovor { get; set; }
    }
}
