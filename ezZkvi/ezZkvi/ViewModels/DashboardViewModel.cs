namespace ezZkvi.ViewModels
{
    public class DashboardViewModel
    {
        // Statistika (kartice na vrhu)
        public int KvizoviZavrseni { get; set; }
        public int ProsjecanRezultat { get; set; }   // u procentima
        public int? Rang { get; set; }
        public int UkupnoStudenata { get; set; }

        public double Score { get; set; }

        // Legacy XP fields
        public int Nivo { get; set; }
        public int XpUkupno { get; set; }
        public int XpUNivou { get; set; }            // bodovi unutar trenutnog nivoa (0-99)
        public int XpZaNivo { get; set; } = 100;     // koliko XP treba za sljedeći nivo

        public List<NedavnaAktivnostViewModel> NedavneAktivnosti { get; set; } = new();
        public List<PredmetNapredakViewModel> MojiPredmeti { get; set; } = new();

        public int? AktivnaSesijaId { get; set; }
        public string? AktivnaSesijaPredmetNaziv { get; set; }
        public int AktivnaSesijaPreostaloSekundi { get; set; }

        public bool ImaAktivnuSesiju => AktivnaSesijaId.HasValue && AktivnaSesijaPreostaloSekundi > 0;
    }

    public class NedavnaAktivnostViewModel
    {
        public string PredmetNaziv { get; set; } = "Predmet";
        public int Procenat { get; set; }
        public int SesijaId { get; set; }
        public decimal Poeni { get; set; }
        public decimal MaksimalniPoeni { get; set; }
        public double PrethodniScore { get; set; }
        public double NoviScore { get; set; }
        public DateTime Datum { get; set; }
    }

    public class PredmetNapredakViewModel
    {
        public string Naziv { get; set; } = "Predmet";
        public int Procenat { get; set; }
        public int BrojPitanja { get; set; }
    }
}
