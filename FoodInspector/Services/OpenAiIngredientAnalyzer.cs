using System.Text.Json;
using System.Text.RegularExpressions;
using FoodInspector.Models;

namespace FoodInspector.Services;

/// <summary>
/// Uses Azure OpenAI (or OpenAI) to analyse ingredients and provide
/// age-specific health advice.  Falls back to a built-in rule engine
/// when no API key is configured.
/// </summary>
public class OpenAiIngredientAnalyzer : IIngredientAnalyzer
{
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenAiIngredientAnalyzer> _log;

    public OpenAiIngredientAnalyzer(
        IConfiguration cfg,
        IHttpClientFactory httpFactory,
        ILogger<OpenAiIngredientAnalyzer> log)
    {
        _cfg = cfg;
        _httpFactory = httpFactory;
        _log = log;
    }

    public async Task<IngredientAnalysis> AnalyzeAsync(string ingredientsText, int userAge)
    {
        var apiKey = _cfg["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _log.LogWarning("No OpenAI API key configured – using built-in rule engine.");
            return FallbackAnalyze(ingredientsText, userAge);
        }

        return await CallOpenAiAsync(ingredientsText, userAge, apiKey);
    }

    private async Task<IngredientAnalysis> CallOpenAiAsync(string ingredientsText, int userAge, string apiKey)
    {
        var endpoint = _cfg["OpenAI:Endpoint"] ?? "https://api.openai.com/v1";
        var model = _cfg["OpenAI:Model"] ?? "gpt-4o-mini";

        var systemPrompt = @"You are a food-safety and nutrition expert. The user will send you a list of ingredients read from a food product label plus their age. 
Respond ONLY with valid JSON (no markdown fences) matching this schema:
{
  ""OverallVerdict"": ""Buy"" | ""Avoid"" | ""Caution"",
  ""Summary"": ""<short advice paragraph tailored to the user's age>"",
  ""Ingredients"": [
    { ""Name"": ""<ingredient>"", ""Status"": ""Good"" | ""Neutral"" | ""Bad"", ""Reason"": ""<why>"" }
  ]
}
Consider the user's age when evaluating risk: children, teens, adults, and elderly have different tolerances for sugar, sodium, caffeine, artificial sweeteners, etc.";

        var userPrompt = $"Ingredients text:\n{ingredientsText}\n\nUser age: {userAge}";

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.3
        };

        var client = _httpFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _log.LogError("OpenAI API error {Status}: {Body}", response.StatusCode, responseJson);
            return FallbackAnalyze(ingredientsText, userAge);
        }

        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            // Strip markdown code fences if present
            content = content.Trim();
            if (content.StartsWith("```"))
            {
                var firstNewline = content.IndexOf('\n');
                if (firstNewline > 0) content = content[(firstNewline + 1)..];
                if (content.EndsWith("```")) content = content[..^3];
                content = content.Trim();
            }

            var analysis = JsonSerializer.Deserialize<IngredientAnalysis>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return analysis ?? FallbackAnalyze(ingredientsText, userAge);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to parse OpenAI response");
            return FallbackAnalyze(ingredientsText, userAge);
        }
    }

    // ───────── Built-in rule engine (works offline, no API key needed) ─────────

    private static readonly Dictionary<string, (string Status, string Reason)> KnownBad = new(StringComparer.OrdinalIgnoreCase)
    {
        ["high fructose corn syrup"] = ("Bad", "Linked to obesity, diabetes, and metabolic syndrome."),
        ["aspartame"] = ("Bad", "Artificial sweetener with controversial health effects; avoid for children."),
        ["monosodium glutamate"] = ("Bad", "May cause headaches and is best limited, especially for children."),
        ["msg"] = ("Bad", "May cause headaches and is best limited, especially for children."),
        ["sodium nitrite"] = ("Bad", "Preservative linked to increased cancer risk."),
        ["sodium nitrate"] = ("Bad", "Preservative linked to increased cancer risk."),
        ["trans fat"] = ("Bad", "Strongly linked to heart disease."),
        ["partially hydrogenated"] = ("Bad", "Source of trans fats; raises bad cholesterol."),
        ["hydrogenated oil"] = ("Bad", "Source of trans fats; raises bad cholesterol."),
        ["butylated hydroxyanisole"] = ("Bad", "BHA – possible carcinogen."),
        ["bha"] = ("Bad", "Possible carcinogen."),
        ["bht"] = ("Bad", "Possible endocrine disruptor."),
        ["potassium bromate"] = ("Bad", "Banned in many countries; possible carcinogen."),
        ["red 40"] = ("Bad", "Artificial dye linked to hyperactivity in children."),
        ["yellow 5"] = ("Bad", "Artificial dye linked to hyperactivity in children."),
        ["yellow 6"] = ("Bad", "Artificial dye linked to hyperactivity in children."),
        ["blue 1"] = ("Bad", "Artificial dye with limited safety data."),
        ["titanium dioxide"] = ("Bad", "Banned in the EU as a food additive."),
        ["carrageenan"] = ("Bad", "May cause gastrointestinal inflammation."),
        ["acesulfame potassium"] = ("Bad", "Artificial sweetener with limited long-term data."),
        ["sucralose"] = ("Neutral", "Artificial sweetener – generally recognised as safe but best in moderation."),
        ["palm oil"] = ("Neutral", "High in saturated fat; environmental concerns."),
        ["sugar"] = ("Neutral", "Safe in moderation but excess linked to obesity and diabetes."),
        ["salt"] = ("Neutral", "Essential but excess raises blood pressure."),
        ["sodium"] = ("Neutral", "Essential but excess raises blood pressure."),
        ["caffeine"] = ("Neutral", "Safe for most adults; avoid for children and elderly in large amounts."),
        ["cholesterol"] = ("Neutral", "Needed by the body but excess linked to heart disease."),
    };

    private static readonly HashSet<string> GoodKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "whole grain", "oats", "olive oil", "flaxseed", "chia", "quinoa",
        "vitamin", "iron", "calcium", "fiber", "protein", "omega-3",
        "turmeric", "green tea", "honey", "coconut oil", "avocado oil",
        "potassium", "dietary fiber", "carbohydrate"
    };

    /// <summary>
    /// Common food ingredients that are safe/neutral but not covered by
    /// KnownBad or GoodKeywords. Only items matching a known list are
    /// included in the analysis — unrecognised OCR text is skipped.
    /// </summary>
    private static readonly HashSet<string> KnownNeutral = new(StringComparer.OrdinalIgnoreCase)
    {
        "flour", "wheat flour", "enriched flour", "all-purpose flour",
        "water", "milk", "cream", "butter", "eggs", "egg",
        "corn starch", "cornstarch", "starch", "modified starch",
        "yeast", "baking soda", "baking powder", "gelatin",
        "soy lecithin", "lecithin", "soybean oil", "canola oil",
        "sunflower oil", "vegetable oil", "corn oil", "peanut oil",
        "rice", "rice flour", "potato starch",
        "vinegar", "citric acid", "lactic acid", "ascorbic acid",
        "xanthan gum", "guar gum", "cellulose gum", "pectin",
        "cocoa", "cocoa butter", "chocolate", "vanilla", "vanilla extract",
        "cinnamon", "pepper", "garlic", "onion", "ginger", "paprika",
        "mustard", "soy sauce", "worcestershire",
        "tomato", "tomato paste", "corn syrup",
        "whey", "casein", "skim milk", "nonfat milk",
        "dextrose", "maltodextrin", "fructose", "glucose",
        "glycerin", "sorbitol",
        "natural flavor", "natural flavors", "artificial flavor", "artificial flavors",
        "spices", "herbs", "seasoning",
        "monoglycerides", "diglycerides", "polysorbate",
        "sodium bicarbonate", "potassium sorbate", "sodium benzoate",
        "annatto", "beta carotene", "caramel color",
    };

    private IngredientAnalysis FallbackAnalyze(string ingredientsText, int userAge)
    {
        var items = ingredientsText
            .Split(new[] { ',', '\n', ';', '•', '·' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(i => i.Trim())
            .Where(i => i.Length > 1)
            .ToList();

        if (items.Count == 0)
        {
            items = ingredientsText
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .ToList();
        }

        var details = new List<IngredientDetail>();
        int badCount = 0, goodCount = 0;

        foreach (var raw in items)
        {
            var item = raw.Trim('.', ':', '-', ' ', '(', ')');
            if (string.IsNullOrWhiteSpace(item)) continue;

            // Skip descriptive text / disclaimers that OCR picked up
            // (e.g. "The % Daily Value (DV) tells you how much a nutrient in")
            if (IsDescriptiveText(item)) continue;

            // Skip nutrition label structural text (e.g. "Calories 200", "Serving Size")
            if (IsNutritionLabelNoise(item)) continue;

            // Skip any item whose quantity is zero (e.g. "Vitamin D 0mcg", "Trans Fat 0g")
            if (HasZeroQuantity(item)) continue;

            var matched = false;

            foreach (var kvp in KnownBad)
            {
                if (item.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    var reason = kvp.Value.Reason;

                    // Age-specific adjustments
                    if (userAge < 12 && (kvp.Key.Contains("dye", StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains("red 40", StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains("yellow", StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains("caffeine", StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains("aspartame", StringComparison.OrdinalIgnoreCase)))
                    {
                        reason += " ⚠️ Especially harmful for children under 12.";
                    }

                    if (userAge > 60 && (kvp.Key.Contains("sodium", StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains("salt", StringComparison.OrdinalIgnoreCase)
                        || kvp.Key.Contains("caffeine", StringComparison.OrdinalIgnoreCase)))
                    {
                        reason += " ⚠️ Extra caution recommended for adults over 60.";
                    }

                    var status = kvp.Value.Status;
                    if (status == "Bad") badCount++;

                    details.Add(new IngredientDetail { Name = item, Status = status, Reason = reason });
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                var isGood = GoodKeywords.Any(g => item.Contains(g, StringComparison.OrdinalIgnoreCase));
                if (isGood)
                {
                    goodCount++;
                    details.Add(new IngredientDetail { Name = item, Status = "Good", Reason = "Generally beneficial for health." });
                }
                else
                {
                    // Only add items that match a known neutral ingredient.
                    // Unrecognised text (OCR noise, label structure, paragraphs) is skipped.
                    var isKnownNeutral = KnownNeutral.Any(n => item.Contains(n, StringComparison.OrdinalIgnoreCase)
                        || n.Contains(item, StringComparison.OrdinalIgnoreCase));
                    if (isKnownNeutral)
                    {
                        details.Add(new IngredientDetail { Name = item, Status = "Neutral", Reason = "Common ingredient – no major concerns in normal amounts." });
                    }
                    // else: not a recognised ingredient — skip (OCR noise / label text)
                }
            }
        }

        string verdict;
        string summary;

        if (badCount >= 3)
        {
            verdict = "Avoid";
            summary = $"This product contains {badCount} concerning ingredient(s). ";
        }
        else if (badCount >= 1)
        {
            verdict = "Caution";
            summary = $"This product contains {badCount} ingredient(s) that deserve attention. ";
        }
        else
        {
            verdict = "Buy";
            summary = "No major harmful ingredients detected. ";
        }

        // Age-specific summary
        if (userAge < 12)
            summary += "As a product for a child, be extra careful with artificial dyes, sweeteners, and caffeine.";
        else if (userAge < 18)
            summary += "For a teenager, moderation with sugar and processed ingredients is important.";
        else if (userAge > 60)
            summary += "For someone over 60, watch sodium levels and artificial additives closely.";
        else
            summary += "For an adult, this evaluation considers standard nutritional guidelines.";

        return new IngredientAnalysis
        {
            OverallVerdict = verdict,
            Summary = summary,
            Ingredients = details
        };
    }

    /// <summary>
    /// Returns true when the text indicates a zero quantity, e.g.
    /// "Trans Fat 0g", "Sodium 0 mg", "Sugar 0%", "Caffeine 0.0g".
    /// </summary>
    private static bool HasZeroQuantity(string text)
    {
        // Matches patterns like "0g", "0 mg", "0%", "0.0g", "0.00mg"
        return Regex.IsMatch(text, @"\b0+(\.0+)?\s*(%|m?g|mg|mcg)\b", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Returns true when the text looks like a descriptive sentence or disclaimer
    /// rather than an ingredient name (e.g. "The % Daily Value tells you how much…").
    /// </summary>
    private static bool IsDescriptiveText(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Ingredient names rarely exceed 6 words
        if (words.Length > 6)
            return true;

        // Count common English function words that never appear in ingredient names
        int count = 0;
        foreach (var word in words)
        {
            var clean = word.Trim('.', ',', ':', ';', '(', ')', '*', '%', '"', '\'');
            if (_sentenceIndicators.Contains(clean) && ++count >= 2)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when the text matches common nutrition label structural entries
    /// that are NOT ingredients (e.g. "Total Fat 9g", "Calories 200", "Amount Per Serving").
    /// </summary>
    private static bool IsNutritionLabelNoise(string text)
    {
        return _nutritionLabelNoisePattern.IsMatch(text);
    }

    private static readonly Regex _nutritionLabelNoisePattern = new(
        @"(?i)^(" +
        @"total\s+fat|saturated\s+fat|total\s+sugars?|added\s+sugars?" +
        @"|calories|amount\s+per|servings?\s+per|serving\s+size" +
        @"|nutrition\s+facts|net\s+wt|net\s+weight|ingredients\s*:" +
        @"|daily\s+value|best\s+by|use\s+by|manufactured|distributed|contains:" +
        @")\b",
        RegexOptions.Compiled);

    private static readonly HashSet<string> _sentenceIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "are", "you", "your", "how", "much", "this", "that",
        "tells", "used", "contributes", "based", "should", "would", "could",
        "does", "have", "has", "was", "were", "been", "being", "about",
        "into", "from", "per", "serves", "serving", "daily", "advice",
        "diet", "general", "percent"
    };
}
