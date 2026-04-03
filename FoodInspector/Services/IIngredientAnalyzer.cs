using FoodInspector.Models;

namespace FoodInspector.Services;

public interface IIngredientAnalyzer
{
    Task<IngredientAnalysis> AnalyzeAsync(string ingredientsText, int userAge);

    /// <summary>
    /// Analyse ingredients text using user-defined custom ingredients in addition
    /// to the built-in dictionaries.
    /// </summary>
    Task<IngredientAnalysis> AnalyzeAsync(string ingredientsText, int userAge, IReadOnlyList<CustomIngredient> customIngredients);
}
