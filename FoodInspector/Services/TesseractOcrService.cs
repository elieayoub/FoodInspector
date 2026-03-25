using Tesseract;

namespace FoodInspector.Services;

public class TesseractOcrService : IOcrService, IDisposable
{
    private readonly TesseractEngine _engine;

    public TesseractOcrService(IWebHostEnvironment env)
    {
        var tessDataPath = Path.Combine(env.ContentRootPath, "tessdata");
        _engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
    }

    public Task<string> ExtractTextAsync(byte[] imageBytes)
    {
        using var pix = Pix.LoadFromMemory(imageBytes);
        using var page = _engine.Process(pix);
        var text = page.GetText();
        return Task.FromResult(text?.Trim() ?? string.Empty);
    }

    public void Dispose()
    {
        _engine?.Dispose();
    }
}
