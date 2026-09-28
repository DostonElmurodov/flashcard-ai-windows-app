using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Mavrylo.Data;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<OpenAiJsonService>();
builder.Services.AddScoped<GeminiJsonService>();
builder.Services.AddScoped<IAiProviderJsonService>(sp => sp.GetRequiredService<OpenAiJsonService>());
builder.Services.AddScoped<IAiProviderJsonService>(sp => sp.GetRequiredService<GeminiJsonService>());
builder.Services.AddScoped<IAiJsonService, FallbackAiJsonService>();
builder.Services.AddSingleton<AiAlertCooldown>();
builder.Services.AddSingleton<ISmtpClientFactory, MailKitSmtpClientFactory>();
builder.Services.AddSingleton<IDevAlertEmailService, SmtpDevAlertEmailService>();
builder.Services.AddScoped<TranslationCacheService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ChallengeService>();
builder.Services.AddSingleton<AppAttestVerifier>();
builder.Services.AddSingleton<IAppAttestVerifier>(sp => sp.GetRequiredService<AppAttestVerifier>());
builder.Services.AddHttpClient<AppStoreServerClient>();
builder.Services.AddScoped<IAppStoreServerClient>(sp => sp.GetRequiredService<AppStoreServerClient>());
builder.Services.AddScoped<EntitlementService>();
builder.Services.AddScoped<DeviceContextService>();
builder.Services.AddScoped<AiUsageService>();
builder.Services.AddScoped<DeviceWordService>();
builder.Services.AddScoped<PublicFlashcardSetService>();
builder.Services.AddScoped<WordService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<UserSettingsService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<AccountSyncService>();
builder.Services.AddScoped<Mavrylo.Filters.OptionalAccountProofFilter>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SharedAccountAuthentication>();
builder.Services.AddScoped<SubscriptionOwnershipService>();
builder.Services.AddScoped<AccountEntitlementService>();
builder.Services.AddScoped<AccountAiUsageService>();
builder.Services.AddScoped<Mavrylo.Filters.AccountClaimProofFilter>();
builder.Services.AddScoped<Mavrylo.Filters.AccountAiProtectionFilter>();
if (!builder.Environment.IsDevelopment() &&
    (builder.Configuration.GetValue<int>("AccountAi:DailyQuota") <= 0 || builder.Configuration.GetValue<int>("AccountAi:RequestsPerMinute") <= 0))
    throw new InvalidOperationException("AccountAi:DailyQuota and AccountAi:RequestsPerMinute must be configured to positive limits.");
builder.Services.AddSingleton<IGoogleIdentityVerifier, GoogleIdentityVerifier>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<WordAiService>();
builder.Services.AddScoped<AppAttestRegistrationService>();
builder.Services.AddScoped<IapService>();
builder.Services.AddScoped<AppStoreNotificationService>();
builder.Services.Configure<AiProtectionOptions>(builder.Configuration.GetSection(AiProtectionOptions.SectionName));
builder.Services.AddScoped<Mavrylo.Filters.AiProtectionFilter>();
builder.Services.AddScoped<Mavrylo.Filters.AppAttestAssertionFilter>();
builder.Services.AddScoped<Mavrylo.Filters.LegacyAuthGuardFilter>();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedHost
        | ForwardedHeaders.XForwardedProto;
    o.KnownProxies.Add(IPAddress.Parse("127.0.0.1"));
    o.KnownProxies.Add(IPAddress.IPv6Loopback);
    foreach (var proxy in SplitCsv(builder.Configuration["ForwardedHeaders:KnownProxies"]))
    {
        if (IPAddress.TryParse(proxy, out var ip))
            o.KnownProxies.Add(ip);
    }
});

if (!builder.Environment.IsDevelopment())
{
    if (!builder.Configuration.GetValue<bool>("AiProtection:Enabled"))
        throw new InvalidOperationException("AiProtection:Enabled must be true outside Development.");

    if (!builder.Configuration.GetValue<bool>("AiProtection:RequireAssertion"))
        throw new InvalidOperationException("AiProtection:RequireAssertion must be true outside Development.");

    if (builder.Configuration.GetValue<bool>("Apple:SkipSignatureValidation"))
        throw new InvalidOperationException("Apple:SkipSignatureValidation must be false outside Development.");

    ValidateProductionConfiguration(builder.Configuration);
}

// PostgreSQL everywhere (local dev + production). The server overrides this via the
// ConnectionStrings__Default environment variable in /etc/owl-ai/api.env.
var conn = builder.Configuration.GetConnectionString("Default")
    ?? "Host=127.0.0.1;Port=5432;Database=owl_ai;Username=owl_ai;Password=owl_ai";
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(conn));

var jwtKey = builder.Configuration["Jwt:Key"]?.Trim();
if (string.IsNullOrEmpty(jwtKey))
    throw new InvalidOperationException(
        "Jwt:Key is missing or empty. Set it in appsettings, User Secrets, or environment (e.g. Jwt__Key). Use a long random string (32+ bytes) for HS256.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

var accountAudience = AccountAuth.Audience(builder.Configuration);

var signingKeyBytes = Encoding.UTF8.GetBytes(jwtKey);
if (signingKeyBytes.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key is too short for HS256. Use at least 32 UTF-8 bytes (a long random secret).");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Default scheme: login/user tokens (aud = Jwt:Audience). Used by user-data endpoints.
        o.TokenValidationParameters = new TokenValidationParameters
        {
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    })
    .AddJwtBearer(DeviceAuth.Scheme, o =>
    {
        // Device scheme: short-lived device-JWTs (aud = "device") minted after App Attest /
        // StoreKit verification. Same HS256 signing key, distinct audience so device tokens and
        // user tokens can never be used interchangeably. Required on AI/IAP endpoints (Phase 2).
        o.TokenValidationParameters = new TokenValidationParameters
        {
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = DeviceAuth.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthentication().AddJwtBearer(AccountAuth.Scheme, o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        IgnoreTrailingSlashWhenValidatingAudience = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true, ValidIssuer = jwtIssuer,
        ValidateAudience = true, ValidAudience = accountAudience,
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var principal = context.Principal!;
            var sub = principal.FindFirst("sub")?.Value;
            var sid = principal.FindFirst("sid")?.Value;
            if (principal.FindFirst("token_use")?.Value != "account" || principal.FindFirst("provider")?.Value is not ("google" or "email")
                || string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(sid)
                || !await context.HttpContext.RequestServices.GetRequiredService<AccountService>()
                    .IsActiveAsync(sub, sid, context.HttpContext.RequestAborted))
                context.Fail("Account session is invalid or revoked.");
        }
    };
});
builder.Services.AddAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    });

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddRateLimiter(o =>
{
    // Login/credential attempts keep their own throttle in both modes.
    o.AddPolicy("account", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    o.AddPolicy("account-usage", http => TestModePolicy.IsEnabled(builder.Configuration)
        ? RateLimitPartition.GetNoLimiter("test-mode")
        : RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    o.AddPolicy("ai", http =>
    {
        if (TestModePolicy.IsEnabled(builder.Configuration))
            return RateLimitPartition.GetNoLimiter("test-mode");
        var keyId = http.User.FindFirst("keyId")?.Value;
        var partition = string.IsNullOrWhiteSpace(keyId)
            ? http.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : keyId;

        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    o.AddPolicy("app-attest", http =>
    {
        var partition = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    // A real iPhone requests a new assertion challenge for every protected operation.
    o.AddPolicy("app-attest-assertion", http => TestModePolicy.IsEnabled(builder.Configuration)
        ? RateLimitPartition.GetNoLimiter("test-mode")
        : RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
// Fail-closed default: every controller action requires an authenticated user unless it opts out
// with [AllowAnonymous] (legal, app-attest bootstrap, the App Store webhook, the login endpoints,
// and AI which self-authorizes via AiProtectionFilter). Applied to controller endpoints only (not a
// global FallbackPolicy) so unknown routes still return 404 instead of 401.
app.MapControllers().RequireAuthorization();
app.MapGet("/health", () => Results.Ok(new { ok = true })).AllowAnonymous();

app.Run();

static void ValidateProductionConfiguration(IConfiguration config)
{
    RequirePresent(config, "Jwt:Key");
    RequirePresent(config, "Jwt:Issuer");
    RequirePresent(config, "Jwt:Audience");
    RequirePresent(config, "Apple:TeamId");
    RequirePresent(config, "Apple:ClientId");
    RequirePresent(config, "Apple:AppStoreServer:BundleId");
    RequirePresent(config, "Apple:AppStoreServer:Environment");
    RequirePresent(config, "Apple:AppStoreServer:AllowedProductIds");

    var deviceMinutes = config.GetValue("Jwt:DeviceExpiresMinutes", DeviceAuth.DefaultLifetimeMinutes);
    if (deviceMinutes > 60)
        throw new InvalidOperationException("Jwt:DeviceExpiresMinutes must be 60 or less outside Development.");
}

static void RequirePresent(IConfiguration config, string key)
{
    var value = config[key]?.Trim();
    if (string.IsNullOrWhiteSpace(value)
        || value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{key} must be configured outside Development.");
    }
}

static IEnumerable<string> SplitCsv(string? value)
    => string.IsNullOrWhiteSpace(value)
        ? Array.Empty<string>()
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

public partial class Program { }
