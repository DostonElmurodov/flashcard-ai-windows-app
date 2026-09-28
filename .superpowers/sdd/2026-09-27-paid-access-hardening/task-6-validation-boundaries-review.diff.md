# Task 6 bounded review package
Base: 7310fa4df055c5715c6974a4bb4a463ff036cac5
Head: 3092b09f2e95668a4302b8e38c747fba2a668e24

3092b09 Validate extraction before spend and protect retained word content
 Filters/AccountAiProtectionFilter.cs               |  24 +-
 Filters/AiProtectionFilter.cs                      |  14 +-
 src/Mavrylo.Services/Services/DeviceWordService.cs |  23 +-
 .../Services/ExtractionRequestValidator.cs         |  61 +++++
 src/Mavrylo.Services/Services/WordAiService.cs     |  36 +--
 tests/DeviceWordServiceTests.cs                    |  15 +-
 tests/PaidAccessBoundaryHttpTests.cs               | 296 +++++++++++++++++++++
 7 files changed, 417 insertions(+), 52 deletions(-)
diff --git a/Filters/AccountAiProtectionFilter.cs b/Filters/AccountAiProtectionFilter.cs
index 3f36335..ecd4243 100644
--- a/Filters/AccountAiProtectionFilter.cs
+++ b/Filters/AccountAiProtectionFilter.cs
@@ -6,21 +6,43 @@ using Microsoft.AspNetCore.Mvc.ModelBinding;
 namespace Mavrylo.Filters;
 
 public sealed class AccountAiProtectionFilter(AccountEntitlementService entitlements, AccountAiUsageService usage,
     IConfiguration configuration, IHostEnvironment? host = null) : IAsyncResourceFilter
 {
     public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
     {
         var http = context.HttpContext;
         var account = http.User.FindFirst("sub")?.Value;
         if (account == null) { context.Result = new UnauthorizedResult(); return; }
-        if (!http.Request.Path.Value!.TrimEnd('/').EndsWith("/extract-words", StringComparison.OrdinalIgnoreCase))
+        if (http.Request.Path.Value!.TrimEnd('/').EndsWith("/extract-words", StringComparison.OrdinalIgnoreCase))
+        {
+            try
+            {
+                if (http.Request.ContentLength > ExtractionRequestValidator.MaxBodyBytes)
+                    throw new IOException("request body too large");
+                http.Request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: ExtractionRequestValidator.MaxBodyBytes);
+                await http.Request.Body.CopyToAsync(Stream.Null, http.RequestAborted);
+                http.Request.Body.Position = 0;
+            }
+            catch (IOException)
+            {
+                context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
+                return;
+            }
+            var error = await ExtractionRequestValidator.ValidateAsync(http.Request, http.RequestAborted);
+            if (error is not null)
+            {
+                context.Result = new BadRequestObjectResult(new { error });
+                return;
+            }
+        }
+        else
         {
             try
             {
                 if (http.Request.ContentLength > 16000) throw new IOException("request body too large");
                 http.Request.EnableBuffering(bufferThreshold: 16000, bufferLimit: 16000);
                 using var body = new MemoryStream();
                 await http.Request.Body.CopyToAsync(body, http.RequestAborted);
                 http.Request.Body.Position = 0;
                 await AiRequestContextReader.ReadAsync(body.ToArray(), http.Request.Path.Value.TrimEnd('/').Split('/').Last().ToLowerInvariant(), http);
             }
diff --git a/Filters/AiProtectionFilter.cs b/Filters/AiProtectionFilter.cs
index d827ff1..799096d 100644
--- a/Filters/AiProtectionFilter.cs
+++ b/Filters/AiProtectionFilter.cs
@@ -29,21 +29,20 @@ public sealed class AiProtectionFilter(
     IConfiguration config,
     IAppAttestVerifier verifier,
     ChallengeService challenges,
     DeviceContextService deviceContext,
     DeviceWordService deviceWords,
     AiUsageService usage,
     TimeProvider timeProvider,
     ILogger<AiProtectionFilter> logger,
     IHostEnvironment? host = null) : IAsyncResourceFilter
 {
-    private const long MaxImageBodyBytes = 20_000_000;
     private const long MaxJsonBodyBytes = 16_000;
 
     public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
     {
         var opts = options.Value;
         var http = context.HttpContext;
         var path = http.Request.Path.Value ?? "";
         var operation = path.TrimEnd('/').Split('/').Last().ToLowerInvariant();
 
         // 1) Device-JWT (aud=device). Validate and extract keyId. The ent claim is UI-only.
@@ -59,21 +58,21 @@ public sealed class AiProtectionFilter(
         {
             context.Result = Problem(http, StatusCodes.Status401Unauthorized, "unknown device");
             return;
         }
         var ent = resolved.Entitlement.Status;
 
         // Buffer the raw body once (needed for assertion hash and to let binding re-read it).
         byte[] body;
         try
         {
-            body = await ReadAndRewindBodyAsync(http, operation == "extract-words" ? MaxImageBodyBytes : MaxJsonBodyBytes);
+            body = await ReadAndRewindBodyAsync(http, operation == "extract-words" ? ExtractionRequestValidator.MaxBodyBytes : MaxJsonBodyBytes);
         }
         catch (IOException)
         {
             context.Result = Problem(http, StatusCodes.Status413PayloadTooLarge, "request body too large");
             return;
         }
 
         // 2) Assertion (App Attest). Skipped in Development for SIMULATOR-* keys, or if RequireAssertion is off.
         var isSimulator = keyId.StartsWith("SIMULATOR-", StringComparison.Ordinal);
         var skipAssertion = !opts.RequireAssertion || (verifier.IsDevelopmentBypassEnabled && isSimulator);
@@ -81,21 +80,30 @@ public sealed class AiProtectionFilter(
         {
             var assertionError = await VerifyAssertionAsync(http, keyId, path, body);
             if (assertionError is not null)
             {
                 context.Result = Problem(http, StatusCodes.Status403Forbidden, assertionError);
                 return;
             }
         }
 
         AiRequestContextReader.WordContext? wordContext = null;
-        if (operation != "extract-words")
+        if (operation == "extract-words")
+        {
+            var error = await ExtractionRequestValidator.ValidateAsync(http.Request, http.RequestAborted);
+            if (error is not null)
+            {
+                context.Result = Problem(http, StatusCodes.Status400BadRequest, error);
+                return;
+            }
+        }
+        else
         {
             try
             {
                 wordContext = await AiRequestContextReader.ReadAsync(body, operation, http);
             }
             catch (UnsupportedContentTypeException ex)
             {
                 context.Result = Problem(http, StatusCodes.Status415UnsupportedMediaType, ex.Message);
                 return;
             }
diff --git a/src/Mavrylo.Services/Services/DeviceWordService.cs b/src/Mavrylo.Services/Services/DeviceWordService.cs
index fc4be4e..7e32b55 100644
--- a/src/Mavrylo.Services/Services/DeviceWordService.cs
+++ b/src/Mavrylo.Services/Services/DeviceWordService.cs
@@ -27,48 +27,59 @@ public sealed class DeviceWordService(AppDbContext db, TimeProvider timeProvider
         var learning = NormalizeLanguage(request.LearningLanguage);
         if (string.IsNullOrWhiteSpace(normalized))
             throw new InvalidOperationException("normalized_word is required");
         if (normalized.Length > SupportedLanguages.MaxWordLength)
             throw new InvalidOperationException("normalized_word is too long");
         if (!string.IsNullOrWhiteSpace(request.DisplayWord) && request.DisplayWord.Trim().Length > SupportedLanguages.MaxWordLength)
             throw new InvalidOperationException("display_word is too long");
 
         await using var tx = await BeginWordMutationAsync(deviceUuid, ct);
         var existing = await FindExistingAsync(deviceUuid, request.ClientWordId, normalized, native, learning, ct);
-        if ((existing is null || !existing.IsActive) && !TestModePolicy.IsEnabled(configuration, host)
+        var display = string.IsNullOrWhiteSpace(request.DisplayWord)
+            ? existing?.DisplayWord ?? request.NormalizedWord.Trim() : request.DisplayWord.Trim();
+        var translation = request.Translation is null ? existing?.Translation : TrimToNull(request.Translation);
+        var pronunciation = request.Pronunciation is null ? existing?.Pronunciation : TrimToNull(request.Pronunciation);
+        var partOfSpeech = request.PartOfSpeech is null ? existing?.PartOfSpeech : TrimToNull(request.PartOfSpeech);
+        var detailJson = request.DetailJson is null ? existing?.DetailJson : TrimToNull(request.DetailJson);
+        var changesContent = existing is null || !existing.IsActive
+            || existing.NormalizedWord != normalized || existing.DisplayWord != display
+            || existing.NativeLanguage != native || existing.LearningLanguage != learning
+            || existing.Translation != translation || existing.Pronunciation != pronunciation
+            || existing.PartOfSpeech != partOfSpeech || existing.DetailJson != detailJson;
+        if (changesContent && !TestModePolicy.IsEnabled(configuration, host)
             && entitlement is not (EntitlementService.Status.Free
                 or EntitlementService.Status.Trial or EntitlementService.Status.Premium or EntitlementService.Status.Grace))
             return new DeviceWordMutationResponse(false, await CountActiveAsync(deviceUuid, ct), FreeLimit);
 
         if ((existing is null || !existing.IsActive) && IsFreeLimited(entitlement))
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
 
         word.ClientWordId = string.IsNullOrWhiteSpace(request.ClientWordId) ? word.ClientWordId : request.ClientWordId.Trim();
         word.NormalizedWord = normalized;
-        word.DisplayWord = string.IsNullOrWhiteSpace(request.DisplayWord) ? request.NormalizedWord.Trim() : request.DisplayWord.Trim();
+        word.DisplayWord = display;
         word.NativeLanguage = native;
         word.LearningLanguage = learning;
-        word.Translation = TrimToNull(request.Translation);
-        word.Pronunciation = TrimToNull(request.Pronunciation);
-        word.PartOfSpeech = TrimToNull(request.PartOfSpeech);
-        word.DetailJson = TrimToNull(request.DetailJson);
+        word.Translation = translation;
+        word.Pronunciation = pronunciation;
+        word.PartOfSpeech = partOfSpeech;
+        word.DetailJson = detailJson;
         word.IsActive = true;
         word.UpdatedAt = now;
 
         if (existing is null)
             db.DeviceWords.Add(word);
 
         await db.SaveChangesAsync(ct);
         var activeCount = await CountActiveAsync(deviceUuid, ct);
         await tx.CommitAsync(ct);
         return new DeviceWordMutationResponse(true, activeCount, FreeLimit);
diff --git a/src/Mavrylo.Services/Services/ExtractionRequestValidator.cs b/src/Mavrylo.Services/Services/ExtractionRequestValidator.cs
new file mode 100644
index 0000000..78d7870
--- /dev/null
+++ b/src/Mavrylo.Services/Services/ExtractionRequestValidator.cs
@@ -0,0 +1,61 @@
+using Microsoft.AspNetCore.Http;
+
+namespace Mavrylo.Services;
+
+/// <summary>Validates the same cached form and image bytes MVC later binds for extraction.</summary>
+public static class ExtractionRequestValidator
+{
+    public const long MaxBodyBytes = 20_000_000;
+
+    public static async Task<string?> ValidateAsync(HttpRequest request, CancellationToken ct)
+    {
+        if (!request.HasFormContentType || !request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
+            return "multipart form data required";
+
+        IFormCollection form;
+        try { form = await request.ReadFormAsync(ct); }
+        catch (InvalidDataException) { return "invalid multipart form data"; }
+        catch (IOException) { return "invalid multipart form data"; }
+
+        if (form.Files.Count != 1 || !string.Equals(form.Files[0].Name, "image", StringComparison.OrdinalIgnoreCase)
+            || form.Files[0].Length == 0)
+            return "image required";
+        var image = form.Files[0];
+        await using var stream = image.OpenReadStream();
+        var header = new byte[Math.Min(12, (int)image.Length)];
+        try { await stream.ReadExactlyAsync(header, ct); }
+        catch (EndOfStreamException) { return "invalid image payload"; }
+        if (!TryDetectImageMime(header, out _))
+            return "unsupported image payload";
+
+        var target = form["targetLanguage"];
+        var native = form["nativeLanguage"];
+        if (target.Count > 1 || native.Count > 1)
+            return "duplicate language field";
+        if (!SupportedLanguages.TryNormalize(target.Count == 0 ? "en" : target.ToString(), out _))
+            return "unsupported target language";
+        if (!SupportedLanguages.TryNormalize(native.Count == 0 ? "en" : native.ToString(), out _))
+            return "unsupported native language";
+        return null;
+    }
+
+    public static bool TryDetectImageMime(ReadOnlySpan<byte> bytes, out string mime)
+    {
+        mime = "";
+        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
+        { mime = "image/jpeg"; return true; }
+        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
+            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
+        { mime = "image/png"; return true; }
+        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
+            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
+        { mime = "image/webp"; return true; }
+        if (bytes.Length >= 12 && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
+        {
+            var brand = System.Text.Encoding.ASCII.GetString(bytes.Slice(8, 4));
+            if (brand is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1")
+            { mime = "image/heic"; return true; }
+        }
+        return false;
+    }
+}
diff --git a/src/Mavrylo.Services/Services/WordAiService.cs b/src/Mavrylo.Services/Services/WordAiService.cs
index acdfc54..0cf3961 100644
--- a/src/Mavrylo.Services/Services/WordAiService.cs
+++ b/src/Mavrylo.Services/Services/WordAiService.cs
@@ -194,21 +194,21 @@ Do not include pronunciation, audio, or additional fields.";
         => (left == "en-us" ? "en" : left) == (right == "en-us" ? "en" : right);
 
     public async Task<AiResult> ExtractWordsAsync(byte[] imageBytes, string? targetLanguage, string? nativeLanguage, CancellationToken ct)
     {
         if (imageBytes.Length == 0) return new AiResult(400, "image required");
         if (!SupportedLanguages.TryNormalize(targetLanguage ?? "en", out var targetLang))
             return new AiResult(400, new { error = "unsupported target language" });
         if (!SupportedLanguages.TryNormalize(nativeLanguage ?? "en", out var nativeLang))
             return new AiResult(400, new { error = "unsupported native language" });
 
-        if (!TryDetectImageMime(imageBytes, out var mime))
+        if (!ExtractionRequestValidator.TryDetectImageMime(imageBytes, out var mime))
             return new AiResult(400, new { error = "unsupported image payload" });
 
         var tl = LanguageName(targetLang);
         var nl = LanguageName(nativeLang);
         var prompt = $@"Look at this image. Extract vocabulary words in {tl}. Return JSON {{ ""words"": [ {{ ""word"": string, ""translation"": string (quick, in {nl}) }} ] }}
 Only nouns, verbs, adjectives, adverbs in base form; skip articles and numbers.";
         JsonElement? el;
         try
         {
             el = await ai.CompleteVisionJsonAsync("extract-words", prompt, imageBytes, mime, ct);
@@ -288,45 +288,11 @@ Only nouns, verbs, adjectives, adverbs in base form; skip articles and numbers."
             error = "unsupported learning language";
             return false;
         }
 
         return true;
     }
 
     private static string LanguageName(string codeOrName)
         => SupportedLanguages.DisplayName(codeOrName);
 
-    private static bool TryDetectImageMime(byte[] bytes, out string mime)
-    {
-        mime = "";
-        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
-        {
-            mime = "image/jpeg";
-            return true;
-        }
-        if (bytes.Length >= 8
-            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
-            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
-        {
-            mime = "image/png";
-            return true;
-        }
-        if (bytes.Length >= 12
-            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
-            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
-        {
-            mime = "image/webp";
-            return true;
-        }
-        if (bytes.Length >= 12
-            && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
-        {
-            var brand = System.Text.Encoding.ASCII.GetString(bytes, 8, 4);
-            if (brand is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1")
-            {
-                mime = "image/heic";
-                return true;
-            }
-        }
-        return false;
-    }
 }
diff --git a/tests/DeviceWordServiceTests.cs b/tests/DeviceWordServiceTests.cs
index bc6eb7a..1a2c79d 100644
--- a/tests/DeviceWordServiceTests.cs
+++ b/tests/DeviceWordServiceTests.cs
@@ -68,35 +68,36 @@ public class DeviceWordServiceTests
     {
         using var testDb = TestDb.Create();
         var config = TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = "false" });
         var service = new DeviceWordService(testDb.Db, TimeProvider.System, config);
         for (var i = 0; i < 11; i++)
             Assert.True((await service.UpsertAsync("device", Request($"word-{i}"), entitlement, CancellationToken.None)).Accepted);
         Assert.Equal(11, await service.CountActiveAsync("device", CancellationToken.None));
     }
 
     [Theory]
-    [InlineData("free")]
-    [InlineData("expired_trial")]
-    [InlineData("expired_paid")]
-    [InlineData("revoked")]
-    public async Task TestModeFalse_PreservesUpdatesToExistingWords(string entitlement)
+    [InlineData("free", true)]
+    [InlineData("expired_trial", false)]
+    [InlineData("expired_paid", false)]
+    [InlineData("revoked", false)]
+    [InlineData("invalid_subscription", false)]
+    public async Task TestModeFalse_ChangesExistingContentOnlyWithEligibleAccess(string entitlement, bool accepted)
     {
         using var testDb = TestDb.Create();
         var config = TestConfig.Create(new Dictionary<string, string?> { ["TestMode:Enabled"] = "false" });
         var service = new DeviceWordService(testDb.Db, TimeProvider.System, config);
         for (var i = 0; i < 10; i++)
             await service.UpsertAsync("device", Request($"word-{i}"), "premium", CancellationToken.None);
         var result = await service.UpsertAsync("device", Request("word-0") with { Translation = "updated" }, entitlement, CancellationToken.None);
-        Assert.True(result.Accepted);
+        Assert.Equal(accepted, result.Accepted);
         Assert.Equal(10, result.ActiveWordCount);
-        Assert.Equal("updated", testDb.Db.DeviceWords.Single(x => x.NormalizedWord == "word-0").Translation);
+        Assert.Equal(accepted ? "updated" : "translation", testDb.Db.DeviceWords.Single(x => x.NormalizedWord == "word-0").Translation);
     }
 
     [Fact]
     public async Task AiReservation_ReservesSlot_AndAllowsExistingWord()
     {
         using var testDb = TestDb.Create();
         testDb.Db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device" });
         await testDb.Db.SaveChangesAsync();
         var service = new DeviceWordService(testDb.Db, TimeProvider.System);
 
diff --git a/tests/PaidAccessBoundaryHttpTests.cs b/tests/PaidAccessBoundaryHttpTests.cs
new file mode 100644
index 0000000..c7bd6a2
--- /dev/null
+++ b/tests/PaidAccessBoundaryHttpTests.cs
@@ -0,0 +1,296 @@
+using System.Net;
+using System.Net.Http.Headers;
+using System.Net.Http.Json;
+using System.Security.Cryptography;
+using System.Text;
+using System.Formats.Cbor;
+using Mavrylo.Data;
+using Mavrylo.Models;
+using Mavrylo.Services;
+using Mavrylo.Tests.TestSupport;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.DependencyInjection.Extensions;
+using Microsoft.Extensions.Logging.Abstractions;
+using Xunit;
+
+namespace Mavrylo.Tests;
+
+public sealed class PaidAccessBoundaryHttpTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
+{
+    [Theory]
+    [InlineData(false, "missing")]
+    [InlineData(true, "missing")]
+    [InlineData(false, "empty")]
+    [InlineData(true, "empty")]
+    [InlineData(false, "invalid")]
+    [InlineData(true, "invalid")]
+    [InlineData(false, "language")]
+    [InlineData(true, "language")]
+    [InlineData(false, "duplicate-image")]
+    [InlineData(true, "duplicate-image")]
+    [InlineData(false, "duplicate-language")]
+    [InlineData(true, "duplicate-language")]
+    public async Task InvalidMultipartDoesNotConsumeQuotaOrReachProvider(bool accountRoute, string invalid)
+    {
+        var provider = new CountingProvider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient();
+        using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var subject = accountRoute ? await Account(db, client, scope) : await Device(db, client, scope);
+        var beforeSpend = await db.AiSpendReservations.CountAsync();
+        using var form = new MultipartFormDataContent();
+        if (invalid != "missing")
+            form.Add(new ByteArrayContent(invalid == "empty" ? [] : invalid == "invalid" ? [1, 2, 3, 4] : [0xff, 0xd8, 0xff, 1]), "image", "image.jpg");
+        if (invalid == "language") form.Add(new StringContent("unsupported-language"), "targetLanguage");
+        if (invalid == "duplicate-image") form.Add(new ByteArrayContent([0xff, 0xd8, 0xff, 2]), "image", "other.jpg");
+        if (invalid == "duplicate-language")
+        {
+            form.Add(new StringContent("es"), "targetLanguage");
+            form.Add(new StringContent("fr"), "targetLanguage");
+        }
+        using var response = await client.PostAsync(Path(accountRoute), form);
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+        Assert.Equal(0, provider.Calls);
+        Assert.Equal(beforeSpend, await db.AiSpendReservations.CountAsync());
+        Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == (accountRoute ? "account:" + subject : subject)));
+        Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == subject));
+    }
+
+    [Theory]
+    [InlineData(false)]
+    [InlineData(true)]
+    public async Task MalformedMultipartDoesNotConsumeQuota(bool accountRoute)
+    {
+        var provider = new CountingProvider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var subject = accountRoute ? await Account(db, client, scope) : await Device(db, client, scope);
+        var beforeSpend = await db.AiSpendReservations.CountAsync();
+        using var body = new ByteArrayContent(Encoding.UTF8.GetBytes("not a multipart body"));
+        body.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=boundary-that-is-absent");
+        using var response = await client.PostAsync(Path(accountRoute), body);
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+        Assert.Equal(0, provider.Calls);
+        Assert.Equal(beforeSpend, await db.AiSpendReservations.CountAsync());
+        Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == (accountRoute ? "account:" + subject : subject)));
+        Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == subject));
+    }
+
+    [Theory]
+    [InlineData(false, false)]
+    [InlineData(true, false)]
+    [InlineData(false, true)]
+    [InlineData(true, true)]
+    public async Task OversizedMultipartDoesNotConsumeQuota(bool accountRoute, bool unknownLength)
+    {
+        var provider = new CountingProvider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var subject = accountRoute ? await Account(db, client, scope) : await Device(db, client, scope);
+        var beforeSpend = await db.AiSpendReservations.CountAsync();
+        using HttpContent body = unknownLength ? new UnknownLengthContent(20_000_001) : new ByteArrayContent(new byte[20_000_001]);
+        body.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=oversized");
+        using var response = await client.PostAsync(Path(accountRoute), body);
+        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
+        Assert.Equal(0, provider.Calls);
+        Assert.Equal(beforeSpend, await db.AiSpendReservations.CountAsync());
+        Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == (accountRoute ? "account:" + subject : subject)));
+        Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == subject));
+    }
+
+    [Theory]
+    [InlineData(false)]
+    [InlineData(true)]
+    public async Task ValidMultipartUsesBoundImageAndLanguagesAndNormalSpendGuards(bool accountRoute)
+    {
+        var provider = new CountingProvider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var subject = accountRoute ? await Account(db, client, scope) : await Device(db, client, scope);
+        using var form = new MultipartFormDataContent();
+        form.Add(new ByteArrayContent([0xff, 0xd8, 0xff, 1]), "image", "photo.jpg");
+        form.Add(new StringContent("es"), "targetLanguage");
+        form.Add(new StringContent("fr"), "nativeLanguage");
+        using var response = await client.PostAsync(Path(accountRoute), form);
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        Assert.Equal(1, provider.Calls);
+        Assert.Equal("image/jpeg", provider.Mime);
+        Assert.Equal(new byte[] { 0xff, 0xd8, 0xff, 1 }, provider.Image);
+        Assert.Contains("Spanish", provider.Prompt);
+        Assert.Contains("French", provider.Prompt);
+        Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == subject));
+        Assert.Equal(accountRoute ? 2 : 1, await db.AiUsage.Where(x => x.KeyId == (accountRoute ? "account:" + subject : subject)).SumAsync(x => x.Count));
+    }
+
+    [Fact]
+    public async Task MultipartAssertionVerifiesExactRawBodyBeforeValidationAndSpending()
+    {
+        var provider = new CountingProvider();
+        var verifier = new AppAttestVerifier(TestConfig.Create(), new FakeEnvironment("Production"), NullLogger<AppAttestVerifier>.Instance);
+        await using var factory = new ApiFactory(postgres.ConnectionString, services =>
+        {
+            services.RemoveAll<IAiJsonService>(); services.AddSingleton<IAiJsonService>(provider);
+            services.RemoveAll<IAppAttestVerifier>(); services.AddSingleton<IAppAttestVerifier>(verifier);
+        }, new Dictionary<string, string?> { ["TestMode:Enabled"] = "false", ["AiProtection:RequireAssertion"] = "true", ["AiProtection:FreeDailyQuota"] = "40" });
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var key = await Device(db, client, scope);
+        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
+        var device = await db.Devices.SingleAsync(x => x.KeyId == key);
+        device.PublicKey = signing.ExportSubjectPublicKeyInfo();
+        await db.SaveChangesAsync();
+        const string path = "/owlai/ai/extract-words";
+        using var form = new MultipartFormDataContent();
+        form.Add(new ByteArrayContent([0xff, 0xd8, 0xff, 1]), "image", "photo.jpg");
+        form.Add(new StringContent("es"), "targetLanguage");
+        var originalBody = await form.ReadAsByteArrayAsync();
+
+        async Task<HttpResponseMessage> Send(bool mutate)
+        {
+            var challenge = await scope.ServiceProvider.GetRequiredService<ChallengeService>().IssueAssertionAsync(key, path);
+            var hash = AppAttestClientData.ComputeAssertionHash(challenge.Nonce, originalBody, path, challenge.ChallengeId);
+            var authData = new byte[37];
+            SHA256.HashData(Encoding.UTF8.GetBytes("TEAMID1234.com.mavrylo.owlai")).CopyTo(authData, 0);
+            authData[36] = 1;
+            var signature = signing.SignData(authData.Concat(hash).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
+            var writer = new CborWriter(); writer.WriteStartMap(2); writer.WriteTextString("signature"); writer.WriteByteString(signature);
+            writer.WriteTextString("authenticatorData"); writer.WriteByteString(authData); writer.WriteEndMap();
+            using var request = new HttpRequestMessage(HttpMethod.Post, path);
+            request.Headers.Add("X-App-Attest-Key-Id", key);
+            request.Headers.Add("X-App-Attest-Challenge-Id", challenge.ChallengeId);
+            request.Headers.Add("X-App-Attest-Assertion", Convert.ToBase64String(writer.Encode()));
+            request.Content = new ByteArrayContent(mutate ? originalBody.Concat(new byte[] { 0x20 }).ToArray() : originalBody);
+            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(form.Headers.ContentType!.ToString());
+            return await client.SendAsync(request);
+        }
+
+        using var denied = await Send(true);
+        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
+        Assert.Equal(0, provider.Calls);
+        Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == key));
+        using var accepted = await Send(false);
+        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
+        Assert.Equal(1, provider.Calls);
+        Assert.Equal(1, await db.AiUsage.Where(x => x.KeyId == key).SumAsync(x => x.Count));
+    }
+
+    [Theory]
+    [InlineData("expired_paid")]
+    [InlineData("expired_trial")]
+    [InlineData("revoked")]
+    [InlineData("invalid_subscription")]
+    public async Task InactiveDeviceCannotChangeExistingActiveWordButCanAcknowledgeAndDelete(string status)
+    {
+        await using var factory = Factory(new CountingProvider());
+        using var client = factory.CreateClient();
+        using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var key = await Device(db, client, scope);
+        var row = new DeviceWordEntity { DeviceUuid = key, ClientWordId = "client-1", NormalizedWord = "hello", DisplayWord = "Hello",
+            NativeLanguage = "en", LearningLanguage = "es", Translation = "original", Pronunciation = "pron", PartOfSpeech = "noun", DetailJson = "{}" };
+        db.DeviceWords.Add(row);
+        db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId = Guid.NewGuid().ToString("N"), DeviceUuid = key,
+            ProductId = status == "invalid_subscription" ? "unknown.product" : "com.flashcardai.owlai.premium.monthly", Environment = "Production", ExpiresAt = DateTime.UtcNow.AddDays(-1),
+            WasEverPaid = status != "expired_trial", IsTrial = status == "expired_trial", RevokedAt = status == "revoked" ? DateTime.UtcNow : null });
+        await db.SaveChangesAsync();
+        var request = new { client_word_id = "client-1", normalized_word = "hello", display_word = "Hello", native_language = "en",
+            learning_language = "es", translation = "changed", pronunciation = "pron", part_of_speech = "noun", detail_json = "{}" };
+        using var denied = await client.PostAsJsonAsync("/owlai/device-words/upsert", request);
+        Assert.Equal(HttpStatusCode.PaymentRequired, denied.StatusCode);
+        Assert.Contains("\"accepted\":false", await denied.Content.ReadAsStringAsync());
+        await db.Entry(row).ReloadAsync();
+        Assert.Equal("original", row.Translation);
+        Assert.True(row.IsActive);
+        foreach (var changed in new object[] {
+            request with { normalized_word = "different" }, request with { display_word = "Different" },
+            request with { part_of_speech = "verb" }, request with { detail_json = "{\"edited\":true}" } })
+        {
+            using var otherDenied = await client.PostAsJsonAsync("/owlai/device-words/upsert", changed);
+            Assert.Equal(HttpStatusCode.PaymentRequired, otherDenied.StatusCode);
+        }
+        using var exact = await client.PostAsJsonAsync("/owlai/device-words/upsert", request with { translation = "original" });
+        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
+        using var omitted = await client.PostAsJsonAsync("/owlai/device-words/upsert", new { client_word_id = "client-1", normalized_word = "hello", display_word = "Hello", native_language = "en", learning_language = "es" });
+        Assert.Equal(HttpStatusCode.OK, omitted.StatusCode);
+        await db.Entry(row).ReloadAsync();
+        Assert.Equal("original", row.Translation);
+        Assert.Equal("pron", row.Pronunciation);
+        using var deleted = await client.PostAsJsonAsync("/owlai/device-words/delete", new { client_word_id = "client-1", normalized_word = "hello", native_language = "en", learning_language = "es" });
+        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
+        Assert.False(await db.DeviceWords.AsNoTracking().AnyAsync(x => x.Id == row.Id));
+    }
+
+    [Fact]
+    public async Task FreeDeviceCanChangeExistingActiveWordWithinLimit()
+    {
+        await using var factory = Factory(new CountingProvider());
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var key = await Device(db, client, scope);
+        db.DeviceWords.Add(new DeviceWordEntity { DeviceUuid = key, ClientWordId = "free-client", NormalizedWord = "hello", DisplayWord = "Hello",
+            NativeLanguage = "en", LearningLanguage = "es", Translation = "old" });
+        await db.SaveChangesAsync();
+        using var response = await client.PostAsJsonAsync("/owlai/device-words/upsert", new { client_word_id = "free-client", normalized_word = "hello",
+            display_word = "Hello", native_language = "en", learning_language = "es", translation = "new" });
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        Assert.Equal("new", (await db.DeviceWords.AsNoTracking().SingleAsync(x => x.DeviceUuid == key)).Translation);
+    }
+
+    private ApiFactory Factory(CountingProvider provider) => new(postgres.ConnectionString,
+        services => {
+            services.RemoveAll<IAiJsonService>(); services.AddSingleton<IAiJsonService>(provider);
+            services.RemoveAll<IAppAttestVerifier>(); services.AddSingleton<IAppAttestVerifier>(new FakeAppAttestVerifier { IsDevelopmentBypassEnabled = true });
+        },
+        new Dictionary<string, string?> { ["TestMode:Enabled"] = "false", ["AiProtection:RequireAssertion"] = "false",
+            ["AiProtection:FreeDailyQuota"] = "40", ["AccountAi:DailyQuota"] = "40", ["AccountAi:RequestsPerMinute"] = "40" });
+
+    private static string Path(bool account) => account ? "/owlai/account/ai/extract-words" : "/owlai/ai/extract-words";
+
+    private static async Task<string> Device(AppDbContext db, HttpClient client, IServiceScope scope)
+    {
+        var key = "SIMULATOR-" + Guid.NewGuid().ToString("N");
+        db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = key });
+        await db.SaveChangesAsync();
+        client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(key, "free").Token);
+        return key;
+    }
+
+    private static async Task<string> Account(AppDbContext db, HttpClient client, IServiceScope scope)
+    {
+        var session = await scope.ServiceProvider.GetRequiredService<AccountService>().SignInAsync(new(Guid.NewGuid().ToString("N"), null, "Owner"), default, allowCreation: true);
+        db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId = Guid.NewGuid().ToString("N"), DeviceUuid = "account-purchase",
+            OwnerAccountId = session.Profile.Id, ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(5),
+            WasEverPaid = true, ProductId = "com.flashcardai.owlai.premium.monthly", Environment = "Production" });
+        await db.SaveChangesAsync();
+        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
+        return session.Profile.Id;
+    }
+
+    private sealed class CountingProvider : IAiJsonService
+    {
+        public int Calls;
+        public string? Mime;
+        public string? Prompt;
+        public byte[]? Image;
+        public Task<System.Text.Json.JsonElement?> CompleteJsonAsync(string operation, string prompt, CancellationToken ct = default)
+        { Calls++; return Task.FromResult<System.Text.Json.JsonElement?>(System.Text.Json.JsonSerializer.SerializeToElement(new { words = Array.Empty<string>() })); }
+        public Task<System.Text.Json.JsonElement?> CompleteVisionJsonAsync(string operation, string prompt, byte[] image, string mime, CancellationToken ct = default)
+        { Mime = mime; Prompt = prompt; Image = image; return CompleteJsonAsync(operation, prompt, ct); }
+    }
+
+    private sealed class UnknownLengthContent(int bytes) : HttpContent
+    {
+        protected override bool TryComputeLength(out long length) { length = 0; return false; }
+        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
+        {
+            var buffer = new byte[8192];
+            for (var remaining = bytes; remaining > 0; remaining -= Math.Min(buffer.Length, remaining))
+                await stream.WriteAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));
+        }
+    }
+}
