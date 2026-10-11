using System.ComponentModel.DataAnnotations;

namespace ezZkvi.Models
{
    public class KvizSesija
    {
        [Key]
        public int ID { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Broj pitanja mora biti veći od 0.")]
        public int TraziBrojPitanja { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Vremensko ograničenje mora biti veće od 0.")]
        public int VremenskoOgranicenje { get; set; }

        [EnumDataType(typeof(StatusSesije), ErrorMessage = "Neispravna vrijednost statusa.")]
        public StatusSesije Status { get; set; }

        // Ko je radio kviz (ID prijavljenog studenta)
        public string? StudentId { get; set; }

        // Iz kojeg predmeta je bio kviz
        public int? PredmetId { get; set; }
        public Predmet? Predmet { get; set; }

        // Iz koje oblasti je bio kviz
        public int? OblastId { get; set; }
        public Oblast? Oblast { get; set; }

        // Rezultat
        public int BrojTacnih { get; set; }
        public int Procenat { get; set; }
        public int? BrojNetacnih { get; set; }
        public int? BrojPreskocenih { get; set; }
        public decimal OsvojeniPoeni { get; set; }
        public decimal MaksimalniPoeni { get; set; }
        public double PrethodniScore { get; set; }
        public double NoviScore { get; set; }
        public double PrethodniScorePredmeta { get; set; }
        public double NoviScorePredmeta { get; set; }

        public DateTime DatumPocetka { get; set; }
        public DateTime DatumZavrsetka { get; set; }
    }
}
