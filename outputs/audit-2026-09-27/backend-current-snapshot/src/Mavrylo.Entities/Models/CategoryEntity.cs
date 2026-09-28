namespace Mavrylo.Models;

public class CategoryEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Emoji { get; set; }
    public string? Color { get; set; }
    public int Order { get; set; }
    public int WordCount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
