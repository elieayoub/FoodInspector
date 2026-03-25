namespace FoodInspector.Models;

public class ScanResult
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public string AnalysisJson { get; set; } = string.Empty;
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
}
