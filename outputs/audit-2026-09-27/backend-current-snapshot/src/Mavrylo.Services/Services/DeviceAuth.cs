namespace Mavrylo.Services;

/// <summary>
/// Constants for the device authentication scheme — the short-lived device-JWT minted after
/// App Attest registration / StoreKit verification. Distinct audience from the user/login token
/// so the two token types can never be substituted for one another.
/// </summary>
public static class DeviceAuth
{
    /// <summary>Named JwtBearer scheme registered in Program.cs for device tokens.</summary>
    public const string Scheme = "device";

    /// <summary>JWT <c>aud</c> claim value for device tokens.</summary>
    public const string Audience = "device";

    /// <summary>Default device-JWT lifetime (minutes).</summary>
    public const int DefaultLifetimeMinutes = 30;
}
