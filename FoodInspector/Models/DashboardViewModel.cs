namespace FoodInspector.Models;

public class DashboardViewModel
{
    public string UserName { get; set; } = string.Empty;
    public int UserAge { get; set; }
    public DateTime Date { get; set; }
    public DailyIntakeSummary DailySummary { get; set; } = new();
    public List<FoodLogEntry> TodayFoodLogs { get; set; } = new();
    public List<DailyHealthRecord> HealthHistory { get; set; } = new();

    /// <summary>
    /// Aggregated ingredient totals consumed today (name → count + status).
    /// </summary>
    public List<IngredientTotal> IngredientTotals { get; set; } = new();
}

public class FoodLogEntry
{
    public int Id { get; set; }
    public string ExtractedText { get; set; } = string.Empty;
    public string Verdict { get; set; } = string.Empty;
    public int BadCount { get; set; }
    public int GoodCount { get; set; }
    public DateTime EatenAt { get; set; }
    public List<IngredientDetail> Ingredients { get; set; } = new();
}

public class DailyHealthRecord
{
    public DateTime Date { get; set; }
    public int TotalMeals { get; set; }
    public bool IsHealthy { get; set; }
    public int AlertCount { get; set; }
    public int BadIngredientCount { get; set; }
}

public class IngredientTotal
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public string WorstStatus { get; set; } = "Neutral";  // "Good", "Neutral", "Bad"
}
