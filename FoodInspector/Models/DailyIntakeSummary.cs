namespace FoodInspector.Models;

public class DailyIntakeSummary
{
    public DateTime Date { get; set; }
    public int TotalMeals { get; set; }
    public Dictionary<string, IngredientAccumulation> Accumulations { get; set; } = new();
    public List<string> Alerts { get; set; } = new();
    public bool IsHealthy => Alerts.Count == 0;
}

public class IngredientAccumulation
{
    public string IngredientName { get; set; } = string.Empty;
    public int Count { get; set; }
    public string WorstStatus { get; set; } = "Neutral";
    public double DailyLimitMax { get; set; }
    public bool ExceedsLimit { get; set; }
    public string LimitUnit { get; set; } = "servings";
}
