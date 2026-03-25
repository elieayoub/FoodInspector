namespace FoodInspector.Services;

public interface IOcrService
{
    Task<string> ExtractTextAsync(byte[] imageBytes);
}
