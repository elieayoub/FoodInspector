using System.Drawing;
using System.Drawing.Imaging;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using FoodInspector.Services;

namespace FoodInspector.Tests.Integration;

/// <summary>
/// Integration tests that generate images with ingredient text, run OCR to extract it,
/// and feed the results through the ingredient analyzer to verify the full pipeline.
/// </summary>
public class OcrToAnalysisPipelineTests : IDisposable
{
    private readonly TesseractOcrService _ocrService;
    private readonly OpenAiIngredientAnalyzer _analyzer;

    public OcrToAnalysisPipelineTests()
    {
        // Locate the tessdata folder in the main project's source directory
        var testOutputDir = AppContext.BaseDirectory; // .../FoodInspector.Tests/bin/Debug/net8.0/
        var solutionDir = Path.GetFullPath(Path.Combine(testOutputDir, "..", "..", "..", ".."));
        var projectDir = Path.Combine(solutionDir, "FoodInspector");

        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(projectDir);

        _ocrService = new TesseractOcrService(mockEnv.Object);

        // No API key → forces the built-in fallback rule engine
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = ""
            })
            .Build();

        _analyzer = new OpenAiIngredientAnalyzer(
            config,
            new Mock<IHttpClientFactory>().Object,
            new Mock<ILogger<OpenAiIngredientAnalyzer>>().Object);
    }

    /// <summary>
    /// Renders the given text onto a white bitmap and returns PNG bytes.
    /// Uses large, clear text to ensure reliable OCR recognition.
    /// </summary>
    private static byte[] GenerateTextImage(string text)
    {
        using var bitmap = new Bitmap(900, 300);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.White);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        using var font = new Font("Arial", 28, FontStyle.Regular);
        using var brush = new SolidBrush(Color.Black);
        graphics.DrawString(text, font, brush, new RectangleF(30, 30, 840, 240));

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    // ── OCR extraction tests ──

    [Fact]
    public async Task Ocr_ExtractsKnownIngredientsFromImage()
    {
        // Arrange: generate an image with a clear ingredient list
        var imageBytes = GenerateTextImage("Sugar, Salt, Flour, Honey");

        // Act
        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);

        // Assert: OCR should recognise the key words
        Assert.False(string.IsNullOrWhiteSpace(extractedText),
            "OCR should extract text from the generated image");
        Assert.Contains("Sugar", extractedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Salt", extractedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Flour", extractedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Honey", extractedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ocr_ExtractsBadIngredientsFromImage()
    {
        var imageBytes = GenerateTextImage("Aspartame, Red 40, Sodium Nitrite");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);

        Assert.False(string.IsNullOrWhiteSpace(extractedText));
        Assert.Contains("Aspartame", extractedText, StringComparison.OrdinalIgnoreCase);
    }

    // ── Full pipeline tests (Image → OCR → Analysis) ──

    [Fact]
    public async Task Pipeline_SafeIngredients_ReturnsBuyVerdict()
    {
        // Arrange: image with only safe/neutral ingredients
        var imageBytes = GenerateTextImage("Sugar, Salt, Flour, Honey");

        // Act: OCR
        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        // Act: Analysis
        var analysis = await _analyzer.AnalyzeAsync(extractedText, 30);

        // Assert
        Assert.NotNull(analysis);
        Assert.NotEmpty(analysis.OverallVerdict);
        Assert.NotEmpty(analysis.Summary);
        Assert.True(analysis.Ingredients.Count >= 2,
            $"Expected at least 2 ingredients but got {analysis.Ingredients.Count}. Extracted text: \"{extractedText}\"");

        // No known-bad ingredients → verdict should not be "Avoid"
        Assert.NotEqual("Avoid", analysis.OverallVerdict);
    }

    [Fact]
    public async Task Pipeline_MixedIngredients_DetectsBadOnes()
    {
        // Arrange: image with a mix of good, neutral, and bad ingredients
        var imageBytes = GenerateTextImage("Olive Oil, Sugar, Aspartame");

        // Act: OCR → Analysis
        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 30);

        // Assert: should have identified multiple ingredients
        Assert.NotNull(analysis);
        Assert.True(analysis.Ingredients.Count >= 2,
            $"Expected at least 2 ingredients. Extracted text: \"{extractedText}\"");

        // If OCR read "Aspartame" correctly, the analysis should flag it
        if (extractedText.Contains("Aspartame", StringComparison.OrdinalIgnoreCase))
        {
            var bad = analysis.Ingredients
                .FirstOrDefault(i => i.Name.Contains("Aspartame", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(bad);
            Assert.Equal("Bad", bad!.Status);

            // With at least one bad ingredient, verdict should be "Caution" or "Avoid"
            Assert.NotEqual("Buy", analysis.OverallVerdict);
        }
    }

    [Fact]
    public async Task Pipeline_ChildAge_IncludesAgeSpecificWarnings()
    {
        var imageBytes = GenerateTextImage("Aspartame, Sugar, Flour");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        // Analyze for a child (age 8)
        var analysis = await _analyzer.AnalyzeAsync(extractedText, 8);

        Assert.NotNull(analysis);

        // Summary should mention child-related advice
        Assert.Contains("child", analysis.Summary, StringComparison.OrdinalIgnoreCase);

        // If aspartame was read, it should have the child-specific warning
        if (extractedText.Contains("Aspartame", StringComparison.OrdinalIgnoreCase))
        {
            var aspartame = analysis.Ingredients
                .FirstOrDefault(i => i.Name.Contains("Aspartame", StringComparison.OrdinalIgnoreCase));
            if (aspartame != null)
            {
                Assert.Contains("children", aspartame.Reason, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Pipeline_AnalysisReturnsStructuredResult()
    {
        // Arrange: simple clear ingredient list
        var imageBytes = GenerateTextImage("Salt, Sugar, Flour");

        // Act
        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 25);

        // Assert: the result structure is fully populated
        Assert.NotNull(analysis);
        Assert.True(analysis.OverallVerdict is "Buy" or "Caution" or "Avoid",
            $"Unexpected verdict: {analysis.OverallVerdict}");
        Assert.NotEmpty(analysis.Summary);

        foreach (var ingredient in analysis.Ingredients)
        {
            Assert.False(string.IsNullOrWhiteSpace(ingredient.Name), "Ingredient name should not be empty");
            Assert.True(ingredient.Status is "Good" or "Neutral" or "Bad",
                $"Unexpected status '{ingredient.Status}' for ingredient '{ingredient.Name}'");
            Assert.False(string.IsNullOrWhiteSpace(ingredient.Reason), "Ingredient reason should not be empty");
        }
    }

    // ── Different ingredient pictures ──

    [Fact]
    public async Task Pipeline_PreservativesLabel_DetectsMultipleBadAndReturnsAvoid()
    {
        // A processed-food label loaded with preservatives
        var imageBytes = GenerateTextImage(
            "BHT, Sodium Nitrate, Potassium Bromate, Salt");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 35);

        // 3 known-bad ingredients → should be "Avoid"
        Assert.Equal("Avoid", analysis.OverallVerdict);
        Assert.Contains("concerning", analysis.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(analysis.Ingredients.Count(i => i.Status == "Bad") >= 3,
            $"Expected at least 3 bad ingredients. Extracted: \"{extractedText}\"");
    }

    [Fact]
    public async Task Pipeline_ArtificialDyesLabel_FlagsDyesAsBad()
    {
        var imageBytes = GenerateTextImage("Red 40, Yellow 5, Yellow 6, Sugar");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 30);

        Assert.Equal("Avoid", analysis.OverallVerdict);

        // Each dye that was OCR'd should be flagged as Bad
        foreach (var dye in new[] { "Red 40", "Yellow 5", "Yellow 6" })
        {
            if (extractedText.Contains(dye, StringComparison.OrdinalIgnoreCase))
            {
                var match = analysis.Ingredients
                    .FirstOrDefault(i => i.Name.Contains(dye, StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(match);
                Assert.Equal("Bad", match!.Status);
            }
        }
    }

    [Fact]
    public async Task Pipeline_HealthFoodLabel_AllGoodIngredients()
    {
        // A healthy granola-style ingredient list
        var imageBytes = GenerateTextImage(
            "Oats, Honey, Flaxseed, Quinoa, Coconut Oil");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 30);

        Assert.Equal("Buy", analysis.OverallVerdict);
        Assert.DoesNotContain(analysis.Ingredients, i => i.Status == "Bad");

        // Most should be classified as Good
        var goodCount = analysis.Ingredients.Count(i => i.Status == "Good");
        Assert.True(goodCount >= 2,
            $"Expected at least 2 good ingredients. Extracted: \"{extractedText}\"");
    }

    [Fact]
    public async Task Pipeline_SodaDrinkLabel_CaffeineFlaggedForChild()
    {
        var imageBytes = GenerateTextImage("Water, Sugar, Caffeine, Citric Acid");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        // Analyze for a 7-year-old
        var analysis = await _analyzer.AnalyzeAsync(extractedText, 7);

        Assert.Contains("child", analysis.Summary, StringComparison.OrdinalIgnoreCase);

        if (extractedText.Contains("Caffeine", StringComparison.OrdinalIgnoreCase))
        {
            var caffeine = analysis.Ingredients
                .FirstOrDefault(i => i.Name.Contains("Caffeine", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(caffeine);
            Assert.Contains("children under 12", caffeine!.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Pipeline_SodaDrinkLabel_CaffeineFlaggedForElderly()
    {
        var imageBytes = GenerateTextImage("Water, Sugar, Caffeine, Citric Acid");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        // Analyze for a 75-year-old
        var analysis = await _analyzer.AnalyzeAsync(extractedText, 75);

        Assert.Contains("over 60", analysis.Summary, StringComparison.OrdinalIgnoreCase);

        if (extractedText.Contains("Caffeine", StringComparison.OrdinalIgnoreCase))
        {
            var caffeine = analysis.Ingredients
                .FirstOrDefault(i => i.Name.Contains("Caffeine", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(caffeine);
            Assert.Contains("over 60", caffeine!.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Pipeline_SaltySoupLabel_SodiumFlaggedForElderly()
    {
        var imageBytes = GenerateTextImage(
            "Water, Salt, Sodium, Onion Powder, Garlic");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        // Elderly user
        var analysis = await _analyzer.AnalyzeAsync(extractedText, 68);

        Assert.Contains("over 60", analysis.Summary, StringComparison.OrdinalIgnoreCase);

        // Salt or sodium should carry the elderly-specific warning
        var saltOrSodium = analysis.Ingredients
            .FirstOrDefault(i =>
                i.Name.Contains("Salt", StringComparison.OrdinalIgnoreCase) ||
                i.Name.Contains("Sodium", StringComparison.OrdinalIgnoreCase));
        if (saltOrSodium != null)
        {
            Assert.Contains("over 60", saltOrSodium.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Pipeline_TeenagerAge_SummaryMentionsTeenager()
    {
        var imageBytes = GenerateTextImage("Flour, Sugar, Palm Oil, Salt");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 15);

        Assert.Contains("teenager", analysis.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pipeline_TransFatLabel_DetectsHeartDiseaseRisk()
    {
        var imageBytes = GenerateTextImage(
            "Flour, Trans Fat, Palm Oil, Sugar");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 45);

        if (extractedText.Contains("Trans", StringComparison.OrdinalIgnoreCase))
        {
            var transFat = analysis.Ingredients
                .FirstOrDefault(i => i.Name.Contains("Trans", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(transFat);
            Assert.Equal("Bad", transFat!.Status);
            Assert.Contains("heart disease", transFat.Reason, StringComparison.OrdinalIgnoreCase);
        }

        Assert.NotEqual("Buy", analysis.OverallVerdict);
    }

    [Fact]
    public async Task Pipeline_VitaminEnrichedCereal_DetectsGoodIngredients()
    {
        var imageBytes = GenerateTextImage(
            "Whole Grain, Iron, Calcium, Vitamin D, Fiber");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 30);

        Assert.Equal("Buy", analysis.OverallVerdict);
        Assert.DoesNotContain(analysis.Ingredients, i => i.Status == "Bad");

        // Most recognized ingredients should be "Good"
        var goodCount = analysis.Ingredients.Count(i => i.Status == "Good");
        Assert.True(goodCount >= 3,
            $"Expected at least 3 good ingredients. Extracted: \"{extractedText}\"");
    }

    [Fact]
    public async Task Pipeline_ProcessedMeatLabel_NitriteAndNitrateFlagged()
    {
        var imageBytes = GenerateTextImage(
            "Pork, Water, Salt, Sodium Nitrite, Sodium Nitrate");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 40);

        // Both nitrite and nitrate should be flagged
        var cancerIngredients = analysis.Ingredients
            .Where(i => i.Status == "Bad" &&
                        i.Reason.Contains("cancer", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (extractedText.Contains("Nitri", StringComparison.OrdinalIgnoreCase))
        {
            Assert.NotEmpty(cancerIngredients);
        }
    }

    [Fact]
    public async Task Pipeline_EnergyDrinkLabel_MultipleBadForChild()
    {
        var imageBytes = GenerateTextImage(
            "Water, Sugar, Caffeine, Aspartame, Taurine");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        // Analyze for a 10-year-old
        var analysis = await _analyzer.AnalyzeAsync(extractedText, 10);

        Assert.Contains("child", analysis.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("Buy", analysis.OverallVerdict);

        // Each known-bad ingredient that was OCR'd should get the child warning
        foreach (var keyword in new[] { "Caffeine", "Aspartame" })
        {
            if (extractedText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                var match = analysis.Ingredients
                    .FirstOrDefault(i => i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    Assert.Contains("children", match.Reason, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public async Task Pipeline_BakedGoodsLabel_NeutralIngredients()
    {
        var imageBytes = GenerateTextImage(
            "Flour, Eggs, Butter, Vanilla, Baking Soda");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 30);

        // Common baking ingredients are all unknown → Neutral
        Assert.Equal("Buy", analysis.OverallVerdict);
        Assert.DoesNotContain(analysis.Ingredients, i => i.Status == "Bad");
    }

    [Fact]
    public async Task Pipeline_MultilineIngredientList_ParsesAllLines()
    {
        // Simulate a multi-line label using the taller image variant
        var imageBytes = GenerateMultilineTextImage(
            "Oats, Honey, Chia\n" +
            "Protein, Calcium, Fiber");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var analysis = await _analyzer.AnalyzeAsync(extractedText, 25);

        Assert.Equal("Buy", analysis.OverallVerdict);

        // Should find ingredients from both lines
        Assert.True(analysis.Ingredients.Count >= 4,
            $"Expected at least 4 ingredients from multi-line image. Extracted: \"{extractedText}\"");
    }

    [Fact]
    public async Task Pipeline_CandyBarLabel_AvoidForChild()
    {
        var imageBytes = GenerateTextImage(
            "Sugar, Red 40, Yellow 5, Aspartame");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        // Child analysis
        var analysis = await _analyzer.AnalyzeAsync(extractedText, 6);

        Assert.Contains("child", analysis.Summary, StringComparison.OrdinalIgnoreCase);

        // Should be Avoid (≥3 bad if dyes and aspartame are read)
        var badCount = analysis.Ingredients.Count(i => i.Status == "Bad");
        if (badCount >= 3)
        {
            Assert.Equal("Avoid", analysis.OverallVerdict);
        }
        else
        {
            Assert.NotEqual("Buy", analysis.OverallVerdict);
        }
    }

    [Fact]
    public async Task Pipeline_SameImageDifferentAges_DifferentSummaries()
    {
        var imageBytes = GenerateTextImage("Sugar, Salt, Caffeine, Flour");

        var extractedText = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.False(string.IsNullOrWhiteSpace(extractedText));

        var childAnalysis = await _analyzer.AnalyzeAsync(extractedText, 8);
        var adultAnalysis = await _analyzer.AnalyzeAsync(extractedText, 35);
        var elderlyAnalysis = await _analyzer.AnalyzeAsync(extractedText, 70);

        // Each age group should get a different tailored summary
        Assert.Contains("child", childAnalysis.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adult", adultAnalysis.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("over 60", elderlyAnalysis.Summary, StringComparison.OrdinalIgnoreCase);

        // All three should produce the same verdict (same ingredients)
        Assert.Equal(childAnalysis.OverallVerdict, adultAnalysis.OverallVerdict);
        Assert.Equal(adultAnalysis.OverallVerdict, elderlyAnalysis.OverallVerdict);
    }

    // ── Helper for taller multi-line images ──

    private static byte[] GenerateMultilineTextImage(string text)
    {
        using var bitmap = new Bitmap(900, 500);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.White);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        using var font = new Font("Arial", 28, FontStyle.Regular);
        using var brush = new SolidBrush(Color.Black);
        graphics.DrawString(text, font, brush, new RectangleF(30, 30, 840, 440));

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public void Dispose()
    {
        _ocrService.Dispose();
    }
}
