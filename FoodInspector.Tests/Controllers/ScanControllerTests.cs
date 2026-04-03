using Xunit;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using FoodInspector.Controllers;
using FoodInspector.Models;
using FoodInspector.Services;
using FoodInspector.Tests.Helpers;

namespace FoodInspector.Tests.Controllers;

public class ScanControllerTests
{
    private readonly Mock<IOcrService> _mockOcr = new();
    private readonly Mock<IIngredientAnalyzer> _mockAnalyzer = new();

    private ScanController CreateController(bool withSession = true)
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);

        if (withSession)
            SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);
        else
            SessionHelper.SetupSession(controller);

        return controller;
    }

    // ── GET Index ──

    [Fact]
    public void Index_NoSession_RedirectsToRegister()
    {
        var controller = CreateController(withSession: false);

        var result = controller.Index();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Register", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    [Fact]
    public void Index_WithSession_ReturnsViewWithViewModel()
    {
        var controller = CreateController();

        var result = controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ScanViewModel>(viewResult.Model);
        Assert.Equal("Alice", model.UserName);
        Assert.Equal(25, model.UserAge);
    }

    // ── POST Analyze ──

    [Fact]
    public async Task Analyze_NoSession_RedirectsToRegister()
    {
        var controller = CreateController(withSession: false);

        var result = await controller.Analyze("somedata");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Register", redirect.ActionName);
    }

    [Fact]
    public async Task Analyze_EmptyImageData_ReturnsIndexWithError()
    {
        var controller = CreateController();

        var result = await controller.Analyze("");

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);
        Assert.Equal("No image received. Please take a photo.", controller.ViewBag.Error);
    }

    [Fact]
    public async Task Analyze_NullImageData_ReturnsIndexWithError()
    {
        var controller = CreateController();

        var result = await controller.Analyze(null!);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);
    }

    [Fact]
    public async Task Analyze_OcrReturnsEmpty_ReturnsIndexWithOcrError()
    {
        var controller = CreateController();

        // Create a valid tiny base64 image (1x1 white PNG)
        var imageBase64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

        _mockOcr.Setup(o => o.ExtractTextAsync(It.IsAny<byte[]>()))
            .ReturnsAsync("");

        var result = await controller.Analyze(imageBase64);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);
        Assert.Contains("Could not read any text", (string)controller.ViewBag.Error);
    }

    [Fact]
    public async Task Analyze_SuccessfulScan_ReturnsResultViewAndPersists()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var imageBase64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

        _mockOcr.Setup(o => o.ExtractTextAsync(It.IsAny<byte[]>()))
            .ReturnsAsync("sugar, flour, salt");

        var expectedAnalysis = new IngredientAnalysis
        {
            OverallVerdict = "Buy",
            Summary = "Looks good",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "sugar", Status = "Neutral", Reason = "OK" },
                new() { Name = "flour", Status = "Neutral", Reason = "OK" },
                new() { Name = "salt", Status = "Neutral", Reason = "OK" }
            }
        };

        _mockAnalyzer.Setup(a => a.AnalyzeAsync("sugar, flour, salt", 25))
            .ReturnsAsync(expectedAnalysis);

        var result = await controller.Analyze(imageBase64);

        // Returns Result view
        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", viewResult.ViewName);

        var model = Assert.IsType<ScanViewModel>(viewResult.Model);
        Assert.Equal("sugar, flour, salt", model.ExtractedText);
        Assert.Equal("Buy", model.Analysis!.OverallVerdict);

        // Persisted to database
        var scanResult = Assert.Single(db.ScanResults);
        Assert.Equal(1, scanResult.UserId);
        Assert.Equal("sugar, flour, salt", scanResult.ExtractedText);
    }

    [Fact]
    public async Task Analyze_Base64WithoutPrefix_StillWorks()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        // Raw base64 (no data:image prefix)
        var rawBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

        _mockOcr.Setup(o => o.ExtractTextAsync(It.IsAny<byte[]>()))
            .ReturnsAsync("flour");

        _mockAnalyzer.Setup(a => a.AnalyzeAsync("flour", 25))
            .ReturnsAsync(new IngredientAnalysis
            {
                OverallVerdict = "Buy",
                Summary = "Safe",
                Ingredients = new List<IngredientDetail>
                {
                    new() { Name = "flour", Status = "Neutral", Reason = "OK" }
                }
            });

        var result = await controller.Analyze(rawBase64);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", viewResult.ViewName);
    }

    [Fact]
    public async Task Analyze_OcrThrowsException_ReturnsIndexWithError()
    {
        var controller = CreateController();

        var imageBase64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

        _mockOcr.Setup(o => o.ExtractTextAsync(It.IsAny<byte[]>()))
            .ThrowsAsync(new InvalidOperationException("OCR engine failed"));

        var result = await controller.Analyze(imageBase64);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", viewResult.ViewName);
        Assert.Contains("Error processing image", (string)controller.ViewBag.Error);
    }

    // ── GET History ──

    [Fact]
    public async Task History_NoSession_RedirectsToRegister()
    {
        var controller = CreateController(withSession: false);

        var result = await controller.History();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Register", redirect.ActionName);
    }

    [Fact]
    public async Task History_WithSession_ReturnsViewWithResults()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        // Seed scan results
        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Buy",
            Summary = "OK",
            Ingredients = new List<IngredientDetail>()
        };

        db.ScanResults.Add(new ScanResult
        {
            UserId = 1,
            ExtractedText = "flour, sugar",
            AnalysisJson = JsonSerializer.Serialize(analysis),
            ScannedAt = DateTime.UtcNow
        });
        db.ScanResults.Add(new ScanResult
        {
            UserId = 1,
            ExtractedText = "salt, pepper",
            AnalysisJson = JsonSerializer.Serialize(analysis),
            ScannedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var result = await controller.History();

        var viewResult = Assert.IsType<ViewResult>(result);
        var models = Assert.IsAssignableFrom<List<ScanViewModel>>(viewResult.Model);
        Assert.Equal(2, models.Count);
    }

    [Fact]
    public async Task History_OnlyShowsCurrentUserResults()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        db.ScanResults.Add(new ScanResult { UserId = 1, ExtractedText = "flour", AnalysisJson = "{}" });
        db.ScanResults.Add(new ScanResult { UserId = 2, ExtractedText = "sugar", AnalysisJson = "{}" }); // different user
        await db.SaveChangesAsync();

        var result = await controller.History();

        var viewResult = Assert.IsType<ViewResult>(result);
        var models = Assert.IsAssignableFrom<List<ScanViewModel>>(viewResult.Model);
        Assert.Single(models);
        Assert.Equal("flour", models[0].ExtractedText);
    }

    [Fact]
    public async Task History_LimitsTo20Results()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        for (int i = 0; i < 25; i++)
        {
            db.ScanResults.Add(new ScanResult
            {
                UserId = 1,
                ExtractedText = $"item {i}",
                AnalysisJson = "{}",
                ScannedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();

        var result = await controller.History();

        var viewResult = Assert.IsType<ViewResult>(result);
        var models = Assert.IsAssignableFrom<List<ScanViewModel>>(viewResult.Model);
        Assert.Equal(20, models.Count);
    }

    // ── POST Reanalyze ──

    [Fact]
    public async Task Reanalyze_NoSession_RedirectsToRegister()
    {
        var controller = CreateController(withSession: false);

        var result = await controller.Reanalyze("sugar, flour", null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Register", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    [Fact]
    public async Task Reanalyze_EmptyText_ReturnsResultViewWithError()
    {
        var controller = CreateController();

        var result = await controller.Reanalyze("", null);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", viewResult.ViewName);
        Assert.Equal("Ingredient text cannot be empty.", (string)controller.ViewBag.Error);
    }

    [Fact]
    public async Task Reanalyze_WhitespaceText_ReturnsResultViewWithError()
    {
        var controller = CreateController();

        var result = await controller.Reanalyze("   ", null);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", viewResult.ViewName);
        Assert.Equal("Ingredient text cannot be empty.", (string)controller.ViewBag.Error);
    }

    [Fact]
    public async Task Reanalyze_NullText_ReturnsResultViewWithError()
    {
        var controller = CreateController();

        var result = await controller.Reanalyze(null!, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", viewResult.ViewName);
    }

    [Fact]
    public async Task Reanalyze_ValidText_ReturnsResultViewWithUpdatedAnalysis()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var updatedText = "sugar, flour, salt";
        var expectedAnalysis = new IngredientAnalysis
        {
            OverallVerdict = "Caution",
            Summary = "Contains sugar",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "sugar", Status = "Bad", Reason = "High sugar" },
                new() { Name = "flour", Status = "Neutral", Reason = "OK" },
                new() { Name = "salt", Status = "Neutral", Reason = "OK" }
            }
        };

        _mockAnalyzer.Setup(a => a.AnalyzeAsync(updatedText, 25))
            .ReturnsAsync(expectedAnalysis);

        var result = await controller.Reanalyze(updatedText, "data:image/png;base64,abc123");

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", viewResult.ViewName);

        var model = Assert.IsType<ScanViewModel>(viewResult.Model);
        Assert.Equal(updatedText, model.ExtractedText);
        Assert.Equal("Caution", model.Analysis!.OverallVerdict);
        Assert.Equal(3, model.Analysis.Ingredients.Count);
        Assert.Equal("data:image/png;base64,abc123", model.ImageBase64);
    }

    [Fact]
    public async Task Reanalyze_ValidText_PersistsToDatabase()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var updatedText = "vitamin c, iron";
        _mockAnalyzer.Setup(a => a.AnalyzeAsync(updatedText, 25))
            .ReturnsAsync(new IngredientAnalysis
            {
                OverallVerdict = "Buy",
                Summary = "Healthy",
                Ingredients = new List<IngredientDetail>
                {
                    new() { Name = "vitamin c", Status = "Good", Reason = "Beneficial" },
                    new() { Name = "iron", Status = "Good", Reason = "Essential mineral" }
                }
            });

        await controller.Reanalyze(updatedText, null);

        var scanResult = Assert.Single(db.ScanResults);
        Assert.Equal(1, scanResult.UserId);
        Assert.Equal("vitamin c, iron", scanResult.ExtractedText);
        Assert.Contains("Buy", scanResult.AnalysisJson);
    }

    [Fact]
    public async Task Reanalyze_PreservesUserInfoInViewModel()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Bob", userAge: 30);

        _mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), 30))
            .ReturnsAsync(new IngredientAnalysis
            {
                OverallVerdict = "Buy",
                Summary = "OK",
                Ingredients = new List<IngredientDetail>()
            });

        var result = await controller.Reanalyze("flour", null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ScanViewModel>(viewResult.Model);
        Assert.Equal("Bob", model.UserName);
        Assert.Equal(30, model.UserAge);
    }

    [Fact]
    public async Task Reanalyze_NullImageData_SetsNullInViewModel()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        _mockAnalyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), 25))
            .ReturnsAsync(new IngredientAnalysis
            {
                OverallVerdict = "Buy",
                Summary = "OK",
                Ingredients = new List<IngredientDetail>()
            });

        var result = await controller.Reanalyze("flour", null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ScanViewModel>(viewResult.Model);
        Assert.Null(model.ImageBase64);
    }

    [Fact]
    public async Task Reanalyze_UsesUserAgeForAnalysis()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new ScanController(_mockOcr.Object, _mockAnalyzer.Object, db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Child", userAge: 8);

        _mockAnalyzer.Setup(a => a.AnalyzeAsync("caffeine", 8))
            .ReturnsAsync(new IngredientAnalysis
            {
                OverallVerdict = "Avoid",
                Summary = "Not suitable for children",
                Ingredients = new List<IngredientDetail>
                {
                    new() { Name = "caffeine", Status = "Bad", Reason = "Not for children under 12" }
                }
            });

        var result = await controller.Reanalyze("caffeine", null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ScanViewModel>(viewResult.Model);
        Assert.Equal("Avoid", model.Analysis!.OverallVerdict);

        _mockAnalyzer.Verify(a => a.AnalyzeAsync("caffeine", 8), Times.Once);
    }
}
