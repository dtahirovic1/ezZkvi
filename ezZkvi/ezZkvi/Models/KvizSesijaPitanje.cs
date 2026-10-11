using System.ComponentModel.DataAnnotations;

namespace ezZkvi.Models
{
    public class KvizSesijaPitanje
    {
        [Key]
        public int ID { get; set; }

        [Required]
        public int KvizSesijaId { get; set; }
        public KvizSesija? KvizSesija { get; set; }

        [Required]
        public int PitanjeId { get; set; }
        public Pitanje? Pitanje { get; set; }

        public int? OdgovorId { get; set; }
        public Odgovor? Odgovor { get; set; }

        [Range(1, int.MaxValue)]
        public int RedniBroj { get; set; }

        [Range(typeof(decimal), "-0.05", "0.15")]
        public decimal BrojBodova { get; set; }

        [Range(-1, 1, ErrorMessage = "Tacno mora biti između -1 i 1.")]
        public double Tacno { get; set; }
    }
}