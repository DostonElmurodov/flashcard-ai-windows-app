namespace Mavrylo.Models;

public class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? PasswordHash { get; set; }
    public string? GoogleSub { get; set; }
    public string? AppleSub { get; set; }
    public DateTime? IosEnrolledAt { get; set; }
    public long SyncRevision { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
