using FoodInspector.Models;

namespace FoodInspector.Services;

public interface IDailyIntakeService
{
    DailyIntakeSummary CalculateDailySummary(List<IngredientAnalysis> analyses, int userAge, DateTime date);
    Dictionary<string, int> GetDailyLimits(int userAge);
}
