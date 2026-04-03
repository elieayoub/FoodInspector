namespace FoodInspector.Models;

public class ScanViewModel
{
    public int? ScanResultId { get; set; }
    public string? ImageBase64 { get; set; }
    public string? ExtractedText { get; set; }
    public IngredientAnalysis? Analysis { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int UserAge { get; set; }
}
