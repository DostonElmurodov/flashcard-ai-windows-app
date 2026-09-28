# Task 5 review package
83be21b Harden AI request interpretation and atomic free word reservations
 Filters/AccountAiProtectionFilter.cs               |  29 +++
 Filters/AiProtectionFilter.cs                      |  43 +++-
 .../Services/AiRequestContextReader.cs             |  77 ++++++
 src/Mavrylo.Services/Services/DeviceWordService.cs |  69 +++---
 .../Services/WordAiService.Batch.cs                |   7 +-
 src/Mavrylo.Services/Services/WordAiService.cs     |  26 +-
 tests/AiProtectionFilterTests.cs                   |   5 +
 tests/AiRequestInterpretationTests.cs              | 275 +++++++++++++++++++++
 tests/DeviceWordServiceTests.cs                    |  12 -
 tests/PaidAccessAuditRegressionTests.cs            |  31 ++-
 tests/SharedSubscriptionPostgresTests.cs           |   9 +-
 tests/TestModeRouteTests.cs                        |  12 +-
 tests/TestSupport/FixedAiJsonService.cs            |  12 +
 13 files changed, 523 insertions(+), 84 deletions(-)
diff --git a/Filters/AccountAiProtectionFilter.cs b/Filters/AccountAiProtectionFilter.cs
index 5557c62..6e0d9cd 100644
--- a/Filters/AccountAiProtectionFilter.cs
+++ b/Filters/AccountAiProtectionFilter.cs
@@ -1,26 +1,55 @@
 using Mavrylo.Services;
 using Microsoft.AspNetCore.Mvc;
 using Microsoft.AspNetCore.Mvc.Filters;
+using Microsoft.Extensions.Options;
 
 namespace Mavrylo.Filters;
 
 public sealed class AccountAiProtectionFilter(AccountEntitlementService entitlements, AccountAiUsageService usage,
     IConfiguration configuration) : IAsyncResourceFilter
 {
     public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
     {
         var http = context.HttpContext;
         var account = http.User.FindFirst("sub")?.Value;
         if (account == null) { context.Result = new UnauthorizedResult(); return; }
+        if (!http.Request.Path.Value!.TrimEnd('/').EndsWith("/extract-words", StringComparison.OrdinalIgnoreCase))
+        {
+            if (!AiRequestContextReader.IsJsonContentType(http.Request.ContentType))
+            {
+                context.Result = new StatusCodeResult(StatusCodes.Status415UnsupportedMediaType);
+                return;
+            }
+            try
+            {
+                if (http.Request.ContentLength > 16000) throw new IOException("request body too large");
+                http.Request.EnableBuffering(bufferThreshold: 16000, bufferLimit: 16000);
+                using var body = new MemoryStream();
+                await http.Request.Body.CopyToAsync(body, http.RequestAborted);
+                http.Request.Body.Position = 0;
+                var options = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
+                AiRequestContextReader.Read(body.ToArray(), http.Request.Path.Value.TrimEnd('/').Split('/').Last().ToLowerInvariant(), options);
+            }
+            catch (InvalidOperationException ex)
+            {
+                context.Result = new BadRequestObjectResult(new { error = ex.Message });
+                return;
+            }
+            catch (IOException)
+            {
+                context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
+                return;
+            }
+        }
         if (TestModePolicy.IsEnabled(configuration))
         {
             await next();
             return;
         }
         var entitlement = await entitlements.GetAsync(account, http.RequestAborted);
         if (!AccountEntitlementService.IsActive(entitlement.Status))
         {
             context.Result = new ObjectResult(new { code = "subscription_required", error = "An active shared subscription is required." }) { StatusCode = 402 };
             return;
         }
         if (!await usage.TryConsumeAsync(account, http.RequestAborted))
diff --git a/Filters/AiProtectionFilter.cs b/Filters/AiProtectionFilter.cs
index cbb768e..098f611 100644
--- a/Filters/AiProtectionFilter.cs
+++ b/Filters/AiProtectionFilter.cs
@@ -25,73 +25,95 @@ namespace Mavrylo.Filters;
 /// </summary>
 public sealed class AiProtectionFilter(
     IOptions<AiProtectionOptions> options,
     IConfiguration config,
     IAppAttestVerifier verifier,
     ChallengeService challenges,
     DeviceContextService deviceContext,
     DeviceWordService deviceWords,
     AiUsageService usage,
     TimeProvider timeProvider,
     ILogger<AiProtectionFilter> logger) : IAsyncResourceFilter
 {
-    private const long MaxBufferedBodyBytes = 20_000_000;
+    private const long MaxImageBodyBytes = 20_000_000;
+    private const long MaxJsonBodyBytes = 16_000;
 
     public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
     {
         var opts = options.Value;
         var http = context.HttpContext;
         var path = http.Request.Path.Value ?? "";
+        var operation = path.TrimEnd('/').Split('/').Last().ToLowerInvariant();
 
         // 1) Device-JWT (aud=device). Validate and extract keyId. The ent claim is UI-only.
         var (keyId, tokenOk) = ValidateDeviceToken(http);
         if (!tokenOk || keyId is null)
         {
             context.Result = Problem(http, StatusCodes.Status401Unauthorized, "missing or invalid device token");
             return;
         }
 
         var resolved = await deviceContext.ResolveAsync(keyId, http.RequestAborted);
         if (resolved is null)
         {
             context.Result = Problem(http, StatusCodes.Status401Unauthorized, "unknown device");
             return;
         }
         var ent = resolved.Entitlement.Status;
 
         // Buffer the raw body once (needed for assertion hash and to let binding re-read it).
         byte[] body;
         try
         {
-            body = await ReadAndRewindBodyAsync(http);
+            body = await ReadAndRewindBodyAsync(http, operation == "extract-words" ? MaxImageBodyBytes : MaxJsonBodyBytes);
         }
         catch (IOException)
         {
             context.Result = Problem(http, StatusCodes.Status413PayloadTooLarge, "request body too large");
             return;
         }
 
         // 2) Assertion (App Attest). Skipped in Development for SIMULATOR-* keys, or if RequireAssertion is off.
         var isSimulator = keyId.StartsWith("SIMULATOR-", StringComparison.Ordinal);
         var skipAssertion = !opts.RequireAssertion || (verifier.IsDevelopmentBypassEnabled && isSimulator);
         if (!skipAssertion)
         {
             var assertionError = await VerifyAssertionAsync(http, keyId, path, body);
             if (assertionError is not null)
             {
                 context.Result = Problem(http, StatusCodes.Status403Forbidden, assertionError);
                 return;
             }
         }
 
+        AiRequestContextReader.WordContext? wordContext = null;
+        if (operation != "extract-words")
+        {
+            if (!AiRequestContextReader.IsJsonContentType(http.Request.ContentType))
+            {
+                context.Result = Problem(http, StatusCodes.Status415UnsupportedMediaType, "JSON content type is required");
+                return;
+            }
+            try
+            {
+                var jsonOptions = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
+                wordContext = AiRequestContextReader.Read(body, operation, jsonOptions);
+            }
+            catch (InvalidOperationException ex)
+            {
+                context.Result = Problem(http, StatusCodes.Status400BadRequest, ex.Message);
+                return;
+            }
+        }
+
         // Test access starts only after the device and its request proof are verified.
         if (TestModePolicy.IsEnabled(config))
         {
             await next();
             return;
         }
 
         // 3) Entitlement gate. Active states may call AI; expired/revoked → 402 (paywall).
         if (ent == "account_required")
         {
             context.Result = Problem(http, StatusCodes.Status402PaymentRequired, "Sign in to the subscription owner account.");
             return;
@@ -112,34 +134,33 @@ public sealed class AiProtectionFilter(
             if (!await http.RequestServices.GetRequiredService<AccountAiUsageService>().TryConsumeAsync(resolved.AccountId, http.RequestAborted))
             {
                 http.Response.Headers.RetryAfter = "60";
                 context.Result = Problem(http, StatusCodes.Status429TooManyRequests, "Account AI limit reached.");
                 return;
             }
             await next();
             return;
         }
         var isFree = string.Equals(ent, EntitlementService.Status.Free, StringComparison.Ordinal);
         if (isFree)
         {
-            var wordContext = DeviceWordService.TryReadWordContext(body);
             DeviceWordService.AiReservationResult reservation;
             try
             {
                 // Secondary review enriches an existing card and must never reserve another word.
                 reservation = path.TrimEnd('/').Equals("/owlai/ai/review-translation", StringComparison.OrdinalIgnoreCase)
-                    ? await deviceWords.CheckExistingAiWordAsync(keyId, wordContext.Word,
-                        wordContext.NativeLanguage, wordContext.LearningLanguage, http.RequestAborted)
-                    : await deviceWords.TryReserveAiSlotAsync(keyId, ent, wordContext.Word,
-                        wordContext.NativeLanguage, wordContext.LearningLanguage, http.RequestAborted);
+                    ? await deviceWords.CheckExistingAiWordAsync(keyId, wordContext?.Word,
+                        wordContext?.NativeLanguage, wordContext?.LearningLanguage, http.RequestAborted)
+                    : await deviceWords.TryReserveAiSlotAsync(keyId, ent, wordContext?.Word,
+                        wordContext?.NativeLanguage, wordContext?.LearningLanguage, http.RequestAborted);
             }
             catch (InvalidOperationException ex)
             {
                 context.Result = Problem(http, StatusCodes.Status400BadRequest, ex.Message);
                 return;
             }
             if (!reservation.Allowed)
             {
                 context.Result = Problem(http, StatusCodes.Status402PaymentRequired, reservation.Error ?? "free plan word limit reached");
                 return;
             }
         }
@@ -233,30 +254,30 @@ public sealed class AiProtectionFilter(
         return null;
     }
 
     private static bool IsEntitlementActiveForAi(string? ent) => ent switch
     {
         EntitlementService.Status.Free => true,    // free gets AI for the first 10 words (on-device gate)
         EntitlementService.Status.Trial => true,
         EntitlementService.Status.Premium => true,
         EntitlementService.Status.Grace => true,
         _ => false, // expired_trial / expired_paid / revoked → 402
     };
 
-    private static async Task<byte[]> ReadAndRewindBodyAsync(HttpContext http)
+    private static async Task<byte[]> ReadAndRewindBodyAsync(HttpContext http, long maxBodyBytes)
     {
-        if (http.Request.ContentLength is > MaxBufferedBodyBytes)
+        if (http.Request.ContentLength > maxBodyBytes)
             throw new IOException("request body too large");
 
-        http.Request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: MaxBufferedBodyBytes);
+        http.Request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: maxBodyBytes);
         using var ms = new MemoryStream();
-        await http.Request.Body.CopyToAsync(ms);
+        await http.Request.Body.CopyToAsync(ms, http.RequestAborted);
         http.Request.Body.Position = 0;
         return ms.ToArray();
     }
 
     private static ObjectResult Problem(HttpContext http, int status, string detail)
     {
         http.Response.StatusCode = status;
         return new ObjectResult(new { error = detail }) { StatusCode = status };
     }
 }
diff --git a/src/Mavrylo.Services/Services/AiRequestContextReader.cs b/src/Mavrylo.Services/Services/AiRequestContextReader.cs
new file mode 100644
index 0000000..d6d4219
--- /dev/null
+++ b/src/Mavrylo.Services/Services/AiRequestContextReader.cs
@@ -0,0 +1,77 @@
+using System.Text.Json;
+using Mavrylo.Dtos;
+using Microsoft.Net.Http.Headers;
+
+namespace Mavrylo.Services;
+
+/// <summary>Reads the same endpoint DTO and serializer options as MVC, before any usage is spent.
+/// Never rewrites the body: request proof and subsequent MVC binding use the original bytes.</summary>
+public static class AiRequestContextReader
+{
+    public sealed record WordContext(string Word, string? NativeLanguage, string? LearningLanguage);
+
+    public static bool IsJsonContentType(string? contentType)
+    {
+        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed)) return false;
+        var type = parsed.MediaType.Value!;
+        return type.Equals("application/json", StringComparison.OrdinalIgnoreCase)
+            || type.Equals("text/json", StringComparison.OrdinalIgnoreCase)
+            || (type.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
+                && type.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
+    }
+
+    public static WordContext Read(byte[] body, string operation, JsonSerializerOptions options)
+    {
+        try
+        {
+            using var document = JsonDocument.Parse(body, new JsonDocumentOptions
+            {
+                AllowTrailingCommas = options.AllowTrailingCommas,
+                CommentHandling = options.ReadCommentHandling,
+                MaxDepth = options.MaxDepth
+            });
+            if (document.RootElement.ValueKind != JsonValueKind.Object)
+                throw new InvalidOperationException("JSON object is required");
+            var names = new HashSet<string>(options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
+            foreach (var property in document.RootElement.EnumerateObject())
+                if (!names.Add(property.Name))
+                    throw new InvalidOperationException("duplicate JSON property");
+
+            WordContext context;
+            string? secondary = null;
+            var review = false;
+            switch (operation)
+            {
+                case "analyze-word":
+                    var analyze = document.RootElement.Deserialize<AnalyzeWordRequest>(options)!;
+                    context = new(analyze.Word, analyze.NativeLanguage, analyze.LearningLanguage);
+                    break;
+                case "word-detail":
+                    var detail = document.RootElement.Deserialize<WordDetailRequest>(options)!;
+                    context = new(detail.Word, detail.NativeLanguage, detail.LearningLanguage);
+                    secondary = detail.SecondaryLanguage;
+                    break;
+                case "review-translation":
+                    var request = document.RootElement.Deserialize<ReviewTranslationRequest>(options)!;
+                    context = new(request.Word, request.NativeLanguage, request.LearningLanguage);
+                    secondary = request.SecondaryLanguage;
+                    review = true;
+                    break;
+                default:
+                    throw new InvalidOperationException("unsupported AI operation");
+            }
+            if (!WordAiService.TryValidateWordRequest(context.Word, context.NativeLanguage, context.LearningLanguage,
+                    out var native, out _, out var error))
+                throw new InvalidOperationException(error);
+            if (review && (string.IsNullOrWhiteSpace(context.NativeLanguage) || string.IsNullOrWhiteSpace(context.LearningLanguage)))
+                throw new InvalidOperationException("native and learning languages are required");
+            if ((review || secondary != null) && !WordAiService.TryValidateSecondaryLanguage(secondary, native, out _, out error))
+                throw new InvalidOperationException(error);
+            return context;
+        }
+        catch (JsonException)
+        {
+            throw new InvalidOperationException("invalid AI JSON request");
+        }
+    }
+}
diff --git a/src/Mavrylo.Services/Services/DeviceWordService.cs b/src/Mavrylo.Services/Services/DeviceWordService.cs
index 6995085..619a84e 100644
--- a/src/Mavrylo.Services/Services/DeviceWordService.cs
+++ b/src/Mavrylo.Services/Services/DeviceWordService.cs
@@ -1,14 +1,14 @@
 using System.Data;
-using System.Text.Json;
+using Microsoft.EntityFrameworkCore.Storage;
 using Mavrylo.Data;
 using Mavrylo.Dtos;
 using Mavrylo.Models;
 using Microsoft.EntityFrameworkCore;
 
 namespace Mavrylo.Services;
 
 public sealed class DeviceWordService(AppDbContext db, TimeProvider timeProvider, IConfiguration? configuration = null)
 {
     public const int FreeLimit = 10;
 
     public async Task<string?> FindDeviceUuidAsync(string keyId, CancellationToken ct)
@@ -23,32 +23,32 @@ public sealed class DeviceWordService(AppDbContext db, TimeProvider timeProvider
     public async Task<DeviceWordMutationResponse> UpsertAsync(string deviceUuid, DeviceWordUpsertRequest request, string entitlement, CancellationToken ct)
     {
         var normalized = Normalize(request.NormalizedWord);
         var native = NormalizeLanguage(request.NativeLanguage);
         var learning = NormalizeLanguage(request.LearningLanguage);
         if (string.IsNullOrWhiteSpace(normalized))
             throw new InvalidOperationException("normalized_word is required");
         if (normalized.Length > SupportedLanguages.MaxWordLength)
             throw new InvalidOperationException("normalized_word is too long");
         if (!string.IsNullOrWhiteSpace(request.DisplayWord) && request.DisplayWord.Trim().Length > SupportedLanguages.MaxWordLength)
             throw new InvalidOperationException("display_word is too long");
 
-        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
+        await using var tx = await BeginWordMutationAsync(deviceUuid, ct);
         var existing = await FindExistingAsync(deviceUuid, request.ClientWordId, normalized, native, learning, ct);
-        if (existing is null && !TestModePolicy.IsEnabled(configuration)
+        if ((existing is null || !existing.IsActive) && !TestModePolicy.IsEnabled(configuration)
             && entitlement is not (EntitlementService.Status.Free
                 or EntitlementService.Status.Trial or EntitlementService.Status.Premium or EntitlementService.Status.Grace))
             return new DeviceWordMutationResponse(false, await CountActiveAsync(deviceUuid, ct), FreeLimit);
 
-        if (existing is null && IsFreeLimited(entitlement))
+        if ((existing is null || !existing.IsActive) && IsFreeLimited(entitlement))
         {
             var count = await CountActiveAsync(deviceUuid, ct);
             if (count >= FreeLimit)
                 return new DeviceWordMutationResponse(false, count, FreeLimit);
         }
 
         var now = timeProvider.GetUtcNow().UtcDateTime;
         var word = existing ?? new DeviceWordEntity
         {
             DeviceUuid = deviceUuid,
             CreatedAt = now
         };
@@ -70,134 +70,134 @@ public sealed class DeviceWordService(AppDbContext db, TimeProvider timeProvider
 
         await db.SaveChangesAsync(ct);
         var activeCount = await CountActiveAsync(deviceUuid, ct);
         await tx.CommitAsync(ct);
         return new DeviceWordMutationResponse(true, activeCount, FreeLimit);
     }
 
     public async Task<DeviceWordMutationResponse> DeleteAsync(string deviceUuid, DeviceWordDeleteRequest request, CancellationToken ct)
     {
         var normalized = Normalize(request.NormalizedWord ?? "");
         var native = NormalizeLanguage(request.NativeLanguage ?? "");
         var learning = NormalizeLanguage(request.LearningLanguage ?? "");
+        await using var tx = await BeginWordMutationAsync(deviceUuid, ct);
         var word = await FindExistingAsync(deviceUuid, request.ClientWordId, normalized, native, learning, ct);
         if (word is not null)
         {
             db.DeviceWords.Remove(word);
             await db.SaveChangesAsync(ct);
         }
 
-        return new DeviceWordMutationResponse(true, await CountActiveAsync(deviceUuid, ct), FreeLimit);
+        var count = await CountActiveAsync(deviceUuid, ct);
+        await tx.CommitAsync(ct);
+        return new DeviceWordMutationResponse(true, count, FreeLimit);
     }
 
     public async Task<AiReservationResult> TryReserveAiSlotAsync(
         string keyId,
         string entitlement,
         string? word,
         string? nativeLanguage,
         string? learningLanguage,
         CancellationToken ct)
     {
         if (!IsFreeLimited(entitlement))
             return AiReservationResult.Allow();
 
         var deviceUuid = await FindDeviceUuidAsync(keyId, ct);
         if (deviceUuid is null)
             return AiReservationResult.Deny("device not registered");
 
         var normalized = Normalize(word ?? "");
         var native = NormalizeLanguage(nativeLanguage ?? "en");
         var learning = NormalizeLanguage(learningLanguage ?? "en");
         if (normalized.Length > SupportedLanguages.MaxWordLength)
             return AiReservationResult.Deny("word is too long");
 
-        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
+        await using var tx = await BeginWordMutationAsync(deviceUuid, ct);
+        DeviceWordEntity? existing = null;
         if (!string.IsNullOrWhiteSpace(normalized))
         {
-            var exists = await db.DeviceWords.AnyAsync(w =>
-                w.DeviceUuid == deviceUuid
-                && w.NormalizedWord == normalized
-                && w.NativeLanguage == native
-                && w.LearningLanguage == learning
-                && w.IsActive, ct);
-            if (exists)
+            existing = await db.DeviceWords.FirstOrDefaultAsync(w =>
+                w.DeviceUuid == deviceUuid && w.NormalizedWord == normalized
+                && w.NativeLanguage == native && w.LearningLanguage == learning, ct);
+            if (existing?.IsActive == true)
             {
                 await tx.CommitAsync(ct);
                 return AiReservationResult.Allow();
             }
         }
 
         var count = await CountActiveAsync(deviceUuid, ct);
         if (count >= FreeLimit)
             return AiReservationResult.Deny("free plan word limit reached");
 
         // Reserve a slot before the expensive AI call, so a custom client cannot bypass the
         // backend limit by calling AI repeatedly and never syncing the resulting word.
         if (!string.IsNullOrWhiteSpace(normalized))
         {
-            db.DeviceWords.Add(new DeviceWordEntity
+            var now = timeProvider.GetUtcNow().UtcDateTime;
+            var reserved = existing ?? new DeviceWordEntity
             {
                 DeviceUuid = deviceUuid,
                 NormalizedWord = normalized,
                 DisplayWord = word?.Trim() ?? normalized,
                 NativeLanguage = native,
                 LearningLanguage = learning,
-                IsActive = true,
-                CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
-                UpdatedAt = timeProvider.GetUtcNow().UtcDateTime
-            });
+                CreatedAt = now
+            };
+            reserved.IsActive = true;
+            reserved.UpdatedAt = now;
+            if (existing is null) db.DeviceWords.Add(reserved);
             await db.SaveChangesAsync(ct);
         }
 
         await tx.CommitAsync(ct);
         return AiReservationResult.Allow();
     }
 
     public async Task<AiReservationResult> CheckExistingAiWordAsync(
         string keyId, string? word, string? nativeLanguage, string? learningLanguage, CancellationToken ct)
     {
         var normalized = Normalize(word ?? "");
         if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > SupportedLanguages.MaxWordLength)
             throw new InvalidOperationException("a valid word is required");
         if (string.IsNullOrWhiteSpace(nativeLanguage) || string.IsNullOrWhiteSpace(learningLanguage))
             throw new InvalidOperationException("native and learning languages are required");
         var native = NormalizeLanguage(nativeLanguage);
         var learning = NormalizeLanguage(learningLanguage);
         var deviceUuid = await FindDeviceUuidAsync(keyId, ct);
         var exists = deviceUuid != null && await db.DeviceWords.AsNoTracking().AnyAsync(w =>
             w.DeviceUuid == deviceUuid && w.NormalizedWord == normalized
             && w.NativeLanguage == native && w.LearningLanguage == learning && w.IsActive, ct);
         return exists ? AiReservationResult.Allow() : AiReservationResult.Deny("review requires an existing active word");
     }
 
-    public static (string? Word, string? NativeLanguage, string? LearningLanguage) TryReadWordContext(byte[] body)
+    private async Task<IDbContextTransaction> BeginWordMutationAsync(string deviceUuid, CancellationToken ct)
     {
-        if (body.Length == 0)
-            return (null, null, null);
-
+        // PostgreSQL's READ COMMITTED refreshes the snapshot after waiting for the per-device
+        // lock. SERIALIZABLE takes a stale snapshot at lock acquisition and can abort the loser.
+        var tx = await db.Database.BeginTransactionAsync(
+            db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, ct);
         try
         {
-            using var doc = JsonDocument.Parse(body);
-            var root = doc.RootElement;
-            if (root.ValueKind != JsonValueKind.Object)
-                return (null, null, null);
-            return (
-                ReadString(root, "word"),
-                ReadString(root, "native_language") ?? ReadString(root, "nativeLanguage"),
-                ReadString(root, "learning_language") ?? ReadString(root, "learningLanguage")
-            );
+            if (db.Database.IsNpgsql())
+                await db.Database.ExecuteSqlInterpolatedAsync(
+                    $"SELECT pg_advisory_xact_lock(hashtextextended({"device-words:" + deviceUuid}, 0))", ct);
+            return tx;
         }
-        catch (JsonException)
+        catch
         {
-            return (null, null, null);
+            await tx.DisposeAsync();
+            throw;
         }
     }
 
     private async Task<DeviceWordEntity?> FindExistingAsync(
         string deviceUuid,
         string? clientWordId,
         string normalized,
         string native,
         string learning,
         CancellationToken ct)
     {
         if (!string.IsNullOrWhiteSpace(clientWordId))
@@ -227,21 +227,18 @@ public sealed class DeviceWordService(AppDbContext db, TimeProvider timeProvider
         => value.Trim().ToLowerInvariant();
 
     private static string NormalizeLanguage(string value)
     {
         if (SupportedLanguages.TryNormalize(value, out var normalized))
             return normalized;
         throw new InvalidOperationException("unsupported language");
     }
 
     private static string? TrimToNull(string? value)
         => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
 
-    private static string? ReadString(JsonElement root, string name)
-        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
-
     public sealed record AiReservationResult(bool Allowed, string? Error)
     {
         public static AiReservationResult Allow() => new(true, null);
         public static AiReservationResult Deny(string error) => new(false, error);
     }
 }
diff --git a/src/Mavrylo.Services/Services/WordAiService.Batch.cs b/src/Mavrylo.Services/Services/WordAiService.Batch.cs
index 08bbc62..f327b51 100644
--- a/src/Mavrylo.Services/Services/WordAiService.Batch.cs
+++ b/src/Mavrylo.Services/Services/WordAiService.Batch.cs
@@ -1,28 +1,25 @@
 using System.Text.Json;
 using System.Text.Json.Nodes;
 using Mavrylo.Dtos;
 using Mavrylo.Models;
 
 namespace Mavrylo.Services;
 
 public sealed partial class WordAiService
 {
     private async Task<AiResult> WordDetailWithSecondaryAsync(WordDetailRequest req, string native, string source, CancellationToken ct)
     {
-        if (string.IsNullOrWhiteSpace(req.SecondaryLanguage)
-            || !SupportedLanguages.TryNormalize(req.SecondaryLanguage, out var target))
-            return new(400, new { error = "unsupported secondary language" });
-        if (EquivalentLanguage(native, target))
-            return new(400, new { error = "secondary language must differ from native language" });
+        if (!TryValidateSecondaryLanguage(req.SecondaryLanguage, native, out var target, out var error))
+            return new(400, new { error });
 
         var word = TranslationCacheKey.NormalizeWord(req.Word);
         var reviewSource = source == "en" ? "en-us" : source;
         target = target == "en" ? "en-us" : target;
         var primary = await CachedPrimaryAsync(word, native, source, req.Word, ct);
         if (primary == null)
         {
             var review = await translationCache.TryGetAsync(word, native == "en" ? "en-us" : native, reviewSource, TranslationCacheEntity.Kinds.ReviewTranslation, ct);
             if (review != null && TryNormalizeReviewTranslation(review, native, out var normalizedReview))
             {
                 var translation = JsonNode.Parse(normalizedReview)!["translation"]!.GetValue<string>();
                 primary = JsonSerializer.Serialize(new { corrected_word = req.Word.Trim(), translation, translations = new[] { translation } });
diff --git a/src/Mavrylo.Services/Services/WordAiService.cs b/src/Mavrylo.Services/Services/WordAiService.cs
index 826c333..ca1a033 100644
--- a/src/Mavrylo.Services/Services/WordAiService.cs
+++ b/src/Mavrylo.Services/Services/WordAiService.cs
@@ -92,29 +92,26 @@ example_translations (array of 1 translation in {nativeLangName})";
         if (el == null) return AiUnavailable();
         var raw = NormalizeWordDetailJson(el.Value.GetRawText(), req.Word);
         await translationCache.SaveAsync(norm, nl, tl, TranslationCacheEntity.Kinds.WordDetail, raw, ct: ct);
         return new AiResult(200, Json: raw);
     }
 
     public async Task<AiResult> ReviewTranslationAsync(ReviewTranslationRequest req, CancellationToken ct)
     {
         if (!TryValidateWordRequest(req.Word, req.NativeLanguage, req.LearningLanguage, out var native, out var source, out var error))
             return new AiResult(400, new { error });
         if (string.IsNullOrWhiteSpace(req.NativeLanguage) || string.IsNullOrWhiteSpace(req.LearningLanguage))
             return new AiResult(400, new { error = "native and learning languages are required" });
-        if (string.IsNullOrWhiteSpace(req.SecondaryLanguage)
-            || !SupportedLanguages.TryNormalize(req.SecondaryLanguage, out var target))
-            return new AiResult(400, new { error = "unsupported secondary language" });
-        if (EquivalentLanguage(native, target))
-            return new AiResult(400, new { error = "secondary language must differ from native language" });
+        if (!TryValidateSecondaryLanguage(req.SecondaryLanguage, native, out var target, out error))
+            return new AiResult(400, new { error });
 
         // English aliases represent the same supported language for this new contract.
         source = source == "en" ? "en-us" : source;
         target = target == "en" ? "en-us" : target;
         var word = TranslationCacheKey.NormalizeWord(req.Word);
         // The cache's explanation/native column holds the requested secondary target.
         // Native language only identifies original card access, never the translation pair.
         var cached = await translationCache.TryGetAsync(word, target, source, TranslationCacheEntity.Kinds.ReviewTranslation, ct);
         if (cached != null && TryNormalizeReviewTranslation(cached, target, out var cachedJson))
             return new AiResult(200, Json: cachedJson);
         TranslationCacheMetrics.RecordMiss(TranslationCacheEntity.Kinds.ReviewTranslation);
 
@@ -164,24 +161,41 @@ Do not include pronunciation, audio, or additional fields.";
         }
     }
 
     private static bool ReadNonblankString(JsonElement root, string name, int maxLength, out string text)
     {
         text = "";
         if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
             return false;
         text = value.GetString()!.Trim();
         return text.Length > 0 && text.Length <= maxLength;
     }
 
+    internal static bool TryValidateSecondaryLanguage(string? secondary, string native, out string target, out string error)
+    {
+        error = "unsupported secondary language";
+        if (string.IsNullOrWhiteSpace(secondary) || !SupportedLanguages.TryNormalize(secondary, out target))
+        {
+            target = "";
+            return false;
+        }
+        if (EquivalentLanguage(native, target))
+        {
+            error = "secondary language must differ from native language";
+            return false;
+        }
+        error = "";
+        return true;
+    }
+
     private static bool EquivalentLanguage(string left, string right)
         => (left == "en-us" ? "en" : left) == (right == "en-us" ? "en" : right);
 
     public async Task<AiResult> ExtractWordsAsync(byte[] imageBytes, string? targetLanguage, string? nativeLanguage, CancellationToken ct)
     {
         if (imageBytes.Length == 0) return new AiResult(400, "image required");
         if (!SupportedLanguages.TryNormalize(targetLanguage ?? "en", out var targetLang))
             return new AiResult(400, new { error = "unsupported target language" });
         if (!SupportedLanguages.TryNormalize(nativeLanguage ?? "en", out var nativeLang))
             return new AiResult(400, new { error = "unsupported native language" });
 
         if (!TryDetectImageMime(imageBytes, out var mime))
@@ -228,25 +242,25 @@ Only nouns, verbs, adjectives, adverbs in base form; skip articles and numbers."
             .Where(x => x.Length > 0)
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .Take(12)
             .ToArray();
 
         if (values.Length == 0) return node.ToJsonString();
 
         node["translations"] = new JsonArray(values.Select(v => JsonValue.Create(v)).ToArray());
         node["translation"] = values[0];
         return node.ToJsonString();
     }
 
-    private static bool TryValidateWordRequest(
+    internal static bool TryValidateWordRequest(
         string? word,
         string? nativeLanguage,
         string? learningLanguage,
         out string native,
         out string learning,
         out string error)
     {
         native = "en-us";
         learning = "en-us";
         error = "";
 
         var trimmedWord = word?.Trim();
diff --git a/tests/AiProtectionFilterTests.cs b/tests/AiProtectionFilterTests.cs
index 5b89c06..d6bc729 100644
--- a/tests/AiProtectionFilterTests.cs
+++ b/tests/AiProtectionFilterTests.cs
@@ -1,12 +1,13 @@
+using System.Text.Json;
 using System.Text;
 using Mavrylo.Data;
 using Mavrylo.Filters;
 using Mavrylo.Models;
 using Mavrylo.Services;
 using Mavrylo.Tests.TestSupport;
 using Microsoft.AspNetCore.Http;
 using Microsoft.AspNetCore.Mvc;
 using Microsoft.AspNetCore.Mvc.Abstractions;
 using Microsoft.AspNetCore.Mvc.Filters;
 using Microsoft.AspNetCore.Mvc.ModelBinding;
 using Microsoft.AspNetCore.Routing;
@@ -340,29 +341,33 @@ public class AiProtectionFilterTests
             TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode }),
             verifier ?? new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true },
             new ChallengeService(db, timeProvider),
             new DeviceContextService(db, new EntitlementService(db, timeProvider)),
             new DeviceWordService(db, timeProvider),
             new AiUsageService(db, timeProvider),
             timeProvider,
             NullLogger<AiProtectionFilter>.Instance);
     }
 
     private static ResourceExecutingContext CreateContext(AppDbContext db, string body, string? bearerToken = null)
     {
+        var jsonOptions = new JsonOptions();
+        jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
         var services = new ServiceCollection()
             .AddSingleton(db)
+            .AddSingleton<IOptions<JsonOptions>>(Options.Create(jsonOptions))
             .BuildServiceProvider();
         var http = new DefaultHttpContext { RequestServices = services };
         http.Request.Method = "POST";
+        http.Request.ContentType = "application/json";
         http.Request.Path = "/owlai/ai/word-detail";
         http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
         http.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
         if (bearerToken is not null)
             http.Request.Headers.Authorization = $"Bearer {bearerToken}";
 
         var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
         return new ResourceExecutingContext(
             actionContext,
             [],
             new List<IValueProviderFactory>());
     }
diff --git a/tests/AiRequestInterpretationTests.cs b/tests/AiRequestInterpretationTests.cs
new file mode 100644
index 0000000..e8a1ef8
--- /dev/null
+++ b/tests/AiRequestInterpretationTests.cs
@@ -0,0 +1,275 @@
+using System.Formats.Cbor;
+using System.Security.Cryptography;
+using Microsoft.Extensions.Logging.Abstractions;
+using System.Net;
+using System.Text;
+using System.Text.Json;
+using Mavrylo.Data;
+using Mavrylo.Dtos;
+using Mavrylo.Models;
+using Mavrylo.Services;
+using Mavrylo.Tests.TestSupport;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.DependencyInjection.Extensions;
+using Xunit;
+
+namespace Mavrylo.Tests;
+
+public sealed class AiRequestInterpretationTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
+{
+    public static IEnumerable<object[]> InvalidBodies()
+    {
+        foreach (var route in new[] { "word-detail", "analyze-word", "review-translation" })
+        foreach (var body in new[] { "{}", "{\"word\":\" \"}", "{\"word\":null}", "{\"word\":42}", "{\"word\":{}}", "{", "[]",
+            "{\"word\":\"one\",\"Word\":\"two\"}", "{\"WORD\":\"one\",\"word\":\"one\"}",
+            "{\"word\":\"one\",\"native_language\":\"en\",\"NATIVE_LANGUAGE\":\"es\"}",
+            "{\"word\":\"one\",\"learning_language\":42}", "{\"word\":\"one\",\"native_language\":\"invalid\"}",
+            "{\"word\":\"one\",\"secondary_language\":42}" })
+            if (route != "analyze-word" || !body.Contains("secondary_language"))
+                yield return new object[] { route, body };
+    }
+
+    [Theory, MemberData(nameof(InvalidBodies))]
+    public async Task InvalidJsonNeverReservesWordOrConsumesUsage(string route, string body)
+    {
+        var provider = new Provider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient();
+        using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var key = await Device(db, client, scope);
+        using var response = await client.PostAsync("/owlai/ai/" + route, new StringContent(body, Encoding.UTF8, "application/json"));
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+        Assert.Equal(0, provider.Calls);
+        Assert.False(await db.DeviceWords.AnyAsync(w => w.DeviceUuid == key));
+        Assert.False(await db.AiUsage.AnyAsync(w => w.KeyId == key));
+    }
+
+    [Theory]
+    [InlineData("word", "native_language", "learning_language")]
+    [InlineData("Word", "Native_Language", "Learning_Language")]
+    [InlineData("WORD", "NATIVE_LANGUAGE", "LEARNING_LANGUAGE")]
+    [InlineData("Word", "nativeLanguage", "learningLanguage")]
+    public async Task LanguagesAndRepeatedWordUseSameMeaningAsMvc(string wordProperty, string nativeProperty, string learningProperty)
+    {
+        var provider = new Provider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient();
+        using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var key = await Device(db, client, scope);
+        var body = JsonSerializer.Serialize(new Dictionary<string, string> { [wordProperty] = key, [nativeProperty] = "fr", [learningProperty] = "es" });
+        for (var i = 0; i < 2; i++)
+        {
+            using var response = await client.PostAsync("/owlai/ai/word-detail", new StringContent(body, Encoding.UTF8, "application/json"));
+            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        }
+        var row = await db.DeviceWords.SingleAsync(w => w.DeviceUuid == key);
+        // CamelCase fields are unknown to MVC's snake_case contract: defaults must also match in the limiter.
+        Assert.Equal(nativeProperty == "nativeLanguage" ? "en" : "fr", row.NativeLanguage);
+        Assert.Equal(learningProperty == "learningLanguage" ? "en" : "es", row.LearningLanguage);
+        Assert.Equal(2, await db.AiUsage.Where(w => w.KeyId == key).SumAsync(w => w.Count));
+        Assert.Equal(1, provider.Calls); // Second operation is served from the real translation cache.
+    }
+
+    [Theory]
+    [InlineData(9, HttpStatusCode.OK, 1)]
+    [InlineData(10, HttpStatusCode.PaymentRequired, 0)]
+    public async Task MultipartExtractionHasExplicitNoWordPolicy(int words, HttpStatusCode expected, int calls)
+    {
+        var provider = new Provider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient();
+        using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var key = await Device(db, client, scope);
+        for (var i = 0; i < words; i++) db.DeviceWords.Add(new() { DeviceUuid = key, NormalizedWord = "word" + i, NativeLanguage = "en", LearningLanguage = "es", IsActive = true });
+        await db.SaveChangesAsync();
+        using var form = new MultipartFormDataContent();
+        form.Add(new ByteArrayContent(new byte[] { 0xff, 0xd8, 0xff, 1 }), "image", "test.jpg");
+        using var response = await client.PostAsync("/owlai/ai/extract-words", form);
+        Assert.Equal(expected, response.StatusCode);
+        Assert.Equal(calls, provider.Calls);
+        Assert.Equal(words, await db.DeviceWords.CountAsync(w => w.DeviceUuid == key));
+        Assert.Equal(calls, await db.AiUsage.Where(w => w.KeyId == key).SumAsync(w => w.Count));
+    }
+
+    [Theory]
+    [InlineData("word-detail")]
+    [InlineData("analyze-word")]
+    [InlineData("review-translation")]
+    public async Task AccountDuplicateBodyDoesNotSpendQuota(string route)
+    {
+        var provider = new Provider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient();
+        using var scope = factory.Services.CreateScope();
+        var session = await scope.ServiceProvider.GetRequiredService<AccountService>().SignInAsync(new(Guid.NewGuid().ToString("N"), null, "Owner"), default, allowCreation: true);
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        db.Subscriptions.Add(new() { OriginalTransactionId = Guid.NewGuid().ToString("N"), DeviceUuid = "owner-device", OwnerAccountId = session.Profile.Id,
+            ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(5), WasEverPaid = true, Environment = "Production" });
+        await db.SaveChangesAsync();
+        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
+        using var response = await client.PostAsync("/owlai/account/ai/" + route,
+            new StringContent("{\"word\":\"one\",\"Word\":\"two\",\"native_language\":\"en\",\"learning_language\":\"es\",\"secondary_language\":\"fr\"}", Encoding.UTF8, "application/json"));
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+        Assert.Equal(0, provider.Calls);
+        Assert.False(await db.AiUsage.AnyAsync(w => w.KeyId == "account:" + session.Profile.Id));
+    }
+
+    [Theory]
+    [InlineData("text/plain", false, HttpStatusCode.UnsupportedMediaType)]
+    [InlineData("application/json", true, HttpStatusCode.RequestEntityTooLarge)]
+    public async Task InvalidHttpEnvelopeDoesNotConsumeQuota(string contentType, bool oversized, HttpStatusCode expected)
+    {
+        var provider = new Provider(); await using var factory = Factory(provider);
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var key = await Device(db, client, scope);
+        var body = "{\"word\":\"" + key + "\",\"padding\":\"" + (oversized ? new string('x', 16000) : "") + "\"}";
+        using var response = await client.PostAsync("/owlai/ai/word-detail", new StringContent(body, Encoding.UTF8, contentType));
+        Assert.Equal(expected, response.StatusCode);
+        Assert.Equal(0, provider.Calls);
+        Assert.False(await db.DeviceWords.AnyAsync(w => w.DeviceUuid == key));
+        Assert.False(await db.AiUsage.AnyAsync(w => w.KeyId == key));
+    }
+
+    [Theory]
+    [InlineData("word")]
+    [InlineData("Word")]
+    [InlineData("WORD")]
+    public async Task ReviewAtTenWordsUsesExistingWordAndConsumesQuota(string property)
+    {
+        var provider = new Provider(); await using var factory = Factory(provider);
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var key = await Device(db, client, scope);
+        for (var i = 0; i < 10; i++) db.DeviceWords.Add(new() { DeviceUuid = key, NormalizedWord = key + i, NativeLanguage = "en", LearningLanguage = "es", IsActive = true });
+        await db.SaveChangesAsync();
+        var body = JsonSerializer.Serialize(new Dictionary<string, string> { [property] = key + 0, ["NATIVE_LANGUAGE"] = "en", ["LEARNING_LANGUAGE"] = "es", ["SECONDARY_LANGUAGE"] = "fr" });
+        using var response = await client.PostAsync("/owlai/ai/review-translation", new StringContent(body, Encoding.UTF8, "application/json"));
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        Assert.Equal(10, await db.DeviceWords.CountAsync(w => w.DeviceUuid == key));
+        Assert.Equal(1, await db.AiUsage.Where(w => w.KeyId == key).SumAsync(w => w.Count));
+        Assert.Equal(1, provider.Calls);
+    }
+
+    [Theory]
+    [InlineData(false)]
+    [InlineData(true)]
+    public async Task RealAssertionStillVerifiesOriginalUppercaseBytes(bool mutateBody)
+    {
+        var provider = new Provider();
+        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
+        var verifier = new AppAttestVerifier(TestConfig.Create(), new FakeEnvironment("Production"), NullLogger<AppAttestVerifier>.Instance);
+        await using var factory = new ApiFactory(postgres.ConnectionString, services =>
+        {
+            services.RemoveAll<IAiJsonService>(); services.AddSingleton<IAiJsonService>(provider);
+            services.RemoveAll<IAppAttestVerifier>(); services.AddSingleton<IAppAttestVerifier>(verifier);
+        }, new Dictionary<string, string?> { ["TestMode:Enabled"] = "false", ["AiProtection:RequireAssertion"] = "true", ["AiProtection:FreeDailyQuota"] = "40" });
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var key = await Device(db, client, scope);
+        var device = await db.Devices.SingleAsync(d => d.KeyId == key); device.PublicKey = signing.ExportSubjectPublicKeyInfo(); await db.SaveChangesAsync();
+        const string path = "/owlai/ai/WORD-DETAIL";
+        var body = "{  \"WORD\":\"" + key + "\", \"NATIVE_LANGUAGE\":\"en\", \"LEARNING_LANGUAGE\":\"es\" }";
+        var challenge = await scope.ServiceProvider.GetRequiredService<ChallengeService>().IssueAssertionAsync(key, path);
+        var clientHash = AppAttestClientData.ComputeAssertionHash(challenge.Nonce, Encoding.UTF8.GetBytes(body), path, challenge.ChallengeId);
+        var authData = new byte[37]; SHA256.HashData(Encoding.UTF8.GetBytes("TEAMID1234.com.mavrylo.owlai")).CopyTo(authData, 0); authData[36] = 1;
+        var signature = signing.SignData(authData.Concat(clientHash).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
+        var writer = new CborWriter(); writer.WriteStartMap(2); writer.WriteTextString("signature"); writer.WriteByteString(signature);
+        writer.WriteTextString("authenticatorData"); writer.WriteByteString(authData); writer.WriteEndMap();
+        using var request = new HttpRequestMessage(HttpMethod.Post, path);
+        request.Headers.Add("X-App-Attest-Key-Id", key); request.Headers.Add("X-App-Attest-Challenge-Id", challenge.ChallengeId);
+        request.Headers.Add("X-App-Attest-Assertion", Convert.ToBase64String(writer.Encode()));
+        request.Content = new StringContent(mutateBody ? body + " " : body, Encoding.UTF8, "application/json");
+        using var response = await client.SendAsync(request);
+        Assert.Equal(mutateBody ? HttpStatusCode.Forbidden : HttpStatusCode.OK, response.StatusCode);
+        Assert.Equal(mutateBody ? 0 : 1, provider.Calls);
+        Assert.Equal(mutateBody ? 0 : 1, await db.DeviceWords.CountAsync(w => w.DeviceUuid == key));
+        Assert.Equal(mutateBody ? 0 : 1, await db.AiUsage.Where(w => w.KeyId == key).SumAsync(w => w.Count));
+    }
+
+    private ApiFactory Factory(Provider provider, bool testMode = false) => new(postgres.ConnectionString,
+        services => { services.RemoveAll<IAiJsonService>(); services.AddSingleton<IAiJsonService>(provider); },
+        new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode.ToString(), ["AiProtection:RequireAssertion"] = "false", ["AiProtection:FreeDailyQuota"] = "40" });
+
+    private static async Task<string> Device(AppDbContext db, HttpClient client, IServiceScope scope)
+    {
+        var key = Guid.NewGuid().ToString("N");
+        db.Devices.Add(new() { KeyId = key, DeviceUuid = key }); await db.SaveChangesAsync();
+        client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(key, "free").Token);
+        return key;
+    }
+
+    private sealed class Provider : IAiJsonService
+    {
+        public int Calls;
+        public Task<JsonElement?> CompleteJsonAsync(string operation, string prompt, CancellationToken ct = default)
+        {
+            Calls++;
+            return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { translations = new[] { "sample" }, translation = "sample", corrected_word = "sample", words = new[] { "sample" }, language_code = "fr", explanation = "sample" }));
+        }
+        public Task<JsonElement?> CompleteVisionJsonAsync(string operation, string prompt, byte[] image, string mime, CancellationToken ct = default) => CompleteJsonAsync(operation, prompt, ct);
+    }
+}
+
+public sealed class DeviceWordConcurrencyTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
+{
+    private AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options);
+    private static DeviceWordUpsertRequest Request(string word) => new(null, word, word, "en", "es", null, null, null, null);
+
+    [Theory]
+    [InlineData(false, false)]
+    [InlineData(false, true)]
+    [InlineData(true, true)]
+    public async Task LastSlotAcrossSeparateContextsHasOneWinnerAndPredictableDenial(bool firstUpsert, bool secondUpsert)
+    {
+        await using var setup = Database(); await setup.Database.MigrateAsync();
+        for (var attempt = 0; attempt < 10; attempt++)
+        {
+            var key = Guid.NewGuid().ToString("N");
+            setup.Devices.Add(new() { KeyId = key, DeviceUuid = key });
+            for (var i = 0; i < 9; i++) setup.DeviceWords.Add(new() { DeviceUuid = key, NormalizedWord = "word" + i, NativeLanguage = "en", LearningLanguage = "es", IsActive = true });
+            await setup.SaveChangesAsync();
+            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
+            async Task<bool> Mutate(string word, bool upsert)
+            {
+                await using var db = Database(); var service = new DeviceWordService(db, TimeProvider.System);
+                await gate.Task;
+                return upsert ? (await service.UpsertAsync(key, Request(word), "free", default)).Accepted
+                    : (await service.TryReserveAiSlotAsync(key, "free", word, "en", "es", default)).Allowed;
+            }
+            var first = Mutate("left", firstUpsert); var second = Mutate("right", secondUpsert); gate.SetResult();
+            var outcomes = await Task.WhenAll(first, second);
+            Assert.Equal(1, outcomes.Count(x => x));
+            Assert.Equal(10, await setup.DeviceWords.CountAsync(w => w.DeviceUuid == key && w.IsActive));
+        }
+    }
+
+    [Theory]
+    [InlineData(9, true)]
+    [InlineData(10, false)]
+    public async Task AiReservationOfInactiveWordRespectsRemainingSlot(int activeWords, bool allowed)
+    {
+        await using var db = Database(); await db.Database.MigrateAsync(); var key = Guid.NewGuid().ToString("N");
+        db.Devices.Add(new() { KeyId = key, DeviceUuid = key });
+        for (var i = 0; i < activeWords; i++) db.DeviceWords.Add(new() { DeviceUuid = key, NormalizedWord = "word" + i, NativeLanguage = "en", LearningLanguage = "es", IsActive = true });
+        db.DeviceWords.Add(new() { DeviceUuid = key, NormalizedWord = "inactive", NativeLanguage = "en", LearningLanguage = "es", IsActive = false });
+        await db.SaveChangesAsync();
+        var result = await new DeviceWordService(db, TimeProvider.System).TryReserveAiSlotAsync(key, "free", "inactive", "en", "es", default);
+        Assert.Equal(allowed, result.Allowed);
+        Assert.Equal(10, await db.DeviceWords.CountAsync(w => w.DeviceUuid == key && w.IsActive));
+        Assert.Equal(activeWords + 1, await db.DeviceWords.CountAsync(w => w.DeviceUuid == key));
+    }
+
+    [Fact]
+    public async Task InactiveWordCannotReactivatePastFreeLimit()
+    {
+        await using var db = Database(); await db.Database.MigrateAsync(); var key = Guid.NewGuid().ToString("N");
+        for (var i = 0; i < 10; i++) db.DeviceWords.Add(new() { DeviceUuid = key, NormalizedWord = "word" + i, NativeLanguage = "en", LearningLanguage = "es", IsActive = true });
+        db.DeviceWords.Add(new() { DeviceUuid = key, NormalizedWord = "inactive", NativeLanguage = "en", LearningLanguage = "es", IsActive = false });
+        await db.SaveChangesAsync();
+        var result = await new DeviceWordService(db, TimeProvider.System).UpsertAsync(key, Request("inactive"), "free", default);
+        Assert.False(result.Accepted);
+        Assert.Equal(10, await db.DeviceWords.CountAsync(w => w.DeviceUuid == key && w.IsActive));
+    }
+}
diff --git a/tests/DeviceWordServiceTests.cs b/tests/DeviceWordServiceTests.cs
index 311c1a6..a9551e0 100644
--- a/tests/DeviceWordServiceTests.cs
+++ b/tests/DeviceWordServiceTests.cs
@@ -112,27 +112,15 @@ public class DeviceWordServiceTests
     public async Task Delete_RemovesMatchingWord()
     {
         using var testDb = TestDb.Create();
         var service = new DeviceWordService(testDb.Db, TimeProvider.System);
         await service.UpsertAsync("device", Request("hello", "client-1"), EntitlementService.Status.Free, CancellationToken.None);
 
         var result = await service.DeleteAsync("device", new DeviceWordDeleteRequest("client-1", null, null, null), CancellationToken.None);
 
         Assert.True(result.Accepted);
         Assert.Equal(0, result.ActiveWordCount);
     }
 
-    [Fact]
-    public void TryReadWordContext_HandlesCamelSnakeAndInvalidJson()
-    {
-        var snake = DeviceWordService.TryReadWordContext("""{"word":"hola","native_language":"en","learning_language":"es"}"""u8.ToArray());
-        var camel = DeviceWordService.TryReadWordContext("""{"word":"hola","nativeLanguage":"en","learningLanguage":"es"}"""u8.ToArray());
-        var invalid = DeviceWordService.TryReadWordContext("not json"u8.ToArray());
-
-        Assert.Equal(("hola", "en", "es"), snake);
-        Assert.Equal(("hola", "en", "es"), camel);
-        Assert.Null(invalid.Word);
-    }
-
     private static DeviceWordUpsertRequest Request(string word, string? clientId = null) =>
         new(clientId, word, word, "en", "es", "translation", "pron", "noun", "{}");
 }
diff --git a/tests/PaidAccessAuditRegressionTests.cs b/tests/PaidAccessAuditRegressionTests.cs
index e1fc5a7..7ffd80f 100644
--- a/tests/PaidAccessAuditRegressionTests.cs
+++ b/tests/PaidAccessAuditRegressionTests.cs
@@ -232,84 +232,105 @@ public sealed class PaidAccessAuditRegressionTests
     private static async Task AddDevice(AppDbContext db, string key, string device)
     {
         db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = device, PublicKey = [1, 2, 3] });
         await db.SaveChangesAsync();
     }
 
     // The action represents one synthetic provider attempt. Every rejection must stop before it.
     private static async Task<(int Status, int ProviderCalls)> InvokeAi(AppDbContext db, string key, int quota)
     {
         var filter = new AiProtectionFilter(Options.Create(new AiProtectionOptions { RequireAssertion = true, FreeDailyQuota = quota }),
             Config, AcceptedAttestation(), new(db, Clock), new(db, new(db, Clock)), new(db, Clock, Config), new(db, Clock, Config),
             Clock, NullLogger<AiProtectionFilter>.Instance);
-        using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
+        var jsonOptions = new JsonOptions();
+        jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
+        using var services = new ServiceCollection().AddSingleton(db)
+            .AddSingleton<IOptions<JsonOptions>>(Options.Create(jsonOptions)).BuildServiceProvider();
         var http = new DefaultHttpContext { RequestServices = services };
         http.Request.Path = "/owlai/ai/word-detail";
         http.Request.Method = "POST";
+        http.Request.ContentType = "application/json";
         http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"word":"hola"}"""));
         http.Request.Headers.Authorization = "Bearer " + new JwtTokenService(Config, Clock).CreateDeviceToken(key, "premium").Token;
         var challenge = await new ChallengeService(db, Clock).IssueAssertionAsync(key, http.Request.Path);
         http.Request.Headers["X-App-Attest-Key-Id"] = key;
         http.Request.Headers["X-App-Attest-Challenge-Id"] = challenge.ChallengeId;
         http.Request.Headers["X-App-Attest-Assertion"] = "AQID";
         var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
         var context = new ResourceExecutingContext(action, [], new List<IValueProviderFactory>());
         var providerCalls = 0;
         await filter.OnResourceExecutionAsync(context, () =>
         {
             providerCalls++;
             return Task.FromResult(new ResourceExecutedContext(action, []) { Result = new OkResult() });
         });
         return (providerCalls > 0 ? 200 : (context.Result as ObjectResult)?.StatusCode ?? 500, providerCalls);
     }
 }
 
 public sealed class PaidAccessJsonAuditRegressionTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
 {
     [Theory]
-    [InlineData("word")]
-    [InlineData("Word")]
-    public async Task B3_CaseInsensitiveBindingCannotBypassTenWordReservation(string property)
+    [InlineData("word", "word-detail")]
+    [InlineData("Word", "word-detail")]
+    [InlineData("WORD", "word-detail")]
+    [InlineData("word", "analyze-word")]
+    [InlineData("Word", "analyze-word")]
+    [InlineData("WORD", "analyze-word")]
+    public async Task B3_CaseInsensitiveBindingCannotBypassTenWordReservation(string property, string operation)
     {
         var provider = new CountingProvider();
         await using var factory = new ApiFactory(postgres.ConnectionString, services =>
         {
             services.RemoveAll<IAppAttestVerifier>();
             services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier { AssertionResult = AppAttestVerifier.AssertionResult.Success(1) });
             services.RemoveAll<IAiJsonService>();
             services.AddSingleton<IAiJsonService>(provider);
         }, new Dictionary<string, string?> { ["TestMode:Enabled"] = "false", ["AiProtection:RequireAssertion"] = "true", ["AiProtection:FreeDailyQuota"] = "40" });
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         var key = Guid.NewGuid().ToString("N");
         db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = key, PublicKey = [1, 2, 3], Environment = "production" });
         await db.SaveChangesAsync();
         client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(key, "free").Token);
-        const string path = "/owlai/ai/word-detail";
+        var path = "/owlai/ai/" + operation;
         for (var i = 0; i < 11; i++)
         {
             var challenge = await scope.ServiceProvider.GetRequiredService<ChallengeService>().IssueAssertionAsync(key, path);
             using var request = new HttpRequestMessage(HttpMethod.Post, path);
             request.Headers.Add("X-App-Attest-Key-Id", key);
             request.Headers.Add("X-App-Attest-Challenge-Id", challenge.ChallengeId);
             request.Headers.Add("X-App-Attest-Assertion", "AQID");
             request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string>
             { [property] = key + i, ["native_language"] = "en", ["learning_language"] = "es" }), Encoding.UTF8, "application/json");
             using var response = await client.SendAsync(request);
             Assert.Equal(i < 10 ? HttpStatusCode.OK : HttpStatusCode.PaymentRequired, response.StatusCode);
             Assert.Equal(Math.Min(i + 1, 10), provider.Calls);
         }
         Assert.Equal(10, await db.DeviceWords.CountAsync(x => x.DeviceUuid == key));
         Assert.Equal(10, await db.AiUsage.Where(x => x.KeyId == key).SumAsync(x => x.Count));
+        // Repeating an existing word at the ceiling remains allowed and spends daily usage even on a cache hit.
+        var repeatChallenge = await scope.ServiceProvider.GetRequiredService<ChallengeService>().IssueAssertionAsync(key, path);
+        using var repeat = new HttpRequestMessage(HttpMethod.Post, path);
+        repeat.Headers.Add("X-App-Attest-Key-Id", key);
+        repeat.Headers.Add("X-App-Attest-Challenge-Id", repeatChallenge.ChallengeId);
+        repeat.Headers.Add("X-App-Attest-Assertion", "AQID");
+        repeat.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string>
+            { [property] = key + 0, ["native_language"] = "en", ["learning_language"] = "es" }), Encoding.UTF8, "application/json");
+        using var repeatedResponse = await client.SendAsync(repeat);
+        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);
+        Assert.Equal(10, provider.Calls);
+        Assert.Equal(10, await db.DeviceWords.CountAsync(x => x.DeviceUuid == key));
+        Assert.Equal(11, await db.AiUsage.Where(x => x.KeyId == key).SumAsync(x => x.Count));
     }
 
     private sealed class CountingProvider : IAiJsonService
     {
         public int Calls { get; private set; }
         public Task<JsonElement?> CompleteJsonAsync(string operation, string prompt, CancellationToken ct = default)
         {
             Calls++;
             return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { translations = new[] { "sample" }, translation = "sample", corrected_word = "sample" }));
         }
         public Task<JsonElement?> CompleteVisionJsonAsync(string operation, string prompt, byte[] image, string mime, CancellationToken ct = default)
             => throw new InvalidOperationException("Unexpected vision provider call");
diff --git a/tests/SharedSubscriptionPostgresTests.cs b/tests/SharedSubscriptionPostgresTests.cs
index 3561b2a..c671863 100644
--- a/tests/SharedSubscriptionPostgresTests.cs
+++ b/tests/SharedSubscriptionPostgresTests.cs
@@ -162,24 +162,25 @@ public class SharedSubscriptionPostgresTests(PostgresContainerFixture postgres)
         Assert.Single(await service.ListAccountMineAsync(b.Id, default));
         Assert.Empty(await service.ListAccountMineAsync(a.Id, default));
     }
 
     [Fact]
     public async Task ClaimApiRequiresBothIdentities_ThenLogoutAndDeletionCloseSharedAccess()
     {
         var otid = Guid.NewGuid().ToString("N");
         var purchase = new SubscriptionEntity { OriginalTransactionId = otid, ProductId = "monthly", DeviceUuid = "device-"+otid, ExpiresAt = DateTime.UtcNow.AddDays(10), WasEverPaid = true };
         var apple = new FakeAppStoreServerClient { VerifyTransactionResult = new(true, purchase, true, null), SubscriptionStatusesResult = new(true, purchase, null) };
         await using var factory = new ApiFactory(postgres.ConnectionString, services =>
         {
+            services.RemoveAll<IAiJsonService>(); services.AddSingleton<IAiJsonService>(new FixedAiJsonService());
             services.RemoveAll<IAppStoreServerClient>(); services.AddSingleton<IAppStoreServerClient>(apple);
             services.RemoveAll<IAppAttestVerifier>(); services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true });
         }, new Dictionary<string,string?> { ["AccountAi:DailyQuota"] = "200", ["AccountAi:RequestsPerMinute"] = "30", ["AiProtection:Enabled"] = "true", ["AiProtection:RequireAssertion"] = "false" });
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
         var first = await accounts.SignInAsync(new("apple-owner-"+otid, null, "Owner"), default, allowCreation: true);
         var second = await accounts.SignInAsync(new("apple-stranger-"+otid, null, "Other"), default, allowCreation: true);
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         var keyId = "SIMULATOR-"+otid;
         db.Devices.Add(new DeviceEntity { KeyId = keyId, DeviceUuid = purchase.DeviceUuid });
         await db.SaveChangesAsync();
@@ -191,36 +192,36 @@ public class SharedSubscriptionPostgresTests(PostgresContainerFixture postgres)
         Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/account/ai/analyze-word", new { word = "cat" })).StatusCode);
         var body = new { account_id = first.Profile.Id, jws_transaction = "proof" };
         Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", body)).StatusCode);
         client.DefaultRequestHeaders.Add("X-Device-Authorization", "Bearer " + deviceJwt);
         Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", new { account_id = second.Profile.Id, jws_transaction = "proof" })).StatusCode);
         foreach (var repeat in Enumerable.Range(0, 2))
         {
             var response = await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", body);
             Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
             Assert.Equal("premium", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
             Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
         }
-        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/account/ai/analyze-word", new { word = "" })).StatusCode);
+        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/account/ai/analyze-word", new { word = otid })).StatusCode);
         client.DefaultRequestHeaders.Authorization = new("Bearer", deviceJwt);
         client.DefaultRequestHeaders.Add("X-Account-Authorization", "Bearer " + first.AccessToken);
-        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
+        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = otid })).StatusCode);
         var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
         Assert.Equal(2, await db.AiUsage.AsNoTracking().Where(x => x.KeyId == "account:"+first.Profile.Id && x.Date == today).Select(x => x.Count).SingleAsync());
         client.DefaultRequestHeaders.Remove("X-Account-Authorization");
-        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
+        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = otid })).StatusCode);
         client.DefaultRequestHeaders.Authorization = new("Bearer", second.AccessToken);
         var conflict = await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", new { account_id = second.Profile.Id, jws_transaction = "proof" });
         Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
         Assert.Equal("subscription_already_linked", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
         client.DefaultRequestHeaders.Authorization = new("Bearer", first.AccessToken);
         Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/owlai/account/session/logout", new { refresh_token = first.RefreshToken })).StatusCode);
         Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/owlai/account/entitlement")).StatusCode);
         client.DefaultRequestHeaders.Authorization = new("Bearer", deviceJwt);
         client.DefaultRequestHeaders.Add("X-Account-Authorization", "Bearer " + first.AccessToken);
-        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
+        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = otid })).StatusCode);
         client.DefaultRequestHeaders.Remove("X-Account-Authorization");
         await accounts.DeleteAsync(first.Profile.Id, default);
         client.DefaultRequestHeaders.Authorization = new("Bearer", second.AccessToken);
         Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/owlai/account/subscription/apple/claim", new { account_id = second.Profile.Id, jws_transaction = "proof" })).StatusCode);
     }
 }
diff --git a/tests/TestModeRouteTests.cs b/tests/TestModeRouteTests.cs
index f6349e8..45ee6a1 100644
--- a/tests/TestModeRouteTests.cs
+++ b/tests/TestModeRouteTests.cs
@@ -33,41 +33,41 @@ public class TestModeRouteTests(PostgresContainerFixture postgres) : IClassFixtu
     }
 
     [Fact]
     public async Task TestModeRemovesAccountSubscriptionAndRequestLimitsAndCanBeTurnedOff()
     {
         await using var factory = Factory("false");
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
         var session = await accounts.SignInAsync(new("test-mode-" + Guid.NewGuid(), null, "Tester"), default, allowCreation: true);
         client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
         const string path = "/owlai/account/ai/analyze-word";
-        // An empty word reaches real input validation without paying for an AI call.
-        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);
+        // A valid word exercises the subscription gate; empty words below exercise test-mode input validation.
+        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(path, new { word = "valid-word" })).StatusCode);
         var config = factory.Services.GetRequiredService<IConfiguration>();
         config["TestMode:Enabled"] = "true";
         for (var i = 0; i < 65; i++)
             Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);
 
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == "account:" + session.Profile.Id));
         Assert.False(await db.Subscriptions.AnyAsync(x => x.OwnerAccountId == session.Profile.Id));
         var ent = await client.GetFromJsonAsync<JsonElement>("/owlai/account/entitlement");
         Assert.Equal("free", ent.GetProperty("status").GetString());
 
         // A forged client header cannot keep test mode enabled after the server turns it off.
         config["TestMode:Enabled"] = "false";
         client.DefaultRequestHeaders.Add("X-Test-Mode", "true");
-        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);
+        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(path, new { word = "valid-word" })).StatusCode);
     }
 
     [Fact]
     public async Task TestModeAllowsRepeatedAssertionChallengesButKeepsPasswordAttemptThrottle()
     {
         await using var factory = Factory("true");
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         var key = "test-mode-proof-" + Guid.NewGuid();
         db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = key, Environment = "production" });
         await db.SaveChangesAsync();
@@ -112,25 +112,25 @@ public class TestModeRouteTests(PostgresContainerFixture postgres) : IClassFixtu
         {
             var response = await client.PostAsJsonAsync("/owlai/device-words/upsert", new {
                 normalized_word = "word-" + i, display_word = "word-" + i, native_language = "en", learning_language = "es"
             });
             Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
         }
         Assert.Equal(12, await db.DeviceWords.CountAsync(x => x.DeviceUuid == id));
         for (var i = 0; i < 65; i++)
             Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
         Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == key));
         Assert.False(await db.Subscriptions.AnyAsync(x => x.DeviceUuid == id));
         factory.Services.GetRequiredService<IConfiguration>()["TestMode:Enabled"] = "false";
-        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
+        Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "valid-word" })).StatusCode);
     }
 
     [Theory]
     [InlineData(false)]
     [InlineData(true)]
     public async Task DevelopmentDeviceCannotRetainTestAccessWhenServerTurnsItOff(bool expired)
     {
         await using var factory = Factory("false", protectionEnabled: false);
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         var id = Guid.NewGuid().ToString("N");
@@ -148,38 +148,40 @@ public class TestModeRouteTests(PostgresContainerFixture postgres) : IClassFixtu
             {
                 DeviceUuid = id, NormalizedWord = "word-" + i, NativeLanguage = "en", LearningLanguage = "es", IsActive = true
             });
         await db.SaveChangesAsync();
         client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<JwtTokenService>()
             .CreateDeviceToken(key, "premium").Token);
         client.DefaultRequestHeaders.Add("X-Test-Mode", "true");
         var config = factory.Services.GetRequiredService<IConfiguration>();
         foreach (var testMode in new[] { false, true, false })
         {
             config["TestMode:Enabled"] = testMode.ToString();
             Assert.Equal(testMode ? HttpStatusCode.BadRequest : HttpStatusCode.PaymentRequired,
-                (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
+                (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = testMode ? "" : "eleventh", native_language = "en", learning_language = "es" })).StatusCode);
             Assert.Equal(testMode ? HttpStatusCode.OK : HttpStatusCode.PaymentRequired,
                 (await client.PostAsJsonAsync("/owlai/device-words/upsert", new
                 {
                     normalized_word = Guid.NewGuid().ToString("N"), display_word = "New word", native_language = "en", learning_language = "es"
                 })).StatusCode);
         }
         Assert.Equal(11, await db.DeviceWords.CountAsync(x => x.DeviceUuid == id));
         Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == key));
         var entitlement = await scope.ServiceProvider.GetRequiredService<DeviceContextService>().ResolveAsync(key);
         Assert.Equal(expired ? "expired_paid" : "free", entitlement!.Entitlement.Status);
     }
 
     private ApiFactory Factory(string? testMode, bool protectionEnabled = true) => new(postgres.ConnectionString, services =>
     {
+        services.RemoveAll<IAiJsonService>();
+        services.AddSingleton<IAiJsonService>(new FixedAiJsonService());
         services.RemoveAll<IAppAttestVerifier>();
         services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true });
     }, new Dictionary<string, string?> {
         ["TestMode:Enabled"] = testMode,
         ["AiProtection:Enabled"] = protectionEnabled.ToString(),
         ["AiProtection:RequireAssertion"] = "false",
         ["AiProtection:FreeDailyQuota"] = "0",
         ["AccountAi:DailyQuota"] = "0",
         ["AccountAi:RequestsPerMinute"] = "0"
     });
 }
diff --git a/tests/TestSupport/FixedAiJsonService.cs b/tests/TestSupport/FixedAiJsonService.cs
new file mode 100644
index 0000000..98aeea1
--- /dev/null
+++ b/tests/TestSupport/FixedAiJsonService.cs
@@ -0,0 +1,12 @@
+using System.Text.Json;
+using Mavrylo.Services;
+
+namespace Mavrylo.Tests.TestSupport;
+
+internal sealed class FixedAiJsonService : IAiJsonService
+{
+    public Task<JsonElement?> CompleteJsonAsync(string operation, string prompt, CancellationToken ct = default)
+        => Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { translations = new[] { "sample" }, translation = "sample", corrected_word = "sample" }));
+    public Task<JsonElement?> CompleteVisionJsonAsync(string operation, string prompt, byte[] image, string mime, CancellationToken ct = default)
+        => throw new InvalidOperationException("Unexpected vision operation");
+}
