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

    public void Dispose()
    {
        _ocrService.Dispose();
    }
}
