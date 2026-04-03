using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FoodInspector.Data;
using FoodInspector.Models;
using FoodInspector.Services;

namespace FoodInspector.Controllers;

public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    private readonly IDailyIntakeService _intakeService;

    public DashboardController(AppDbContext db, IDailyIntakeService intakeService)
    {
        _db = db;
        _intakeService = intakeService;
    }

    [HttpGet]
    public async Task<IActionResult> Today()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return RedirectToAction("Login", "Account");

        var userName = HttpContext.Session.GetString("UserName") ?? "";
        var userAge = HttpContext.Session.GetInt32("UserAge") ?? 25;
        var today = DateTime.UtcNow.Date;

        var todayLogs = await _db.FoodLogs
            .Where(f => f.UserId == userId.Value && f.EatenAt >= today && f.EatenAt < today.AddDays(1))
            .OrderByDescending(f => f.EatenAt)
            .ToListAsync();

        var analyses = DeserializeAnalyses(todayLogs);
        var dailySummary = _intakeService.CalculateDailySummary(analyses, userAge, today);

        var vm = new DashboardViewModel
        {
            UserName = userName,
            UserAge = userAge,
            Date = today,
            DailySummary = dailySummary,
            TodayFoodLogs = todayLogs.Select(f =>
            {
                var analysis = DeserializeAnalysis(f.AnalysisJson);
                return new FoodLogEntry
                {
                    Id = f.Id,
                    ExtractedText = f.ExtractedText,
                    Verdict = analysis?.OverallVerdict ?? "Unknown",
                    BadCount = analysis?.Ingredients.Count(i => i.Status == "Bad") ?? 0,
                    GoodCount = analysis?.Ingredients.Count(i => i.Status == "Good") ?? 0,
                    EatenAt = f.EatenAt,
                    Ingredients = analysis?.Ingredients ?? new()
                };
            }).ToList()
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> HealthHistory()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return RedirectToAction("Login", "Account");

        var userName = HttpContext.Session.GetString("UserName") ?? "";
        var userAge = HttpContext.Session.GetInt32("UserAge") ?? 25;

        var allLogs = await _db.FoodLogs
            .Where(f => f.UserId == userId.Value)
            .OrderByDescending(f => f.EatenAt)
            .ToListAsync();

        var groupedByDate = allLogs
            .GroupBy(f => f.EatenAt.Date)
            .OrderByDescending(g => g.Key)
            .Take(30)
            .ToList();

        var healthHistory = new List<DailyHealthRecord>();
        foreach (var group in groupedByDate)
        {
            var dayAnalyses = DeserializeAnalyses(group.ToList());
            var daySummary = _intakeService.CalculateDailySummary(dayAnalyses, userAge, group.Key);

            healthHistory.Add(new DailyHealthRecord
            {
                Date = group.Key,
                TotalMeals = group.Count(),
                IsHealthy = daySummary.IsHealthy,
                AlertCount = daySummary.Alerts.Count,
                BadIngredientCount = dayAnalyses.Sum(a => a.Ingredients.Count(i => i.Status == "Bad"))
            });
        }

        var vm = new DashboardViewModel
        {
            UserName = userName,
            UserAge = userAge,
            Date = DateTime.UtcNow.Date,
            HealthHistory = healthHistory
        };

        return View(vm);
    }

    private static List<IngredientAnalysis> DeserializeAnalyses(List<FoodLog> logs)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return logs
            .Select(f => DeserializeAnalysis(f.AnalysisJson))
            .Where(a => a != null)
            .Cast<IngredientAnalysis>()
            .ToList();
    }

    private static IngredientAnalysis? DeserializeAnalysis(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<IngredientAnalysis>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}
