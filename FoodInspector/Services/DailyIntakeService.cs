using FoodInspector.Models;

namespace FoodInspector.Services;

public class DailyIntakeService : IDailyIntakeService
{
    // Maximum number of "Bad" ingredient servings allowed per day by age group.
    // These are intentionally conservative counts of bad-ingredient exposures.
    private static readonly Dictionary<string, Dictionary<string, int>> AgeLimits = new()
    {
        ["child"] = new()
        {
            ["sugar"] = 2,
            ["sodium"] = 2,
            ["caffeine"] = 0,
            ["trans fat"] = 0,
            ["artificial dye"] = 0,
            ["artificial sweetener"] = 0,
            ["preservative"] = 1,
            ["bad_total"] = 3
        },
        ["teen"] = new()
        {
            ["sugar"] = 3,
            ["sodium"] = 3,
            ["caffeine"] = 1,
            ["trans fat"] = 0,
            ["artificial dye"] = 1,
            ["artificial sweetener"] = 1,
            ["preservative"] = 2,
            ["bad_total"] = 5
        },
        ["adult"] = new()
        {
            ["sugar"] = 4,
            ["sodium"] = 4,
            ["caffeine"] = 3,
            ["trans fat"] = 1,
            ["artificial dye"] = 2,
            ["artificial sweetener"] = 2,
            ["preservative"] = 3,
            ["bad_total"] = 8
        },
        ["elderly"] = new()
        {
            ["sugar"] = 3,
            ["sodium"] = 2,
            ["caffeine"] = 1,
            ["trans fat"] = 0,
            ["artificial dye"] = 1,
            ["artificial sweetener"] = 1,
            ["preservative"] = 2,
            ["bad_total"] = 5
        }
    };

    // Maps known bad ingredient keywords to their category
    private static readonly Dictionary<string, string> IngredientCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sugar"] = "sugar",
        ["high fructose corn syrup"] = "sugar",
        ["sodium"] = "sodium",
        ["salt"] = "sodium",
        ["sodium nitrite"] = "preservative",
        ["sodium nitrate"] = "preservative",
        ["bha"] = "preservative",
        ["bht"] = "preservative",
        ["potassium bromate"] = "preservative",
        ["carrageenan"] = "preservative",
        ["caffeine"] = "caffeine",
        ["trans fat"] = "trans fat",
        ["partially hydrogenated"] = "trans fat",
        ["hydrogenated oil"] = "trans fat",
        ["red 40"] = "artificial dye",
        ["yellow 5"] = "artificial dye",
        ["yellow 6"] = "artificial dye",
        ["blue 1"] = "artificial dye",
        ["titanium dioxide"] = "artificial dye",
        ["aspartame"] = "artificial sweetener",
        ["acesulfame potassium"] = "artificial sweetener",
        ["sucralose"] = "artificial sweetener",
        ["monosodium glutamate"] = "preservative",
        ["msg"] = "preservative",
    };

    public Dictionary<string, int> GetDailyLimits(int userAge)
    {
        var group = GetAgeGroup(userAge);
        return AgeLimits.TryGetValue(group, out var limits)
            ? new Dictionary<string, int>(limits)
            : new Dictionary<string, int>(AgeLimits["adult"]);
    }

    public DailyIntakeSummary CalculateDailySummary(List<IngredientAnalysis> analyses, int userAge, DateTime date)
    {
        var limits = GetDailyLimits(userAge);
        var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int totalBad = 0;

        foreach (var analysis in analyses)
        {
            foreach (var ing in analysis.Ingredients)
            {
                if (ing.Status != "Bad") continue;

                totalBad++;
                var category = CategorizeIngredient(ing.Name);
                if (!categoryCounts.ContainsKey(category))
                    categoryCounts[category] = 0;
                categoryCounts[category]++;
            }
        }

        var summary = new DailyIntakeSummary
        {
            Date = date,
            TotalMeals = analyses.Count
        };

        // Build accumulations
        foreach (var kvp in categoryCounts)
        {
            var limitMax = limits.TryGetValue(kvp.Key, out var lim) ? lim : 999;
            var exceeds = kvp.Value > limitMax;

            summary.Accumulations[kvp.Key] = new IngredientAccumulation
            {
                IngredientName = kvp.Key,
                Count = kvp.Value,
                WorstStatus = "Bad",
                DailyLimitMax = limitMax,
                ExceedsLimit = exceeds,
                LimitUnit = "servings"
            };

            if (exceeds)
            {
                summary.Alerts.Add($"⚠️ You exceeded your daily limit for {kvp.Key}: {kvp.Value}/{limitMax} servings.");
            }
        }

        // Check total bad limit
        var totalBadLimit = limits.TryGetValue("bad_total", out var btl) ? btl : 8;
        if (totalBad > totalBadLimit)
        {
            summary.Alerts.Add($"🚨 Total bad ingredients today ({totalBad}) exceeds your daily limit of {totalBadLimit}.");
        }

        // Add total bad tracking
        summary.Accumulations["bad_total"] = new IngredientAccumulation
        {
            IngredientName = "Total Bad Ingredients",
            Count = totalBad,
            DailyLimitMax = totalBadLimit,
            ExceedsLimit = totalBad > totalBadLimit,
            LimitUnit = "total"
        };

        return summary;
    }

    private static string CategorizeIngredient(string ingredientName)
    {
        foreach (var kvp in IngredientCategories)
        {
            if (ingredientName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }
        return "other";
    }

    private static string GetAgeGroup(int age) => age switch
    {
        < 12 => "child",
        < 18 => "teen",
        > 60 => "elderly",
        _ => "adult"
    };
}
