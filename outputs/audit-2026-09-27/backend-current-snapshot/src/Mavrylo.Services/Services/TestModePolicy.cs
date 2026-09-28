namespace Mavrylo.Services;

/// <summary>Server-owned testing access; never creates or modifies a purchase.</summary>
public static class TestModePolicy
{
    public static bool IsEnabled(IConfiguration? configuration) =>
        bool.TryParse(configuration?["TestMode:Enabled"], out var enabled) && enabled;
}
