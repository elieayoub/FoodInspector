using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FoodInspector.Data;
using FoodInspector.Models;
using FoodInspector.Services;

namespace FoodInspector.Controllers;

public class ScanController : Controller
{
    private readonly IOcrService _ocr;
    private readonly IIngredientAnalyzer _analyzer;
    private readonly AppDbContext _db;

    public ScanController(IOcrService ocr, IIngredientAnalyzer analyzer, AppDbContext db)
    {
        _ocr = ocr;
        _analyzer = analyzer;
        _db = db;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (HttpContext.Session.GetInt32("UserId") == null)
            return RedirectToAction("Register", "Account");

        var vm = new ScanViewModel
        {
            UserName = HttpContext.Session.GetString("UserName") ?? "",
            UserAge = HttpContext.Session.GetInt32("UserAge") ?? 0
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Analyze([FromForm] string imageData)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return RedirectToAction("Register", "Account");

        var userName = HttpContext.Session.GetString("UserName") ?? "";
        var userAge = HttpContext.Session.GetInt32("UserAge") ?? 25;

        var vm = new ScanViewModel
        {
            UserName = userName,
            UserAge = userAge
        };

        if (string.IsNullOrWhiteSpace(imageData))
        {
            ViewBag.Error = "No image received. Please take a photo.";
            return View("Index", vm);
        }

        try
        {
            // imageData comes as "data:image/...;base64,XXXXXX"
            var base64 = imageData;
            var commaIndex = imageData.IndexOf(',');
            if (commaIndex >= 0)
                base64 = imageData[(commaIndex + 1)..];

            var imageBytes = Convert.FromBase64String(base64);

            // OCR
            var extractedText = await _ocr.ExtractTextAsync(imageBytes);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                ViewBag.Error = "Could not read any text from the image. Please try again with a clearer photo.";
                vm.ImageBase64 = imageData;
                return View("Index", vm);
            }

            // Analyse
            var analysis = await _analyzer.AnalyzeAsync(extractedText, userAge);

            // Persist
            var scanResult = new ScanResult
            {
                UserId = userId.Value,
                ExtractedText = extractedText,
                AnalysisJson = JsonSerializer.Serialize(analysis)
            };
            _db.ScanResults.Add(scanResult);
            await _db.SaveChangesAsync();

            vm.ImageBase64 = imageData;
            vm.ExtractedText = extractedText;
            vm.Analysis = analysis;

            return View("Result", vm);
        }
        catch (Exception ex)
        {
            ViewBag.Error = $"Error processing image: {ex.Message}";
            return View("Index", vm);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Reanalyze([FromForm] string extractedText, [FromForm] string? imageData)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return RedirectToAction("Register", "Account");

        var userName = HttpContext.Session.GetString("UserName") ?? "";
        var userAge = HttpContext.Session.GetInt32("UserAge") ?? 25;

        var vm = new ScanViewModel
        {
            UserName = userName,
            UserAge = userAge,
            ImageBase64 = imageData
        };

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            ViewBag.Error = "Ingredient text cannot be empty.";
            return View("Result", vm);
        }

        var analysis = await _analyzer.AnalyzeAsync(extractedText, userAge);

        // Persist the updated scan
        var scanResult = new ScanResult
        {
            UserId = userId.Value,
            ExtractedText = extractedText,
            AnalysisJson = JsonSerializer.Serialize(analysis)
        };
        _db.ScanResults.Add(scanResult);
        await _db.SaveChangesAsync();

        vm.ExtractedText = extractedText;
        vm.Analysis = analysis;

        return View("Result", vm);
    }

    [HttpGet]
    public async Task<IActionResult> History()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return RedirectToAction("Register", "Account");

        var results = await _db.ScanResults
            .Where(s => s.UserId == userId.Value)
            .OrderByDescending(s => s.ScannedAt)
            .Take(20)
            .ToListAsync();

        var viewModels = results.Select(r => new ScanViewModel
        {
            ExtractedText = r.ExtractedText,
            Analysis = string.IsNullOrWhiteSpace(r.AnalysisJson)
                ? null
                : JsonSerializer.Deserialize<IngredientAnalysis>(r.AnalysisJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
            UserName = HttpContext.Session.GetString("UserName") ?? "",
            UserAge = HttpContext.Session.GetInt32("UserAge") ?? 0
        }).ToList();

        return View(viewModels);
    }
}
