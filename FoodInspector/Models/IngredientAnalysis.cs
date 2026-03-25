namespace FoodInspector.Models;

public class IngredientAnalysis
{
    public string OverallVerdict { get; set; } = string.Empty;   // "Buy" / "Avoid" / "Caution"
    public string Summary { get; set; } = string.Empty;
    public List<IngredientDetail> Ingredients { get; set; } = new();
}

public class IngredientDetail
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;          // "Good", "Neutral", "Bad"
    public string Reason { get; set; } = string.Empty;
}
