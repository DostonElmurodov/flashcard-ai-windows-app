namespace Mavrylo.Models;

public sealed class AccountSyncEntity
{
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public long Version { get; set; }
    public long ChangeRevision { get; set; }
    public bool Deleted { get; set; }
    public string? Data { get; set; }
}
