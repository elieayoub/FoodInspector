using Xunit;
using FoodInspector.Models;
using FoodInspector.Services;

namespace FoodInspector.Tests.Services;

public class DailyIntakeServiceTests
{
    private readonly DailyIntakeService _service = new();

    // ── GetDailyLimits ──

    [Fact]
    public void GetDailyLimits_Child_ReturnsChildLimits()
    {
        var limits = _service.GetDailyLimits(8);

        Assert.Equal(0, limits["caffeine"]);
        Assert.Equal(0, limits["artificial dye"]);
        Assert.Equal(3, limits["bad_total"]);
    }

    [Fact]
    public void GetDailyLimits_Teen_ReturnsTeenLimits()
    {
        var limits = _service.GetDailyLimits(15);

        Assert.Equal(1, limits["caffeine"]);
        Assert.Equal(5, limits["bad_total"]);
    }

    [Fact]
    public void GetDailyLimits_Adult_ReturnsAdultLimits()
    {
        var limits = _service.GetDailyLimits(30);

        Assert.Equal(3, limits["caffeine"]);
        Assert.Equal(8, limits["bad_total"]);
    }

    [Fact]
    public void GetDailyLimits_Elderly_ReturnsElderlyLimits()
    {
        var limits = _service.GetDailyLimits(65);

        Assert.Equal(1, limits["caffeine"]);
        Assert.Equal(2, limits["sodium"]);
        Assert.Equal(5, limits["bad_total"]);
    }

    // ── CalculateDailySummary ──

    [Fact]
    public void CalculateDailySummary_NoMeals_ReturnsHealthy()
    {
        var summary = _service.CalculateDailySummary(new List<IngredientAnalysis>(), 25, DateTime.UtcNow.Date);

        Assert.True(summary.IsHealthy);
        Assert.Empty(summary.Alerts);
        Assert.Equal(0, summary.TotalMeals);
    }

    [Fact]
    public void CalculateDailySummary_OneSafeMeal_ReturnsHealthy()
    {
        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Buy",
            Summary = "Good",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "flour", Status = "Neutral", Reason = "OK" },
                new() { Name = "olive oil", Status = "Good", Reason = "Healthy fat" }
            }
        };

        var summary = _service.CalculateDailySummary(new List<IngredientAnalysis> { analysis }, 25, DateTime.UtcNow.Date);

        Assert.True(summary.IsHealthy);
        Assert.Empty(summary.Alerts);
        Assert.Equal(1, summary.TotalMeals);
    }

    [Fact]
    public void CalculateDailySummary_MultipleBadMeals_GeneratesAlerts()
    {
        var analyses = Enumerable.Range(0, 5).Select(_ => new IngredientAnalysis
        {
            OverallVerdict = "Avoid",
            Summary = "Bad",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "high fructose corn syrup", Status = "Bad", Reason = "Sugary" },
                new() { Name = "red 40", Status = "Bad", Reason = "Dye" }
            }
        }).ToList();

        var summary = _service.CalculateDailySummary(analyses, 25, DateTime.UtcNow.Date);

        Assert.False(summary.IsHealthy);
        Assert.NotEmpty(summary.Alerts);
        Assert.Equal(5, summary.TotalMeals);
    }

    [Fact]
    public void CalculateDailySummary_ChildExceedsCaffeine_GeneratesAlert()
    {
        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Caution",
            Summary = "Caffeine",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "caffeine", Status = "Bad", Reason = "Not for children" }
            }
        };

        var summary = _service.CalculateDailySummary(new List<IngredientAnalysis> { analysis }, 8, DateTime.UtcNow.Date);

        Assert.False(summary.IsHealthy);
        Assert.Contains(summary.Alerts, a => a.Contains("caffeine"));
    }

    [Fact]
    public void CalculateDailySummary_AdultWithinLimits_StaysHealthy()
    {
        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Caution",
            Summary = "Some bad",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "sugar", Status = "Bad", Reason = "High sugar" },
                new() { Name = "flour", Status = "Neutral", Reason = "OK" }
            }
        };

        var summary = _service.CalculateDailySummary(new List<IngredientAnalysis> { analysis }, 30, DateTime.UtcNow.Date);

        Assert.True(summary.IsHealthy);
        Assert.Empty(summary.Alerts);
    }

    [Fact]
    public void CalculateDailySummary_ExceedsTotalBadLimit_GeneratesTotalAlert()
    {
        // Adult bad_total limit is 8; create 10 bad ingredients
        var analyses = Enumerable.Range(0, 10).Select(_ => new IngredientAnalysis
        {
            OverallVerdict = "Avoid",
            Summary = "Bad",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "sodium nitrite", Status = "Bad", Reason = "Preservative" }
            }
        }).ToList();

        var summary = _service.CalculateDailySummary(analyses, 30, DateTime.UtcNow.Date);

        Assert.False(summary.IsHealthy);
        Assert.Contains(summary.Alerts, a => a.Contains("Total bad ingredients"));
    }

    [Fact]
    public void CalculateDailySummary_TracksAccumulationByCategory()
    {
        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Avoid",
            Summary = "Bad",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "high fructose corn syrup", Status = "Bad", Reason = "Sugar" },
                new() { Name = "sugar", Status = "Bad", Reason = "Sugar" },
                new() { Name = "red 40", Status = "Bad", Reason = "Dye" }
            }
        };

        var summary = _service.CalculateDailySummary(new List<IngredientAnalysis> { analysis }, 30, DateTime.UtcNow.Date);

        Assert.True(summary.Accumulations.ContainsKey("sugar"));
        Assert.Equal(2, summary.Accumulations["sugar"].Count);
        Assert.True(summary.Accumulations.ContainsKey("artificial dye"));
        Assert.Equal(1, summary.Accumulations["artificial dye"].Count);
    }

    [Fact]
    public void CalculateDailySummary_DateIsPreserved()
    {
        var date = new DateTime(2024, 6, 15);
        var summary = _service.CalculateDailySummary(new List<IngredientAnalysis>(), 25, date);

        Assert.Equal(date, summary.Date);
    }

    [Fact]
    public void CalculateDailySummary_ElderlyExceedsSodium_GeneratesAlert()
    {
        // Elderly sodium limit is 2
        var analyses = new List<IngredientAnalysis>
        {
            new()
            {
                OverallVerdict = "Caution",
                Summary = "Salty",
                Ingredients = new List<IngredientDetail>
                {
                    new() { Name = "sodium", Status = "Bad", Reason = "High sodium" }
                }
            },
            new()
            {
                OverallVerdict = "Caution",
                Summary = "Salty",
                Ingredients = new List<IngredientDetail>
                {
                    new() { Name = "salt", Status = "Bad", Reason = "High sodium" }
                }
            },
            new()
            {
                OverallVerdict = "Caution",
                Summary = "Salty",
                Ingredients = new List<IngredientDetail>
                {
                    new() { Name = "sodium", Status = "Bad", Reason = "High sodium" }
                }
            }
        };

        var summary = _service.CalculateDailySummary(analyses, 65, DateTime.UtcNow.Date);

        Assert.False(summary.IsHealthy);
        Assert.Contains(summary.Alerts, a => a.Contains("sodium"));
    }

    [Fact]
    public void CalculateDailySummary_GoodAndNeutralIngredients_AreNotCounted()
    {
        var analysis = new IngredientAnalysis
        {
            OverallVerdict = "Buy",
            Summary = "Good",
            Ingredients = new List<IngredientDetail>
            {
                new() { Name = "olive oil", Status = "Good", Reason = "Healthy" },
                new() { Name = "flour", Status = "Neutral", Reason = "OK" }
            }
        };

        var summary = _service.CalculateDailySummary(new List<IngredientAnalysis> { analysis }, 25, DateTime.UtcNow.Date);

        Assert.Equal(0, summary.Accumulations["bad_total"].Count);
    }
}
