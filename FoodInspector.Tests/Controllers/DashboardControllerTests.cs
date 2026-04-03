using Xunit;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using FoodInspector.Controllers;
using FoodInspector.Models;
using FoodInspector.Services;
using FoodInspector.Tests.Helpers;

namespace FoodInspector.Tests.Controllers;

public class DashboardControllerTests
{
    private readonly IDailyIntakeService _intakeService = new DailyIntakeService();

    private DashboardController CreateController(bool withSession = true)
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new DashboardController(db, _intakeService);

        if (withSession)
            SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);
        else
            SessionHelper.SetupSession(controller);

        return controller;
    }

    // ── GET Today ──

    [Fact]
    public async Task Today_NoSession_RedirectsToLogin()
    {
        var controller = CreateController(withSession: false);

        var result = await controller.Today();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    [Fact]
    public async Task Today_WithSession_NoLogs_ReturnsHealthyView()
    {
        var controller = CreateController();

        var result = await controller.Today();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.True(model.DailySummary.IsHealthy);
        Assert.Empty(model.TodayFoodLogs);
        Assert.Equal("Alice", model.UserName);
        Assert.Equal(25, model.UserAge);
    }

    [Fact]
    public async Task Today_WithLogs_ShowsTodaysFoodOnly()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new DashboardController(db, _intakeService);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Buy",
            Summary = "OK",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "flour", Status = "Neutral", Reason = "OK" }
            }
        };
        var json = JsonSerializer.Serialize(analysis);

        // Add a scan result for the food log to reference
        var scan = new ScanResult { UserId = 1, ExtractedText = "flour", AnalysisJson = json };
        db.ScanResults.Add(scan);
        await db.SaveChangesAsync();

        // Today's log
        db.FoodLogs.Add(new FoodLog
        {
            UserId = 1,
            ScanResultId = scan.Id,
            ExtractedText = "flour",
            AnalysisJson = json,
            EatenAt = DateTime.UtcNow
        });

        // Yesterday's log (should NOT show)
        db.FoodLogs.Add(new FoodLog
        {
            UserId = 1,
            ScanResultId = scan.Id,
            ExtractedText = "sugar",
            AnalysisJson = json,
            EatenAt = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var result = await controller.Today();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Single(model.TodayFoodLogs);
        Assert.Equal("flour", model.TodayFoodLogs[0].ExtractedText);
    }

    [Fact]
    public async Task Today_WithBadIngredients_ShowsAlerts()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new DashboardController(db, _intakeService);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 8); // child

        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Avoid",
            Summary = "Bad for children",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "caffeine", Status = "Bad", Reason = "Not for children" }
            }
        };
        var json = JsonSerializer.Serialize(analysis);

        var scan = new ScanResult { UserId = 1, ExtractedText = "caffeine", AnalysisJson = json };
        db.ScanResults.Add(scan);
        await db.SaveChangesAsync();

        db.FoodLogs.Add(new FoodLog
        {
            UserId = 1,
            ScanResultId = scan.Id,
            ExtractedText = "caffeine",
            AnalysisJson = json,
            EatenAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Today();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.False(model.DailySummary.IsHealthy);
        Assert.NotEmpty(model.DailySummary.Alerts);
    }

    [Fact]
    public async Task Today_OnlyShowsCurrentUserLogs()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new DashboardController(db, _intakeService);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Buy",
            Summary = "OK",
            Ingredients = new List<IngredientDetail>()
        };
        var json = JsonSerializer.Serialize(analysis);

        var scan1 = new ScanResult { UserId = 1, ExtractedText = "flour", AnalysisJson = json };
        var scan2 = new ScanResult { UserId = 2, ExtractedText = "sugar", AnalysisJson = json };
        db.ScanResults.AddRange(scan1, scan2);
        await db.SaveChangesAsync();

        db.FoodLogs.Add(new FoodLog { UserId = 1, ScanResultId = scan1.Id, ExtractedText = "flour", AnalysisJson = json, EatenAt = DateTime.UtcNow });
        db.FoodLogs.Add(new FoodLog { UserId = 2, ScanResultId = scan2.Id, ExtractedText = "sugar", AnalysisJson = json, EatenAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await controller.Today();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Single(model.TodayFoodLogs);
    }

    // ── GET HealthHistory ──

    [Fact]
    public async Task HealthHistory_NoSession_RedirectsToLogin()
    {
        var controller = CreateController(withSession: false);

        var result = await controller.HealthHistory();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
    }

    [Fact]
    public async Task HealthHistory_NoLogs_ReturnsEmptyHistory()
    {
        var controller = CreateController();

        var result = await controller.HealthHistory();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Empty(model.HealthHistory);
    }

    [Fact]
    public async Task HealthHistory_MultipleDays_GroupsByDate()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new DashboardController(db, _intakeService);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Buy",
            Summary = "OK",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "flour", Status = "Neutral", Reason = "OK" }
            }
        };
        var json = JsonSerializer.Serialize(analysis);

        var scan = new ScanResult { UserId = 1, ExtractedText = "flour", AnalysisJson = json };
        db.ScanResults.Add(scan);
        await db.SaveChangesAsync();

        // Day 1
        db.FoodLogs.Add(new FoodLog { UserId = 1, ScanResultId = scan.Id, ExtractedText = "flour", AnalysisJson = json, EatenAt = DateTime.UtcNow });
        // Day 2
        db.FoodLogs.Add(new FoodLog { UserId = 1, ScanResultId = scan.Id, ExtractedText = "flour", AnalysisJson = json, EatenAt = DateTime.UtcNow.AddDays(-1) });
        // Day 3
        db.FoodLogs.Add(new FoodLog { UserId = 1, ScanResultId = scan.Id, ExtractedText = "flour", AnalysisJson = json, EatenAt = DateTime.UtcNow.AddDays(-2) });
        await db.SaveChangesAsync();

        var result = await controller.HealthHistory();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Equal(3, model.HealthHistory.Count);
    }

    [Fact]
    public async Task HealthHistory_HealthyDays_ShowGreenStatus()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new DashboardController(db, _intakeService);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Buy",
            Summary = "OK",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "olive oil", Status = "Good", Reason = "Healthy" }
            }
        };
        var json = JsonSerializer.Serialize(analysis);

        var scan = new ScanResult { UserId = 1, ExtractedText = "olive oil", AnalysisJson = json };
        db.ScanResults.Add(scan);
        await db.SaveChangesAsync();

        db.FoodLogs.Add(new FoodLog { UserId = 1, ScanResultId = scan.Id, ExtractedText = "olive oil", AnalysisJson = json, EatenAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await controller.HealthHistory();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Single(model.HealthHistory);
        Assert.True(model.HealthHistory[0].IsHealthy);
    }

    [Fact]
    public async Task HealthHistory_LimitsTo30Days()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new DashboardController(db, _intakeService);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Buy",
            Summary = "OK",
            Ingredients = new List<IngredientDetail>()
        };
        var json = JsonSerializer.Serialize(analysis);

        var scan = new ScanResult { UserId = 1, ExtractedText = "flour", AnalysisJson = json };
        db.ScanResults.Add(scan);
        await db.SaveChangesAsync();

        for (int i = 0; i < 35; i++)
        {
            db.FoodLogs.Add(new FoodLog
            {
                UserId = 1,
                ScanResultId = scan.Id,
                ExtractedText = "flour",
                AnalysisJson = json,
                EatenAt = DateTime.UtcNow.AddDays(-i)
            });
        }
        await db.SaveChangesAsync();

        var result = await controller.HealthHistory();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Equal(30, model.HealthHistory.Count);
    }

    // ── Ingredient Totals ──

    [Fact]
    public async Task Today_NoLogs_HasEmptyIngredientTotals()
    {
        var controller = CreateController();

        var result = await controller.Today();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);
        Assert.Empty(model.IngredientTotals);
    }

    [Fact]
    public async Task Today_WithLogs_AggregatesIngredientTotals()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new DashboardController(db, _intakeService);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Caution",
            Summary = "Watch out",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "sugar", Status = "Neutral", Reason = "OK" },
                new() { Name = "flour", Status = "Neutral", Reason = "OK" },
                new() { Name = "red 40", Status = "Bad", Reason = "Dye" }
            }
        };
        var json = JsonSerializer.Serialize(analysis);

        var scan = new ScanResult { UserId = 1, ExtractedText = "sugar, flour, red 40", AnalysisJson = json };
        db.ScanResults.Add(scan);
        await db.SaveChangesAsync();

        // Two meals with the same ingredients
        db.FoodLogs.Add(new FoodLog { UserId = 1, ScanResultId = scan.Id, ExtractedText = "sugar, flour, red 40", AnalysisJson = json, EatenAt = DateTime.UtcNow });
        db.FoodLogs.Add(new FoodLog { UserId = 1, ScanResultId = scan.Id, ExtractedText = "sugar, flour, red 40", AnalysisJson = json, EatenAt = DateTime.UtcNow.AddHours(-2) });
        await db.SaveChangesAsync();

        var result = await controller.Today();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);

        Assert.Equal(3, model.IngredientTotals.Count);

        var sugarTotal = model.IngredientTotals.First(t => t.Name.Equals("sugar", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, sugarTotal.Count);
        Assert.Equal("Neutral", sugarTotal.WorstStatus);

        var dyeTotal = model.IngredientTotals.First(t => t.Name.Contains("red 40", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, dyeTotal.Count);
        Assert.Equal("Bad", dyeTotal.WorstStatus);
    }

    [Fact]
    public async Task Today_IngredientTotals_BadIngredientsSortedFirst()
    {
        var db = DbHelper.CreateInMemoryContext();
        var controller = new DashboardController(db, _intakeService);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Caution",
            Summary = "Mixed",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "olive oil", Status = "Good", Reason = "Healthy" },
                new() { Name = "aspartame", Status = "Bad", Reason = "Artificial" },
                new() { Name = "flour", Status = "Neutral", Reason = "OK" }
            }
        };
        var json = JsonSerializer.Serialize(analysis);

        var scan = new ScanResult { UserId = 1, ExtractedText = "olive oil, aspartame, flour", AnalysisJson = json };
        db.ScanResults.Add(scan);
        await db.SaveChangesAsync();

        db.FoodLogs.Add(new FoodLog { UserId = 1, ScanResultId = scan.Id, ExtractedText = "olive oil, aspartame, flour", AnalysisJson = json, EatenAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await controller.Today();

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DashboardViewModel>(viewResult.Model);

        // Bad ingredients should appear first
        Assert.Equal("Bad", model.IngredientTotals[0].WorstStatus);
    }
}
