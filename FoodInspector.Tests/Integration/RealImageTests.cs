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
/// Integration tests that use real food label photographs from the Images folder.
/// Each test loads an actual image, runs Tesseract OCR, and verifies extraction and analysis.
/// </summary>
public class RealImageTests : IDisposable
{
    private readonly TesseractOcrService _ocrService;
    private readonly OpenAiIngredientAnalyzer _analyzer;
    private readonly string _imagesDir;

    public RealImageTests()
    {
        var testOutputDir = AppContext.BaseDirectory;
        var solutionDir = Path.GetFullPath(Path.Combine(testOutputDir, "..", "..", "..", ".."));
        var projectDir = Path.Combine(solutionDir, "FoodInspector");

        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(projectDir);

        _ocrService = new TesseractOcrService(mockEnv.Object);

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

        _imagesDir = Path.Combine(testOutputDir, "Images");
    }

    private byte[] LoadImage(string fileName)
    {
        var path = Path.Combine(_imagesDir, fileName);
        Assert.True(File.Exists(path), $"Test image not found: {path}");
        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// Converts a JPEG (or any image) to PNG bytes via System.Drawing.
    /// Tesseract's Pix.LoadFromMemory works more reliably with PNG.
    /// </summary>
    private static byte[] ConvertToPngBytes(byte[] imageBytes)
    {
        using var ms = new MemoryStream(imageBytes);
        using var img = Image.FromStream(ms);
        using var output = new MemoryStream();
        img.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Image 1: n01009-nutrition-label-440.png
    //  A clear nutrition facts label — OCR reads it very well
    // ═══════════════════════════════════════════════════════════════════

    private const string NutritionLabel = "n01009-nutrition-label-440.png";

    [Fact]
    public async Task NutritionLabel_OcrExtractsReadableText()
    {
        var imageBytes = LoadImage(NutritionLabel);

        var text = await _ocrService.ExtractTextAsync(imageBytes);

        Assert.False(string.IsNullOrWhiteSpace(text),
            "OCR should extract text from the nutrition label image");
        Assert.True(text.Length > 100,
            $"Expected substantial text but got only {text.Length} chars");
    }

    [Fact]
    public async Task NutritionLabel_OcrDetectsSodium()
    {
        var imageBytes = LoadImage(NutritionLabel);

        var text = await _ocrService.ExtractTextAsync(imageBytes);

        Assert.Contains("Sodium", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NutritionLabel_OcrDetectsTransFat()
    {
        var imageBytes = LoadImage(NutritionLabel);

        var text = await _ocrService.ExtractTextAsync(imageBytes);

        Assert.Contains("Trans Fat", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NutritionLabel_OcrDetectsCalciumAndIron()
    {
        var imageBytes = LoadImage(NutritionLabel);

        var text = await _ocrService.ExtractTextAsync(imageBytes);

        Assert.Contains("Calcium", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Iron", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NutritionLabel_OcrDetectsVitaminD()
    {
        var imageBytes = LoadImage(NutritionLabel);

        var text = await _ocrService.ExtractTextAsync(imageBytes);

        Assert.Contains("Vitamin D", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NutritionLabel_OcrDetectsProteinAndFiber()
    {
        var imageBytes = LoadImage(NutritionLabel);

        var text = await _ocrService.ExtractTextAsync(imageBytes);

        Assert.Contains("Protein", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fiber", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NutritionLabel_AnalysisForAdult_DetectsNutrients()
    {
        var imageBytes = LoadImage(NutritionLabel);
        var text = await _ocrService.ExtractTextAsync(imageBytes);

        var analysis = await _analyzer.AnalyzeAsync(text, 30);

        Assert.NotNull(analysis);
        Assert.NotEmpty(analysis.OverallVerdict);
        Assert.NotEmpty(analysis.Summary);
        Assert.True(analysis.Ingredients.Count >= 3,
            $"Expected at least 3 ingredients from a detailed label. Got {analysis.Ingredients.Count}");
        Assert.Contains("adult", analysis.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NutritionLabel_AnalysisDetectsGoodIngredients()
    {
        var imageBytes = LoadImage(NutritionLabel);
        var text = await _ocrService.ExtractTextAsync(imageBytes);

        var analysis = await _analyzer.AnalyzeAsync(text, 30);

        // Calcium, Iron, Vitamin, Protein, Fiber are all "Good" keywords
        var goodIngredients = analysis.Ingredients.Where(i => i.Status == "Good").ToList();
        Assert.True(goodIngredients.Count >= 2,
            $"Expected at least 2 good ingredients from a nutrition label. Found: {string.Join(", ", goodIngredients.Select(i => i.Name))}");
    }

    [Fact]
    public async Task NutritionLabel_AnalysisDetectsSodiumAsNeutral()
    {
        var imageBytes = LoadImage(NutritionLabel);
        var text = await _ocrService.ExtractTextAsync(imageBytes);

        var analysis = await _analyzer.AnalyzeAsync(text, 30);

        // Sodium is classified as Neutral in the rule engine
        var sodium = analysis.Ingredients
            .FirstOrDefault(i => i.Name.Contains("Sodium", StringComparison.OrdinalIgnoreCase));
        if (sodium != null)
        {
            Assert.Equal("Neutral", sodium.Status);
            Assert.Contains("blood pressure", sodium.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task NutritionLabel_AnalysisForElderly_WarnsSodium()
    {
        var imageBytes = LoadImage(NutritionLabel);
        var text = await _ocrService.ExtractTextAsync(imageBytes);

        var analysis = await _analyzer.AnalyzeAsync(text, 72);

        Assert.Contains("over 60", analysis.Summary, StringComparison.OrdinalIgnoreCase);

        var sodium = analysis.Ingredients
            .FirstOrDefault(i => i.Name.Contains("Sodium", StringComparison.OrdinalIgnoreCase));
        if (sodium != null)
        {
            Assert.Contains("over 60", sodium.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task NutritionLabel_AnalysisForChild_MentionsChild()
    {
        var imageBytes = LoadImage(NutritionLabel);
        var text = await _ocrService.ExtractTextAsync(imageBytes);

        var analysis = await _analyzer.AnalyzeAsync(text, 9);

        Assert.Contains("child", analysis.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NutritionLabel_StructuredResultIsFullyPopulated()
    {
        var imageBytes = LoadImage(NutritionLabel);
        var text = await _ocrService.ExtractTextAsync(imageBytes);

        var analysis = await _analyzer.AnalyzeAsync(text, 35);

        Assert.True(analysis.OverallVerdict is "Buy" or "Caution" or "Avoid");

        foreach (var ingredient in analysis.Ingredients)
        {
            Assert.False(string.IsNullOrWhiteSpace(ingredient.Name));
            Assert.True(ingredient.Status is "Good" or "Neutral" or "Bad",
                $"Unexpected status '{ingredient.Status}' for '{ingredient.Name}'");
            Assert.False(string.IsNullOrWhiteSpace(ingredient.Reason));
        }
    }

    [Fact]
    public async Task NutritionLabel_DifferentAges_ProduceDifferentSummaries()
    {
        var imageBytes = LoadImage(NutritionLabel);
        var text = await _ocrService.ExtractTextAsync(imageBytes);

        var childResult = await _analyzer.AnalyzeAsync(text, 10);
        var teenResult = await _analyzer.AnalyzeAsync(text, 16);
        var adultResult = await _analyzer.AnalyzeAsync(text, 35);
        var elderlyResult = await _analyzer.AnalyzeAsync(text, 75);

        Assert.Contains("child", childResult.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("teenager", teenResult.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adult", adultResult.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("over 60", elderlyResult.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Image 2: labelling-on-morrisons-own-label-tinned-baked-beans-...jpg
    //  A real-world product photo (complex background, angles, etc.)
    // ═══════════════════════════════════════════════════════════════════

    private const string BakedBeansLabel =
        "labelling-on-morrisons-own-label-tinned-baked-beans-for-food-ingredients-labels-nutrition-labelling-food-facts-allergy-advice-food-packaging-2EA1TMR.jpg";

    [Fact]
    public async Task BakedBeansPhoto_LoadsWithoutException()
    {
        var imageBytes = LoadImage(BakedBeansLabel);
        Assert.True(imageBytes.Length > 0);

        // Convert JPEG to PNG for better Tesseract compatibility
        var pngBytes = ConvertToPngBytes(imageBytes);
        Assert.True(pngBytes.Length > 0);

        // Should not throw even if text extraction is poor
        var text = await _ocrService.ExtractTextAsync(pngBytes);
        Assert.NotNull(text);
    }

    [Fact]
    public async Task BakedBeansPhoto_AnalysisHandlesLowQualityOcr()
    {
        var imageBytes = LoadImage(BakedBeansLabel);
        var pngBytes = ConvertToPngBytes(imageBytes);
        var text = await _ocrService.ExtractTextAsync(pngBytes);

        // Even with poor/no OCR text, the analyzer should not crash
        var analysis = await _analyzer.AnalyzeAsync(text, 30);

        Assert.NotNull(analysis);
        Assert.True(analysis.OverallVerdict is "Buy" or "Caution" or "Avoid");
        Assert.NotEmpty(analysis.Summary);
    }

    [Fact]
    public async Task BakedBeansPhoto_PipelineIsResilientToComplexPhoto()
    {
        var imageBytes = LoadImage(BakedBeansLabel);
        var pngBytes = ConvertToPngBytes(imageBytes);

        // Full pipeline should complete without exceptions
        var text = await _ocrService.ExtractTextAsync(pngBytes);
        var analysis = await _analyzer.AnalyzeAsync(text, 45);

        // With a complex photo, result is valid even if sparse
        Assert.NotNull(analysis);
        Assert.Contains("adult", analysis.Summary, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Image 3: images.png
    //  A small thumbnail (183×275) — tests how pipeline handles
    //  low-resolution images that produce little or no OCR text
    // ═══════════════════════════════════════════════════════════════════

    private const string SmallThumbnail = "images.png";

    [Fact]
    public async Task SmallThumbnail_LoadsWithoutException()
    {
        var imageBytes = LoadImage(SmallThumbnail);
        Assert.True(imageBytes.Length > 0);

        // Should not throw on a small image
        var text = await _ocrService.ExtractTextAsync(imageBytes);
        Assert.NotNull(text);
    }

    [Fact]
    public async Task SmallThumbnail_AnalysisHandlesMinimalText()
    {
        var imageBytes = LoadImage(SmallThumbnail);
        var text = await _ocrService.ExtractTextAsync(imageBytes);

        var analysis = await _analyzer.AnalyzeAsync(text, 25);

        Assert.NotNull(analysis);
        Assert.True(analysis.OverallVerdict is "Buy" or "Caution" or "Avoid");
        Assert.NotEmpty(analysis.Summary);
    }

    [Fact]
    public async Task SmallThumbnail_PipelineDoesNotCrashOnTinyImage()
    {
        var imageBytes = LoadImage(SmallThumbnail);
        var text = await _ocrService.ExtractTextAsync(imageBytes);

        // Even with minimal/no text, full pipeline completes gracefully
        var analysis = await _analyzer.AnalyzeAsync(text, 40);

        Assert.NotNull(analysis);
        Assert.NotNull(analysis.Ingredients);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Cross-image comparison tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AllThreeImages_ProduceValidAnalysisResults()
    {
        var files = new[] { NutritionLabel, BakedBeansLabel, SmallThumbnail };

        foreach (var file in files)
        {
            var imageBytes = LoadImage(file);

            // Convert JPEGs for compatibility
            byte[] processedBytes;
            if (file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                processedBytes = ConvertToPngBytes(imageBytes);
            else
                processedBytes = imageBytes;

            var text = await _ocrService.ExtractTextAsync(processedBytes);
            var analysis = await _analyzer.AnalyzeAsync(text, 30);

            Assert.NotNull(analysis);
            Assert.True(analysis.OverallVerdict is "Buy" or "Caution" or "Avoid",
                $"Invalid verdict for {file}: {analysis.OverallVerdict}");
            Assert.NotEmpty(analysis.Summary);
        }
    }

    [Fact]
    public async Task NutritionLabel_ExtractsMoreTextThanOtherImages()
    {
        // The clear nutrition label should produce far more OCR text
        // than the thumbnail or the complex photo
        var nutritionBytes = LoadImage(NutritionLabel);
        var thumbnailBytes = LoadImage(SmallThumbnail);

        var nutritionText = await _ocrService.ExtractTextAsync(nutritionBytes);
        var thumbnailText = await _ocrService.ExtractTextAsync(thumbnailBytes);

        Assert.True(nutritionText.Length > thumbnailText.Length,
            $"Nutrition label ({nutritionText.Length} chars) should produce more text than thumbnail ({thumbnailText.Length} chars)");
    }

    public void Dispose()
    {
        _ocrService.Dispose();
    }
}
