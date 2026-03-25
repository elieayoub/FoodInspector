# FoodInspector

A .NET 8 web application that helps users make healthier food choices by scanning food product labels, extracting ingredients via OCR, and analyzing them using AI or a built-in rule engine.

## Features

- **User Registration** — Simple name + age registration stored in SQLite; age is used to personalize ingredient analysis.
- **Label Scanning** — Take a photo of a food product's ingredient list using your device camera.
- **OCR Text Extraction** — Tesseract OCR extracts text from the captured image.
- **AI-Powered Analysis** — Ingredients are evaluated via OpenAI (GPT-4o-mini) with age-specific health advice. Falls back to a built-in rule engine when no API key is configured.
- **Verdict System** — Each scan produces a **Buy**, **Caution**, or **Avoid** recommendation with per-ingredient breakdowns.
- **Scan History** — View your last 20 scans with full analysis details.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core 8 (MVC) |
| Database | SQLite via Entity Framework Core 8 |
| OCR | Tesseract 5 (`Tesseract` + `Tesseract.Drawing` NuGet packages) |
| AI | OpenAI API (`Azure.AI.OpenAI`) with built-in fallback rule engine |
| Front-end | Razor Views, Bootstrap, jQuery |

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Tesseract trained data (`eng.traineddata`) is included in `FoodInspector/tessdata/`

### Run Locally

```bash
cd FoodInspector
dotnet run
```

The app will be available at `https://localhost:5001` (or the port shown in the console).

### Configuration

Edit `FoodInspector/appsettings.json` to configure OpenAI:

```json
{
  "OpenAI": {
    "ApiKey": "your-api-key-here",
    "Endpoint": "https://api.openai.com/v1",
    "Model": "gpt-4o-mini"
  }
}
```

If no API key is provided, the app uses a built-in rule engine that recognizes common harmful, neutral, and beneficial ingredients.

## Project Structure

```
FoodInspector/
├── Controllers/
│   ├── AccountController.cs   # User registration & logout
│   ├── HomeController.cs      # Home & privacy pages
│   └── ScanController.cs      # Label scanning & analysis
├── Data/
│   └── AppDbContext.cs         # EF Core DbContext (SQLite)
├── Models/
│   ├── AppUser.cs              # User entity
│   ├── ScanResult.cs           # Persisted scan entity
│   ├── IngredientAnalysis.cs   # Analysis result model
│   ├── ScanViewModel.cs        # View model for scan pages
│   ├── RegisterViewModel.cs    # View model for registration
│   └── ErrorViewModel.cs       # Error page view model
├── Services/
│   ├── IOcrService.cs          # OCR service interface
│   ├── TesseractOcrService.cs  # Tesseract OCR implementation
│   ├── IIngredientAnalyzer.cs  # Analyzer interface
│   └── OpenAiIngredientAnalyzer.cs  # OpenAI + fallback rule engine
├── Views/                      # Razor views
├── tessdata/                   # Tesseract trained data
└── Program.cs                  # Application entry point
```

## Running Tests

```bash
dotnet test
```

## License

This project is provided as-is for educational purposes.
