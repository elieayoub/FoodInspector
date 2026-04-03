namespace FoodInspector.Models;

public class FoodLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ScanResultId { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public string AnalysisJson { get; set; } = string.Empty;
    public DateTime EatenAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
    public ScanResult? ScanResult { get; set; }
}
