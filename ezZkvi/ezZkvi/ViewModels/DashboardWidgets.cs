using ezZkvi.Models;

namespace ezZkvi.ViewModels
{
    public class KorisnikNaCekanjuItem
    {
        public string Id { get; set; } = "";
        public string Ime { get; set; } = "";
        public string Inicijali { get; set; } = "";
    }

    public class PredmetAktivnostItem
    {
        public int BrojOblasti { get; set; }
        public int Id { get; set; }
        public string Naziv { get; set; } = "";
        public int BrojPitanja { get; set; }
        public int BrojKvizova { get; set; }
    }


    public class OblastAktivnostItem
    {
        public int Id { get; set; }
        public string Naziv { get; set; } = "";
        public int PredmetId { get; set; }
        public string PredmetNaziv { get; set; } = "";
        public int BrojPitanja { get; set; }
    }

    public class KvizAktivnostItem
    {
        public string Korisnik { get; set; } = "";
        public string Predmet { get; set; } = "";
        public int Procenat { get; set; }
        public DateTime Datum { get; set; }
    }

    public class FeedbackItem
    {
        public string Sadrzaj { get; set; } = "";
        public TipFeedbacka Tip { get; set; }
        public DateTime Datum { get; set; }
    }
}
