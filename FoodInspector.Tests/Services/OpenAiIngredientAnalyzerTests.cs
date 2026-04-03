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

        var detail = Assert.Single(result.Ingredients);
        // A zero-quantity ingredient should be treated as Neutral (unknown),
        // not classified via the KnownBad dictionary
        Assert.NotEqual("Bad", detail.Status);
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
        // Trans Fat 0g should be ignored, but aspartame (no quantity) should be Bad
        var result = await _analyzer.AnalyzeAsync("Trans Fat 0g, aspartame, flour", 30);

        Assert.Equal("Caution", result.OverallVerdict);

        var transFat = result.Ingredients
            .FirstOrDefault(i => i.Name.Contains("Trans Fat", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(transFat);
        Assert.NotEqual("Bad", transFat!.Status);

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
    }
}
