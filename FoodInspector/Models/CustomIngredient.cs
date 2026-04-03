namespace FoodInspector.Models;

public class CustomIngredient
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Neutral";       // "Good", "Neutral", "Bad"
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
}
