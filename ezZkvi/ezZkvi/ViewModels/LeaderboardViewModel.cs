namespace ezZkvi.ViewModels;
public class LeaderboardViewModel { public List<LeaderboardEntryViewModel> Entries { get; set; } = new(); }
public class LeaderboardEntryViewModel {
    public string StudentId { get; set; } = "";
    public string Ime { get; set; } = "Student";
    public string Inicijali { get; set; } = "ST";
    public double Score { get; set; }
    public long DisplayScore => ezZkvi.Services.ScoreService.Display(Score);
    public double Accuracy { get; set; }
    public long Correct { get; set; }
    public bool JeTrenutni { get; set; }
}
