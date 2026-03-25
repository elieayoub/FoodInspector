using FoodInspector.Models;

namespace FoodInspector.Services;

public interface IIngredientAnalyzer
{
    Task<IngredientAnalysis> AnalyzeAsync(string ingredientsText, int userAge);
}
