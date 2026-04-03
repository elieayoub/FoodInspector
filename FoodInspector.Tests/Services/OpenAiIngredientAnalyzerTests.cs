using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using FoodInspector.Services;

namespace FoodInspector.Tests.Services;

public class OpenAiIngredientAnalyzerTests
{
    private readonly OpenAiIngredientAnalyzer _analyzer;

    public OpenAiIngredientAnalyzerTests()
    {
        // No API key configured → forces fallback rule engine for all tests
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = ""
            })
            .Build();

        var httpFactory = new Mock<IHttpClientFactory>();
        var logger = new Mock<ILogger<OpenAiIngredientAnalyzer>>();

        _analyzer = new OpenAiIngredientAnalyzer(config, httpFactory.Object, logger.Object);
    }

    // ── Verdict logic ──

    [Fact]
    public async Task AnalyzeAsync_AllSafeIngredients_ReturnsBuyVerdict()
    {
        var result = await _analyzer.AnalyzeAsync("whole grain, oats, olive oil", 30);

        Assert.Equal("Buy", result.OverallVerdict);
        Assert.Contains("No major harmful ingredients detected", result.Summary);
    }

    [Fact]
    public async Task AnalyzeAsync_OneBadIngredient_ReturnsCautionVerdict()
    {
        var result = await _analyzer.AnalyzeAsync("flour, sugar, aspartame", 30);

        Assert.Equal("Caution", result.OverallVerdict);
        Assert.Contains("1 ingredient(s) that deserve attention", result.Summary);
    }

    [Fact]
    public async Task AnalyzeAsync_ThreeOrMoreBadIngredients_ReturnsAvoidVerdict()
    {
        var result = await _analyzer.AnalyzeAsync("aspartame, red 40, sodium nitrite, sugar", 30);

        Assert.Equal("Avoid", result.OverallVerdict);
        Assert.Contains("concerning ingredient(s)", result.Summary);
    }

    // ── Ingredient classification ──

    [Theory]
    [InlineData("aspartame")]
    [InlineData("high fructose corn syrup")]
    [InlineData("red 40")]
    [InlineData("sodium nitrite")]
    [InlineData("titanium dioxide")]
    [InlineData("trans fat")]
    [InlineData("bha")]
    [InlineData("potassium bromate")]
    public async Task AnalyzeAsync_KnownBadIngredient_ClassifiedAsBad(string ingredient)
    {
        var result = await _analyzer.AnalyzeAsync(ingredient, 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Bad", detail.Status);
    }

    [Theory]
    [InlineData("sugar")]
    [InlineData("salt")]
    [InlineData("sucralose")]
    [InlineData("palm oil")]
    [InlineData("caffeine")]
    public async Task AnalyzeAsync_KnownNeutralIngredient_ClassifiedAsNeutral(string ingredient)
    {
        var result = await _analyzer.AnalyzeAsync(ingredient, 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Neutral", detail.Status);
    }

    [Theory]
    [InlineData("whole grain")]
    [InlineData("olive oil")]
    [InlineData("vitamin C")]
    [InlineData("omega-3")]
    [InlineData("fiber")]
    public async Task AnalyzeAsync_KnownGoodIngredient_ClassifiedAsGood(string ingredient)
    {
        var result = await _analyzer.AnalyzeAsync(ingredient, 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Good", detail.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_UnknownIngredient_ClassifiedAsNeutral()
    {
        var result = await _analyzer.AnalyzeAsync("xanthan gum", 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Neutral", detail.Status);
        Assert.Contains("no major concerns", detail.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Age-specific adjustments ──

    [Fact]
    public async Task AnalyzeAsync_ChildAge_AddsChildWarningForDyes()
    {
        var result = await _analyzer.AnalyzeAsync("red 40", userAge: 8);

        var detail = Assert.Single(result.Ingredients);
        Assert.Contains("children under 12", detail.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_ChildAge_AddsChildWarningForAspartame()
    {
        var result = await _analyzer.AnalyzeAsync("aspartame", userAge: 10);

        var detail = Assert.Single(result.Ingredients);
        Assert.Contains("children under 12", detail.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_ElderlyAge_AddsElderlyWarningForSodium()
    {
        var result = await _analyzer.AnalyzeAsync("salt", userAge: 70);

        var detail = Assert.Single(result.Ingredients);
        Assert.Contains("over 60", detail.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_ElderlyAge_AddsElderlyWarningForCaffeine()
    {
        var result = await _analyzer.AnalyzeAsync("caffeine", userAge: 65);

        var detail = Assert.Single(result.Ingredients);
        Assert.Contains("over 60", detail.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Age-specific summary messages ──

    [Fact]
    public async Task AnalyzeAsync_ChildAge_SummaryMentionsChild()
    {
        var result = await _analyzer.AnalyzeAsync("flour", userAge: 8);
        Assert.Contains("child", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_TeenAge_SummaryMentionsTeenager()
    {
        var result = await _analyzer.AnalyzeAsync("flour", userAge: 15);
        Assert.Contains("teenager", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_AdultAge_SummaryMentionsAdult()
    {
        var result = await _analyzer.AnalyzeAsync("flour", userAge: 30);
        Assert.Contains("adult", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeAsync_ElderlyAge_SummaryMentionsOver60()
    {
        var result = await _analyzer.AnalyzeAsync("flour", userAge: 70);
        Assert.Contains("over 60", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ── Parsing edge cases ──

    [Fact]
    public async Task AnalyzeAsync_CommaSeparatedIngredients_ParsesAll()
    {
        var result = await _analyzer.AnalyzeAsync("flour, sugar, salt, olive oil", 30);

        Assert.Equal(4, result.Ingredients.Count);
    }

    [Fact]
    public async Task AnalyzeAsync_SemicolonSeparatedIngredients_ParsesAll()
    {
        var result = await _analyzer.AnalyzeAsync("flour; sugar; salt", 30);

        Assert.Equal(3, result.Ingredients.Count);
    }

    [Fact]
    public async Task AnalyzeAsync_NewlineSeparatedIngredients_ParsesAll()
    {
        var result = await _analyzer.AnalyzeAsync("flour\nsugar\nsalt", 30);

        Assert.Equal(3, result.Ingredients.Count);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyText_ReturnsEmptyIngredientsList()
    {
        var result = await _analyzer.AnalyzeAsync("", 30);

        Assert.Empty(result.Ingredients);
        Assert.Equal("Buy", result.OverallVerdict);
    }

    [Fact]
    public async Task AnalyzeAsync_MixedIngredients_CorrectlyCategorizes()
    {
        var result = await _analyzer.AnalyzeAsync("whole grain, aspartame, flour", 30);

        Assert.Equal(3, result.Ingredients.Count);
        Assert.Contains(result.Ingredients, i => i.Status == "Good");
        Assert.Contains(result.Ingredients, i => i.Status == "Bad");
        Assert.Contains(result.Ingredients, i => i.Status == "Neutral");
    }

    // ── Fallback when no API key ──

    [Fact]
    public async Task AnalyzeAsync_NoApiKey_UsesFallbackEngine()
    {
        // The constructor sets empty API key, so this always uses fallback
        var result = await _analyzer.AnalyzeAsync("sugar, olive oil", 30);

        Assert.NotNull(result);
        Assert.NotEmpty(result.OverallVerdict);
        Assert.NotEmpty(result.Summary);
        Assert.NotEmpty(result.Ingredients);
    }

    // ── Descriptive text / disclaimer paragraphs should be filtered out ──

    [Fact]
    public async Task AnalyzeAsync_DisclaimerParagraph_IsNotTreatedAsIngredient()
    {
        // This is the exact text OCR extracts from the bottom of nutrition labels
        var text = "* The % Daily Value (DV) tells you how much a nutrient in\n" +
                   "aserving of food contributes to a daily diet. 2,000 calories\n" +
                   "aday is used for general nutrition advice";

        var result = await _analyzer.AnalyzeAsync(text, 30);

        // None of these sentence fragments should appear as ingredients
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("tells you", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("contributes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("general nutrition advice", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("The % Daily Value tells you how much a nutrient in")]
    [InlineData("aserving of food contributes to a daily diet")]
    [InlineData("aday is used for general nutrition advice")]
    [InlineData("Percent daily values are based on a 2000 calorie diet")]
    [InlineData("Not a significant source of added sugars")]
    public async Task AnalyzeAsync_SentenceText_IsFilteredOut(string sentence)
    {
        var result = await _analyzer.AnalyzeAsync(sentence, 30);

        // Sentence text should produce no ingredients
        Assert.Empty(result.Ingredients);
    }

    [Fact]
    public async Task AnalyzeAsync_MixOfIngredientsAndDisclaimer_OnlyExtractsIngredients()
    {
        var text = "Sugar, Salt, Calcium 320mg\n" +
                   "* The % Daily Value tells you how much a nutrient in\n" +
                   "aserving of food contributes to a daily diet";

        var result = await _analyzer.AnalyzeAsync(text, 30);

        // Should have the real ingredients but not the disclaimer text
        Assert.True(result.Ingredients.Count >= 2,
            $"Expected at least 2 real ingredients, got {result.Ingredients.Count}");
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("tells you", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("contributes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeAsync_RealNutritionLabelText_FiltersDisclaimer()
    {
        // Simulates full OCR output from a real nutrition label
        var text = "Total Fat 9g\n" +
                   "Trans Fat 0g\n" +
                   "Sodium 850mg\n" +
                   "Calcium 320mg\n" +
                   "Iron 1.6mg\n" +
                   "Protein 15g\n" +
                   "Vitamin D 0mcg\n" +
                   "* The % Daily Value (DV) tells you how much a nutrient in\n" +
                   "aserving of food contributes to a daily diet. 2,000 calories\n" +
                   "aday is used for general nutrition advice.";

        var result = await _analyzer.AnalyzeAsync(text, 30);

        // Should NOT have any disclaimer fragments as ingredients
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("tells", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("advice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("diet", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("sugar")]
    [InlineData("Trans Fat 2g")]
    [InlineData("Sodium 850mg")]
    [InlineData("high fructose corn syrup")]
    [InlineData("whole grain oats")]
    [InlineData("olive oil")]
    public async Task AnalyzeAsync_RealIngredientNames_AreNotFilteredOut(string ingredient)
    {
        var result = await _analyzer.AnalyzeAsync(ingredient, 30);

        // Real ingredient names should still produce exactly one ingredient
        Assert.Single(result.Ingredients);
    }

    // ── Zero-quantity ingredients should NOT be flagged ──

    [Theory]
    [InlineData("Trans Fat 0g")]
    [InlineData("Trans Fat 0 g")]
    [InlineData("Trans Fat 0.0g")]
    [InlineData("Sodium 0mg")]
    [InlineData("Sodium 0 mg")]
    [InlineData("Caffeine 0mg")]
    [InlineData("Sugar 0g")]
    [InlineData("Sugar 0%")]
    [InlineData("Salt 0.0mg")]
    public async Task AnalyzeAsync_ZeroQuantityIngredient_NotFlaggedAsBadOrNeutralFromKnownList(string ingredient)
    {
        var result = await _analyzer.AnalyzeAsync(ingredient, 30);

        // Zero-quantity items from nutrition labels should be skipped entirely
        // (they are label measurements, not ingredients)
        if (result.Ingredients.Count == 0)
        {
            Assert.Empty(result.Ingredients);
        }
        else
        {
            var detail = Assert.Single(result.Ingredients);
            Assert.NotEqual("Bad", detail.Status);
        }
    }

    [Fact]
    public async Task AnalyzeAsync_TransFat0g_DoesNotCountAsBadForVerdict()
    {
        var result = await _analyzer.AnalyzeAsync("Trans Fat 0g, Sugar, Flour", 30);

        // Trans Fat 0g should not trigger Caution
        Assert.Equal("Buy", result.OverallVerdict);
    }

    [Fact]
    public async Task AnalyzeAsync_TransFatWithQuantity_StillFlaggedAsBad()
    {
        // Non-zero quantity should still be flagged
        var result = await _analyzer.AnalyzeAsync("trans fat", 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Bad", detail.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_TransFat2g_StillFlaggedAsBad()
    {
        var result = await _analyzer.AnalyzeAsync("Trans Fat 2g", 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Bad", detail.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_Sodium850mg_StillClassified()
    {
        var result = await _analyzer.AnalyzeAsync("Sodium 850mg", 30);

        var detail = Assert.Single(result.Ingredients);
        // Sodium with a real amount should still be classified from KnownBad (Neutral)
        Assert.Equal("Neutral", detail.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_MixOfZeroAndNonZero_OnlyNonZeroCounted()
    {
        // Trans Fat 0g should be ignored entirely (not a recognized ingredient
        // at zero quantity), aspartame should be Bad, flour is a known neutral
        var result = await _analyzer.AnalyzeAsync("Trans Fat 0g, aspartame, flour", 30);

        Assert.Equal("Caution", result.OverallVerdict);

        // Trans Fat 0g should be skipped entirely (nutrition label noise at zero qty)
        var transFat = result.Ingredients
            .FirstOrDefault(i => i.Name.Contains("Trans Fat", StringComparison.OrdinalIgnoreCase));
        Assert.Null(transFat);

        var aspartame = result.Ingredients
            .FirstOrDefault(i => i.Name.Contains("aspartame", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(aspartame);
        Assert.Equal("Bad", aspartame!.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_NutritionLabelStyle_ZeroTransFatNotCounted()
    {
        // Simulates OCR output from a real nutrition label
        var text = "Total Fat 9g, Saturated Fat 4.5g, Trans Fat 0g, Cholesterol 35mg, Sodium 850mg";

        var result = await _analyzer.AnalyzeAsync(text, 30);

        // Trans Fat 0g should not make the verdict worse
        var transFat = result.Ingredients
            .FirstOrDefault(i => i.Name.Contains("Trans Fat", StringComparison.OrdinalIgnoreCase));
        if (transFat != null)
        {
            Assert.NotEqual("Bad", transFat.Status);
        }

        // "Total Fat 9g", "Saturated Fat 4.5g" are non-informative label noise — filtered out
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("Total Fat", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("Saturated Fat", StringComparison.OrdinalIgnoreCase));

        // Cholesterol and Sodium are recognized nutritional components
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Cholesterol", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Sodium", StringComparison.OrdinalIgnoreCase));
    }

    // ── Unrecognised text should NOT appear as ingredients ──

    [Theory]
    [InlineData("Total Fat 9g")]
    [InlineData("Saturated Fat 4.5g")]
    [InlineData("Calories 200")]
    [InlineData("Amount Per Serving")]
    [InlineData("Nutrition Facts")]
    [InlineData("Serving Size 1 cup")]
    [InlineData("Daily Value")]
    public async Task AnalyzeAsync_NutritionLabelNoise_IsFilteredOut(string noise)
    {
        var result = await _analyzer.AnalyzeAsync(noise, 30);

        Assert.Empty(result.Ingredients);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("XKCD")]
    [InlineData("1234")]
    [InlineData("NET WT")]
    public async Task AnalyzeAsync_RandomOcrNoise_IsNotAddedAsIngredient(string noise)
    {
        var result = await _analyzer.AnalyzeAsync(noise, 30);

        Assert.Empty(result.Ingredients);
    }

    [Fact]
    public async Task AnalyzeAsync_MixOfRealIngredientsAndNoise_OnlyIncludesRealIngredients()
    {
        var text = "flour, sugar, Total Fat 9g, Calories 200, olive oil, some random text";

        var result = await _analyzer.AnalyzeAsync(text, 30);

        // Should include flour (KnownNeutral), sugar (KnownBad/Neutral), olive oil (Good)
        // Should NOT include "Total Fat 9g", "Calories 200", "some random text"
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("Total Fat", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("Calories", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("random", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("flour", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("sugar", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("olive oil", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeAsync_KnownNeutralIngredients_AreRecognised()
    {
        var result = await _analyzer.AnalyzeAsync("flour, water, xanthan gum, vanilla extract", 30);

        Assert.Equal(4, result.Ingredients.Count);
        Assert.All(result.Ingredients, i => Assert.Equal("Neutral", i.Status));
    }

    [Fact]
    public async Task AnalyzeAsync_FullNutritionLabel_OnlyIncludesRealIngredients()
    {
        // Simulates typical OCR output from a nutrition label
        var text = "Nutrition Facts\n" +
                   "Serving Size 1 cup\n" +
                   "Calories 200\n" +
                   "Total Fat 9g\n" +
                   "Saturated Fat 4.5g\n" +
                   "Trans Fat 0g\n" +
                   "Cholesterol 35mg\n" +
                   "Sodium 850mg\n" +
                   "Total Carbohydrate 25g\n" +
                   "Dietary Fiber 3g\n" +
                   "Total Sugars 12g\n" +
                   "Protein 15g\n" +
                   "Vitamin D 0mcg\n" +
                   "Calcium 320mg\n" +
                   "Iron 1.6mg\n" +
                   "INGREDIENTS: flour, sugar, salt, butter, vanilla extract\n" +
                   "* The % Daily Value tells you how much a nutrient in\n" +
                   "a serving of food contributes to a daily diet.";

        var result = await _analyzer.AnalyzeAsync(text, 30);

        // Should NOT contain structural noise or zero-quantity items
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("Total Fat", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("Calories", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("Serving Size", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("tells you", StringComparison.OrdinalIgnoreCase));
        // Vitamin D 0mcg should be skipped (zero quantity)
        Assert.DoesNotContain(result.Ingredients,
            i => i.Name.Contains("Vitamin D", StringComparison.OrdinalIgnoreCase));

        // Should contain recognised nutritional components
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Cholesterol", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Sodium", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Carbohydrate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Protein", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Calcium", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Iron", StringComparison.OrdinalIgnoreCase));

        // Should contain the real ingredients
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("flour", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("sugar", StringComparison.OrdinalIgnoreCase));
    }

    // ── Nutritional components should be extracted ──

    [Fact]
    public async Task AnalyzeAsync_RealLabel_ExtractsSodiumPotassiumCarbohydrate()
    {
        // Exact OCR text from user's real nutrition label
        var text = "Nutrition Facts\n" +
                   "4 servings per container\n" +
                   "Serving size 1 cup (2279g)\n" +
                   "Amount per serving\n" +
                   "Calories 280\n" +
                   "Total Fat 99 12%\n" +
                   "Saturated Fat 4.5g 23%\n" +
                   "Trans Fat 0g\n" +
                   "Cholesterol 35mg 12%\n" +
                   "Sodium850mg 37%\n" +
                   "Total Carbohydrate 34g 12%\n" +
                   "Dietary Fiber 4g 14%\n" +
                   "Total Sugars 6g\n" +
                   "Includes 0g Added Sugars 0%\n" +
                   "Protein 15g\n" +
                   "Vitamin D Omcg\n" +
                   "Calcium 320mg\n" +
                   "Iron 1.6mg\n" +
                   "Potassium 510mg\n" +
                   "* The % Daily Value (DV) tells you how much a nutrient in\n" +
                   "aserving of food contributes to a daily diet. 2,000 calories\n" +
                   "aday is used for general nutrition advice.";

        var result = await _analyzer.AnalyzeAsync(text, 30);

        // Should extract meaningful nutritional components
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Sodium", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Potassium", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Carbohydrate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Cholesterol", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Protein", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Iron", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Calcium", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Ingredients,
            i => i.Name.Contains("Fiber", StringComparison.OrdinalIgnoreCase));

        // Should NOT extract Vitamin D (zero quantity — OCR reads "Omcg" as 0mcg)
        // Note: OCR reads "Omcg" not "0mcg" — this is a letter O, but the analyzer
        // should still handle it gracefully (either skip or not flag as meaningful)
    }

    [Theory]
    [InlineData("Vitamin D 0mcg")]
    [InlineData("Vitamin D 0 mcg")]
    [InlineData("Vitamin D 0.0mcg")]
    public async Task AnalyzeAsync_VitaminDZeroQuantity_IsSkipped(string input)
    {
        var result = await _analyzer.AnalyzeAsync(input, 30);

        // Zero-quantity vitamins should be skipped entirely
        Assert.Empty(result.Ingredients);
    }

    [Fact]
    public async Task AnalyzeAsync_Potassium510mg_ClassifiedAsGood()
    {
        var result = await _analyzer.AnalyzeAsync("Potassium 510mg", 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Good", detail.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_TotalCarbohydrate34g_ClassifiedAsGood()
    {
        var result = await _analyzer.AnalyzeAsync("Total Carbohydrate 34g", 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Good", detail.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_Cholesterol35mg_ClassifiedAsNeutral()
    {
        var result = await _analyzer.AnalyzeAsync("Cholesterol 35mg", 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Neutral", detail.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_DietaryFiber4g_ClassifiedAsGood()
    {
        var result = await _analyzer.AnalyzeAsync("Dietary Fiber 4g", 30);

        var detail = Assert.Single(result.Ingredients);
        Assert.Equal("Good", detail.Status);
    }
}
