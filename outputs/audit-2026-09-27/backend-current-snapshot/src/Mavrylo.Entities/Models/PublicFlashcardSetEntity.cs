namespace Mavrylo.Models;

public static class PublicFlashcardSetStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
}

public class PublicFlashcardSetEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? OwnerDeviceId { get; set; }
    public DeviceEntity? OwnerDevice { get; set; }
    public string? OwnerAccountId { get; set; }
    public string ClientSetId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string SnapshotJson { get; set; } = "[]";
    public string SearchText { get; set; } = "";
    public int WordCount { get; set; }
    public string Status { get; set; } = PublicFlashcardSetStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
