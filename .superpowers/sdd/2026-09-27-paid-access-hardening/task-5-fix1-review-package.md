# Task5 fix1 review package
1c31750 Match MVC request encoding before AI usage reservation
 Filters/AccountAiProtectionFilter.cs               | 15 ++--
 Filters/AiProtectionFilter.cs                      | 12 ++--
 .../Services/AiRequestContextReader.cs             | 82 +++++++++++++++++-----
 tests/AiProtectionFilterTests.cs                   |  6 +-
 tests/AiRequestInterpretationTests.cs              | 75 ++++++++++++++++----
 tests/PaidAccessAuditRegressionTests.cs            |  6 +-
 tests/TestModeRouteTests.cs                        |  8 ++-
 7 files changed, 154 insertions(+), 50 deletions(-)
diff --git a/Filters/AccountAiProtectionFilter.cs b/Filters/AccountAiProtectionFilter.cs
index 6e0d9cd..95fa0a0 100644
--- a/Filters/AccountAiProtectionFilter.cs
+++ b/Filters/AccountAiProtectionFilter.cs
@@ -1,43 +1,42 @@
 using Mavrylo.Services;
 using Microsoft.AspNetCore.Mvc;
 using Microsoft.AspNetCore.Mvc.Filters;
-using Microsoft.Extensions.Options;
+using Microsoft.AspNetCore.Mvc.ModelBinding;
 
 namespace Mavrylo.Filters;
 
 public sealed class AccountAiProtectionFilter(AccountEntitlementService entitlements, AccountAiUsageService usage,
     IConfiguration configuration) : IAsyncResourceFilter
 {
     public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
     {
         var http = context.HttpContext;
         var account = http.User.FindFirst("sub")?.Value;
         if (account == null) { context.Result = new UnauthorizedResult(); return; }
         if (!http.Request.Path.Value!.TrimEnd('/').EndsWith("/extract-words", StringComparison.OrdinalIgnoreCase))
         {
-            if (!AiRequestContextReader.IsJsonContentType(http.Request.ContentType))
-            {
-                context.Result = new StatusCodeResult(StatusCodes.Status415UnsupportedMediaType);
-                return;
-            }
             try
             {
                 if (http.Request.ContentLength > 16000) throw new IOException("request body too large");
                 http.Request.EnableBuffering(bufferThreshold: 16000, bufferLimit: 16000);
                 using var body = new MemoryStream();
                 await http.Request.Body.CopyToAsync(body, http.RequestAborted);
                 http.Request.Body.Position = 0;
-                var options = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
-                AiRequestContextReader.Read(body.ToArray(), http.Request.Path.Value.TrimEnd('/').Split('/').Last().ToLowerInvariant(), options);
+                await AiRequestContextReader.ReadAsync(body.ToArray(), http.Request.Path.Value.TrimEnd('/').Split('/').Last().ToLowerInvariant(), http);
+            }
+            catch (UnsupportedContentTypeException)
+            {
+                context.Result = new StatusCodeResult(StatusCodes.Status415UnsupportedMediaType);
+                return;
             }
             catch (InvalidOperationException ex)
             {
                 context.Result = new BadRequestObjectResult(new { error = ex.Message });
                 return;
             }
             catch (IOException)
             {
                 context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
                 return;
             }
         }
diff --git a/Filters/AiProtectionFilter.cs b/Filters/AiProtectionFilter.cs
index 098f611..6a48d92 100644
--- a/Filters/AiProtectionFilter.cs
+++ b/Filters/AiProtectionFilter.cs
@@ -1,18 +1,19 @@
 using System.IdentityModel.Tokens.Jwt;
 using System.Text;
 using Mavrylo.Data;
 using Mavrylo.Services;
 using Microsoft.AspNetCore.Mvc;
 using Microsoft.AspNetCore.Mvc.Filters;
+using Microsoft.AspNetCore.Mvc.ModelBinding;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.Options;
 using Microsoft.IdentityModel.Tokens;
 
 namespace Mavrylo.Filters;
 
 /// <summary>
 /// Protects AI endpoints. Implemented as an <see cref="IAsyncResourceFilter"/> so it runs BEFORE
 /// model binding — it must read the raw request body to recompute the App Attest assertion
 /// clientDataHash (the body is consumed by binding otherwise), and extract-words is a multipart
 /// upload up to 20 MB.
 ///
@@ -79,33 +80,32 @@ public sealed class AiProtectionFilter(
         {
             var assertionError = await VerifyAssertionAsync(http, keyId, path, body);
             if (assertionError is not null)
             {
                 context.Result = Problem(http, StatusCodes.Status403Forbidden, assertionError);
                 return;
             }
         }
 
         AiRequestContextReader.WordContext? wordContext = null;
         if (operation != "extract-words")
         {
-            if (!AiRequestContextReader.IsJsonContentType(http.Request.ContentType))
+            try
             {
-                context.Result = Problem(http, StatusCodes.Status415UnsupportedMediaType, "JSON content type is required");
-                return;
+                wordContext = await AiRequestContextReader.ReadAsync(body, operation, http);
             }
-            try
+            catch (UnsupportedContentTypeException ex)
             {
-                var jsonOptions = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
-                wordContext = AiRequestContextReader.Read(body, operation, jsonOptions);
+                context.Result = Problem(http, StatusCodes.Status415UnsupportedMediaType, ex.Message);
+                return;
             }
             catch (InvalidOperationException ex)
             {
                 context.Result = Problem(http, StatusCodes.Status400BadRequest, ex.Message);
                 return;
             }
         }
 
         // Test access starts only after the device and its request proof are verified.
         if (TestModePolicy.IsEnabled(config))
         {
             await next();
diff --git a/src/Mavrylo.Services/Services/AiRequestContextReader.cs b/src/Mavrylo.Services/Services/AiRequestContextReader.cs
index d6d4219..738ed75 100644
--- a/src/Mavrylo.Services/Services/AiRequestContextReader.cs
+++ b/src/Mavrylo.Services/Services/AiRequestContextReader.cs
@@ -1,69 +1,113 @@
+using System.Text;
 using System.Text.Json;
+using Microsoft.AspNetCore.Mvc;
+using Microsoft.AspNetCore.Mvc.Formatters;
+using Microsoft.AspNetCore.Mvc.ModelBinding;
+using Microsoft.Extensions.Options;
 using Mavrylo.Dtos;
-using Microsoft.Net.Http.Headers;
 
 namespace Mavrylo.Services;
 
 /// <summary>Reads the same endpoint DTO and serializer options as MVC, before any usage is spent.
 /// Never rewrites the body: request proof and subsequent MVC binding use the original bytes.</summary>
 public static class AiRequestContextReader
 {
     public sealed record WordContext(string Word, string? NativeLanguage, string? LearningLanguage);
 
-    public static bool IsJsonContentType(string? contentType)
+    public static async Task<WordContext> ReadAsync(byte[] body, string operation, HttpContext http)
     {
-        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed)) return false;
-        var type = parsed.MediaType.Value!;
-        return type.Equals("application/json", StringComparison.OrdinalIgnoreCase)
-            || type.Equals("text/json", StringComparison.OrdinalIgnoreCase)
-            || (type.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
-                && type.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
+        var requestType = operation switch
+        {
+            "analyze-word" => typeof(AnalyzeWordRequest),
+            "word-detail" => typeof(WordDetailRequest),
+            "review-translation" => typeof(ReviewTranslationRequest),
+            _ => throw new InvalidOperationException("unsupported AI operation")
+        };
+        var metadata = http.RequestServices.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(requestType);
+        var input = new InputFormatterContext(http, "", new ModelStateDictionary(), metadata,
+            (stream, encoding) => new StreamReader(stream, encoding));
+        var formatter = http.RequestServices.GetRequiredService<IOptions<MvcOptions>>().Value.InputFormatters
+            .OfType<SystemTextJsonInputFormatter>().FirstOrDefault(candidate => candidate.CanRead(input));
+        var encoding = formatter is null ? null : new EncodingSelector(formatter.SupportedEncodings).Select(input);
+        if (formatter is null || encoding is null)
+            throw new UnsupportedContentTypeException("unsupported AI JSON content type or charset");
+
+        if (encoding.CodePage == Encoding.UTF8.CodePage)
+            return Read(body, requestType, formatter.SerializerOptions);
+
+        // MVC transcodes supported non-UTF-8 encodings. Do the same on a separate copy:
+        // never replace the original stream/bytes used for binding and App Attest proof.
+        try
+        {
+            using var source = new MemoryStream(body, writable: false);
+            await using var transcoded = Encoding.CreateTranscodingStream(source, encoding, Encoding.UTF8);
+            using var interpretation = new MemoryStream();
+            await transcoded.CopyToAsync(interpretation, http.RequestAborted);
+            return Read(interpretation.ToArray(), requestType, formatter.SerializerOptions);
+        }
+        catch (DecoderFallbackException)
+        {
+            throw new InvalidOperationException("invalid AI JSON encoding");
+        }
+    }
+
+    // MVC exposes encoding selection as protected; this adapter reuses that implementation
+    // with the actual JSON formatter's supported encodings instead of duplicating its rules.
+    private sealed class EncodingSelector : TextInputFormatter
+    {
+        public EncodingSelector(IEnumerable<Encoding> encodings)
+        {
+            foreach (var encoding in encodings) SupportedEncodings.Add(encoding);
+        }
+        public Encoding? Select(InputFormatterContext context) => SelectCharacterEncoding(context);
+        public override Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
+            => throw new NotSupportedException("This adapter only selects request encoding");
     }
 
-    public static WordContext Read(byte[] body, string operation, JsonSerializerOptions options)
+    private static WordContext Read(byte[] body, Type requestType, JsonSerializerOptions options)
     {
         try
         {
-            using var document = JsonDocument.Parse(body, new JsonDocumentOptions
+            // Stream parsing accepts the optional UTF-8 BOM, like MVC's stream deserializer.
+            using var interpretation = new MemoryStream(body, writable: false);
+            using var document = JsonDocument.Parse(interpretation, new JsonDocumentOptions
             {
                 AllowTrailingCommas = options.AllowTrailingCommas,
                 CommentHandling = options.ReadCommentHandling,
                 MaxDepth = options.MaxDepth
             });
             if (document.RootElement.ValueKind != JsonValueKind.Object)
                 throw new InvalidOperationException("JSON object is required");
             var names = new HashSet<string>(options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
             foreach (var property in document.RootElement.EnumerateObject())
                 if (!names.Add(property.Name))
                     throw new InvalidOperationException("duplicate JSON property");
 
             WordContext context;
             string? secondary = null;
             var review = false;
-            switch (operation)
+            var request = document.RootElement.Deserialize(requestType, options);
+            switch (request)
             {
-                case "analyze-word":
-                    var analyze = document.RootElement.Deserialize<AnalyzeWordRequest>(options)!;
+                case AnalyzeWordRequest analyze:
                     context = new(analyze.Word, analyze.NativeLanguage, analyze.LearningLanguage);
                     break;
-                case "word-detail":
-                    var detail = document.RootElement.Deserialize<WordDetailRequest>(options)!;
+                case WordDetailRequest detail:
                     context = new(detail.Word, detail.NativeLanguage, detail.LearningLanguage);
                     secondary = detail.SecondaryLanguage;
                     break;
-                case "review-translation":
-                    var request = document.RootElement.Deserialize<ReviewTranslationRequest>(options)!;
-                    context = new(request.Word, request.NativeLanguage, request.LearningLanguage);
-                    secondary = request.SecondaryLanguage;
+                case ReviewTranslationRequest translation:
+                    context = new(translation.Word, translation.NativeLanguage, translation.LearningLanguage);
+                    secondary = translation.SecondaryLanguage;
                     review = true;
                     break;
                 default:
                     throw new InvalidOperationException("unsupported AI operation");
             }
             if (!WordAiService.TryValidateWordRequest(context.Word, context.NativeLanguage, context.LearningLanguage,
                     out var native, out _, out var error))
                 throw new InvalidOperationException(error);
             if (review && (string.IsNullOrWhiteSpace(context.NativeLanguage) || string.IsNullOrWhiteSpace(context.LearningLanguage)))
                 throw new InvalidOperationException("native and learning languages are required");
             if ((review || secondary != null) && !WordAiService.TryValidateSecondaryLanguage(secondary, native, out _, out error))
                 throw new InvalidOperationException(error);
diff --git a/tests/AiProtectionFilterTests.cs b/tests/AiProtectionFilterTests.cs
index d6bc729..644bfdc 100644
--- a/tests/AiProtectionFilterTests.cs
+++ b/tests/AiProtectionFilterTests.cs
@@ -1,23 +1,24 @@
 using System.Text.Json;
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
+using Microsoft.AspNetCore.Mvc.Formatters;
 using Microsoft.AspNetCore.Mvc.ModelBinding;
 using Microsoft.AspNetCore.Routing;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Logging.Abstractions;
 using Microsoft.Extensions.Options;
 using Xunit;
 
 namespace Mavrylo.Tests;
 
 public class AiProtectionFilterTests
 {
@@ -343,27 +344,30 @@ public class AiProtectionFilterTests
             new ChallengeService(db, timeProvider),
             new DeviceContextService(db, new EntitlementService(db, timeProvider)),
             new DeviceWordService(db, timeProvider),
             new AiUsageService(db, timeProvider),
             timeProvider,
             NullLogger<AiProtectionFilter>.Instance);
     }
 
     private static ResourceExecutingContext CreateContext(AppDbContext db, string body, string? bearerToken = null)
     {
         var jsonOptions = new JsonOptions();
         jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
+        var mvcOptions = new MvcOptions();
+        mvcOptions.InputFormatters.Add(new SystemTextJsonInputFormatter(jsonOptions, NullLogger<SystemTextJsonInputFormatter>.Instance));
         var services = new ServiceCollection()
             .AddSingleton(db)
-            .AddSingleton<IOptions<JsonOptions>>(Options.Create(jsonOptions))
+            .AddSingleton<IModelMetadataProvider>(new EmptyModelMetadataProvider())
+            .AddSingleton<IOptions<MvcOptions>>(Options.Create(mvcOptions))
             .BuildServiceProvider();
         var http = new DefaultHttpContext { RequestServices = services };
         http.Request.Method = "POST";
         http.Request.ContentType = "application/json";
         http.Request.Path = "/owlai/ai/word-detail";
         http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
         http.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
         if (bearerToken is not null)
             http.Request.Headers.Authorization = $"Bearer {bearerToken}";
 
         var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
         return new ResourceExecutingContext(
diff --git a/tests/AiRequestInterpretationTests.cs b/tests/AiRequestInterpretationTests.cs
index e8a1ef8..c55f385 100644
--- a/tests/AiRequestInterpretationTests.cs
+++ b/tests/AiRequestInterpretationTests.cs
@@ -37,24 +37,61 @@ public sealed class AiRequestInterpretationTests(PostgresContainerFixture postgr
         await using var factory = Factory(provider);
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         var key = await Device(db, client, scope);
         using var response = await client.PostAsync("/owlai/ai/" + route, new StringContent(body, Encoding.UTF8, "application/json"));
         Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
         Assert.Equal(0, provider.Calls);
         Assert.False(await db.DeviceWords.AnyAsync(w => w.DeviceUuid == key));
         Assert.False(await db.AiUsage.AnyAsync(w => w.KeyId == key));
     }
 
+    [Theory]
+    [InlineData(false, "utf-8", "utf-8", HttpStatusCode.OK, false)]
+    [InlineData(false, "utf-16", "utf-16", HttpStatusCode.OK, false)]
+    [InlineData(false, "utf-8", "utf-16", HttpStatusCode.BadRequest, false)]
+    [InlineData(false, "utf-8", "iso-8859-1", HttpStatusCode.UnsupportedMediaType, false)]
+    [InlineData(true, "utf-8", "utf-8", HttpStatusCode.OK, false)]
+    [InlineData(true, "utf-16", "utf-16", HttpStatusCode.OK, false)]
+    [InlineData(true, "utf-8", "utf-16", HttpStatusCode.BadRequest, false)]
+    [InlineData(true, "utf-8", "iso-8859-1", HttpStatusCode.UnsupportedMediaType, false)]
+    [InlineData(false, "utf-8", "utf-8", HttpStatusCode.OK, true)]
+    [InlineData(false, "utf-16", "utf-16", HttpStatusCode.OK, true)]
+    [InlineData(true, "utf-8", "utf-8", HttpStatusCode.OK, true)]
+    [InlineData(true, "utf-16", "utf-16", HttpStatusCode.OK, true)]
+    public async Task DeclaredEncodingMatchesMvcBeforeAnySpending(bool accountRoute, string wireEncoding, string charset, HttpStatusCode expected, bool withBom)
+    {
+        var provider = new Provider(); await using var factory = Factory(provider);
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var subject = accountRoute ? await Account(db, client, scope) : await Device(db, client, scope);
+        var word = "caf�-" + subject;
+        var body = "{\"WORD\":\"" + word + "\",\"NATIVE_LANGUAGE\":\"en\",\"LEARNING_LANGUAGE\":\"es\"}";
+        var encoding = wireEncoding == "utf-16" ? Encoding.Unicode : Encoding.UTF8;
+        var bytes = (withBom ? encoding.GetPreamble() : Array.Empty<byte>()).Concat(encoding.GetBytes(body)).ToArray();
+        using var content = new ByteArrayContent(bytes);
+        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = charset };
+        using var response = await client.PostAsync(accountRoute ? "/owlai/account/ai/word-detail" : "/owlai/ai/word-detail", content);
+        Assert.Equal(expected, response.StatusCode);
+        var succeeds = expected == HttpStatusCode.OK;
+        Assert.Equal(succeeds ? 1 : 0, provider.Calls);
+        var words = await db.DeviceWords.Where(w => w.DeviceUuid == subject).ToListAsync();
+        Assert.Equal(succeeds && !accountRoute ? 1 : 0, words.Count);
+        if (words.Count > 0) Assert.Equal(word, words[0].NormalizedWord);
+        var usage = await db.AiUsage.Where(w => w.KeyId == (accountRoute ? "account:" + subject : subject)).ToListAsync();
+        if (succeeds) Assert.All(usage, row => Assert.Equal(1, row.Count));
+        Assert.Equal(succeeds ? (accountRoute ? 2 : 1) : 0, usage.Count);
+    }
+
     [Theory]
     [InlineData("word", "native_language", "learning_language")]
     [InlineData("Word", "Native_Language", "Learning_Language")]
     [InlineData("WORD", "NATIVE_LANGUAGE", "LEARNING_LANGUAGE")]
     [InlineData("Word", "nativeLanguage", "learningLanguage")]
     public async Task LanguagesAndRepeatedWordUseSameMeaningAsMvc(string wordProperty, string nativeProperty, string learningProperty)
     {
         var provider = new Provider();
         await using var factory = Factory(provider);
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
@@ -96,35 +133,31 @@ public sealed class AiRequestInterpretationTests(PostgresContainerFixture postgr
     }
 
     [Theory]
     [InlineData("word-detail")]
     [InlineData("analyze-word")]
     [InlineData("review-translation")]
     public async Task AccountDuplicateBodyDoesNotSpendQuota(string route)
     {
         var provider = new Provider();
         await using var factory = Factory(provider);
         using var client = factory.CreateClient();
         using var scope = factory.Services.CreateScope();
-        var session = await scope.ServiceProvider.GetRequiredService<AccountService>().SignInAsync(new(Guid.NewGuid().ToString("N"), null, "Owner"), default, allowCreation: true);
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
-        db.Subscriptions.Add(new() { OriginalTransactionId = Guid.NewGuid().ToString("N"), DeviceUuid = "owner-device", OwnerAccountId = session.Profile.Id,
-            ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(5), WasEverPaid = true, Environment = "Production" });
-        await db.SaveChangesAsync();
-        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
+        var account = await Account(db, client, scope);
         using var response = await client.PostAsync("/owlai/account/ai/" + route,
             new StringContent("{\"word\":\"one\",\"Word\":\"two\",\"native_language\":\"en\",\"learning_language\":\"es\",\"secondary_language\":\"fr\"}", Encoding.UTF8, "application/json"));
         Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
         Assert.Equal(0, provider.Calls);
-        Assert.False(await db.AiUsage.AnyAsync(w => w.KeyId == "account:" + session.Profile.Id));
+        Assert.False(await db.AiUsage.AnyAsync(w => w.KeyId == "account:" + account));
     }
 
     [Theory]
     [InlineData("text/plain", false, HttpStatusCode.UnsupportedMediaType)]
     [InlineData("application/json", true, HttpStatusCode.RequestEntityTooLarge)]
     public async Task InvalidHttpEnvelopeDoesNotConsumeQuota(string contentType, bool oversized, HttpStatusCode expected)
     {
         var provider = new Provider(); await using var factory = Factory(provider);
         using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var key = await Device(db, client, scope);
         var body = "{\"word\":\"" + key + "\",\"padding\":\"" + (oversized ? new string('x', 16000) : "") + "\"}";
         using var response = await client.PostAsync("/owlai/ai/word-detail", new StringContent(body, Encoding.UTF8, contentType));
@@ -145,70 +178,88 @@ public sealed class AiRequestInterpretationTests(PostgresContainerFixture postgr
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var key = await Device(db, client, scope);
         for (var i = 0; i < 10; i++) db.DeviceWords.Add(new() { DeviceUuid = key, NormalizedWord = key + i, NativeLanguage = "en", LearningLanguage = "es", IsActive = true });
         await db.SaveChangesAsync();
         var body = JsonSerializer.Serialize(new Dictionary<string, string> { [property] = key + 0, ["NATIVE_LANGUAGE"] = "en", ["LEARNING_LANGUAGE"] = "es", ["SECONDARY_LANGUAGE"] = "fr" });
         using var response = await client.PostAsync("/owlai/ai/review-translation", new StringContent(body, Encoding.UTF8, "application/json"));
         Assert.Equal(HttpStatusCode.OK, response.StatusCode);
         Assert.Equal(10, await db.DeviceWords.CountAsync(w => w.DeviceUuid == key));
         Assert.Equal(1, await db.AiUsage.Where(w => w.KeyId == key).SumAsync(w => w.Count));
         Assert.Equal(1, provider.Calls);
     }
 
     [Theory]
-    [InlineData(false)]
-    [InlineData(true)]
-    public async Task RealAssertionStillVerifiesOriginalUppercaseBytes(bool mutateBody)
+    [InlineData(false, false, false)]
+    [InlineData(true, false, false)]
+    [InlineData(false, true, false)]
+    [InlineData(true, true, false)]
+    [InlineData(false, true, true)]
+    [InlineData(true, true, true)]
+    public async Task RealAssertionStillVerifiesOriginalUppercaseBytes(bool mutateBody, bool utf16, bool withBom)
     {
         var provider = new Provider();
         using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
         var verifier = new AppAttestVerifier(TestConfig.Create(), new FakeEnvironment("Production"), NullLogger<AppAttestVerifier>.Instance);
         await using var factory = new ApiFactory(postgres.ConnectionString, services =>
         {
             services.RemoveAll<IAiJsonService>(); services.AddSingleton<IAiJsonService>(provider);
             services.RemoveAll<IAppAttestVerifier>(); services.AddSingleton<IAppAttestVerifier>(verifier);
         }, new Dictionary<string, string?> { ["TestMode:Enabled"] = "false", ["AiProtection:RequireAssertion"] = "true", ["AiProtection:FreeDailyQuota"] = "40" });
         using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var key = await Device(db, client, scope);
         var device = await db.Devices.SingleAsync(d => d.KeyId == key); device.PublicKey = signing.ExportSubjectPublicKeyInfo(); await db.SaveChangesAsync();
         const string path = "/owlai/ai/WORD-DETAIL";
         var body = "{  \"WORD\":\"" + key + "\", \"NATIVE_LANGUAGE\":\"en\", \"LEARNING_LANGUAGE\":\"es\" }";
         var challenge = await scope.ServiceProvider.GetRequiredService<ChallengeService>().IssueAssertionAsync(key, path);
-        var clientHash = AppAttestClientData.ComputeAssertionHash(challenge.Nonce, Encoding.UTF8.GetBytes(body), path, challenge.ChallengeId);
+        var encoding = utf16 ? Encoding.Unicode : Encoding.UTF8;
+        var preamble = withBom ? encoding.GetPreamble() : Array.Empty<byte>();
+        var signedBytes = preamble.Concat(encoding.GetBytes(body)).ToArray();
+        var clientHash = AppAttestClientData.ComputeAssertionHash(challenge.Nonce, signedBytes, path, challenge.ChallengeId);
         var authData = new byte[37]; SHA256.HashData(Encoding.UTF8.GetBytes("TEAMID1234.com.mavrylo.owlai")).CopyTo(authData, 0); authData[36] = 1;
         var signature = signing.SignData(authData.Concat(clientHash).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
         var writer = new CborWriter(); writer.WriteStartMap(2); writer.WriteTextString("signature"); writer.WriteByteString(signature);
         writer.WriteTextString("authenticatorData"); writer.WriteByteString(authData); writer.WriteEndMap();
         using var request = new HttpRequestMessage(HttpMethod.Post, path);
         request.Headers.Add("X-App-Attest-Key-Id", key); request.Headers.Add("X-App-Attest-Challenge-Id", challenge.ChallengeId);
         request.Headers.Add("X-App-Attest-Assertion", Convert.ToBase64String(writer.Encode()));
-        request.Content = new StringContent(mutateBody ? body + " " : body, Encoding.UTF8, "application/json");
+        request.Content = new ByteArrayContent(preamble.Concat(encoding.GetBytes(mutateBody ? body + " " : body)).ToArray());
+        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = encoding.WebName };
         using var response = await client.SendAsync(request);
         Assert.Equal(mutateBody ? HttpStatusCode.Forbidden : HttpStatusCode.OK, response.StatusCode);
         Assert.Equal(mutateBody ? 0 : 1, provider.Calls);
         Assert.Equal(mutateBody ? 0 : 1, await db.DeviceWords.CountAsync(w => w.DeviceUuid == key));
         Assert.Equal(mutateBody ? 0 : 1, await db.AiUsage.Where(w => w.KeyId == key).SumAsync(w => w.Count));
     }
 
     private ApiFactory Factory(Provider provider, bool testMode = false) => new(postgres.ConnectionString,
         services => { services.RemoveAll<IAiJsonService>(); services.AddSingleton<IAiJsonService>(provider); },
-        new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode.ToString(), ["AiProtection:RequireAssertion"] = "false", ["AiProtection:FreeDailyQuota"] = "40" });
+        new Dictionary<string, string?> { ["TestMode:Enabled"] = testMode.ToString(), ["AiProtection:RequireAssertion"] = "false", ["AiProtection:FreeDailyQuota"] = "40", ["AccountAi:DailyQuota"] = "40", ["AccountAi:RequestsPerMinute"] = "40" });
 
     private static async Task<string> Device(AppDbContext db, HttpClient client, IServiceScope scope)
     {
         var key = Guid.NewGuid().ToString("N");
         db.Devices.Add(new() { KeyId = key, DeviceUuid = key }); await db.SaveChangesAsync();
         client.DefaultRequestHeaders.Authorization = new("Bearer", scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(key, "free").Token);
         return key;
     }
 
+    private static async Task<string> Account(AppDbContext db, HttpClient client, IServiceScope scope)
+    {
+        var session = await scope.ServiceProvider.GetRequiredService<AccountService>().SignInAsync(new(Guid.NewGuid().ToString("N"), null, "Owner"), default, allowCreation: true);
+        db.Subscriptions.Add(new() { OriginalTransactionId = Guid.NewGuid().ToString("N"), DeviceUuid = "owner-device", OwnerAccountId = session.Profile.Id,
+            ClaimedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(5), WasEverPaid = true, Environment = "Production" });
+        await db.SaveChangesAsync();
+        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
+        return session.Profile.Id;
+    }
+
     private sealed class Provider : IAiJsonService
     {
         public int Calls;
         public Task<JsonElement?> CompleteJsonAsync(string operation, string prompt, CancellationToken ct = default)
         {
             Calls++;
             return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { translations = new[] { "sample" }, translation = "sample", corrected_word = "sample", words = new[] { "sample" }, language_code = "fr", explanation = "sample" }));
         }
         public Task<JsonElement?> CompleteVisionJsonAsync(string operation, string prompt, byte[] image, string mime, CancellationToken ct = default) => CompleteJsonAsync(operation, prompt, ct);
     }
 }
 
diff --git a/tests/PaidAccessAuditRegressionTests.cs b/tests/PaidAccessAuditRegressionTests.cs
index 7ffd80f..ff006c2 100644
--- a/tests/PaidAccessAuditRegressionTests.cs
+++ b/tests/PaidAccessAuditRegressionTests.cs
@@ -2,24 +2,25 @@ using System.Net;
 using System.Text;
 using System.Text.Json;
 using Mavrylo.Data;
 using Mavrylo.Dtos;
 using Mavrylo.Filters;
 using Mavrylo.Models;
 using Mavrylo.Services;
 using Mavrylo.Tests.TestSupport;
 using Microsoft.AspNetCore.Http;
 using Microsoft.AspNetCore.Mvc;
 using Microsoft.AspNetCore.Mvc.Abstractions;
 using Microsoft.AspNetCore.Mvc.Filters;
+using Microsoft.AspNetCore.Mvc.Formatters;
 using Microsoft.AspNetCore.Mvc.ModelBinding;
 using Microsoft.AspNetCore.Routing;
 using Microsoft.EntityFrameworkCore;
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.DependencyInjection.Extensions;
 using Microsoft.Extensions.Logging.Abstractions;
 using Microsoft.Extensions.Options;
 using Xunit;
 
 namespace Mavrylo.Tests;
 
 // Security expectations, intentionally red on audit baseline 473a39b. Fake Apple/AI only.
@@ -234,26 +235,29 @@ public sealed class PaidAccessAuditRegressionTests
         db.Devices.Add(new DeviceEntity { KeyId = key, DeviceUuid = device, PublicKey = [1, 2, 3] });
         await db.SaveChangesAsync();
     }
 
     // The action represents one synthetic provider attempt. Every rejection must stop before it.
     private static async Task<(int Status, int ProviderCalls)> InvokeAi(AppDbContext db, string key, int quota)
     {
         var filter = new AiProtectionFilter(Options.Create(new AiProtectionOptions { RequireAssertion = true, FreeDailyQuota = quota }),
             Config, AcceptedAttestation(), new(db, Clock), new(db, new(db, Clock)), new(db, Clock, Config), new(db, Clock, Config),
             Clock, NullLogger<AiProtectionFilter>.Instance);
         var jsonOptions = new JsonOptions();
         jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
+        var mvcOptions = new MvcOptions();
+        mvcOptions.InputFormatters.Add(new SystemTextJsonInputFormatter(jsonOptions, NullLogger<SystemTextJsonInputFormatter>.Instance));
         using var services = new ServiceCollection().AddSingleton(db)
-            .AddSingleton<IOptions<JsonOptions>>(Options.Create(jsonOptions)).BuildServiceProvider();
+            .AddSingleton<IModelMetadataProvider>(new EmptyModelMetadataProvider())
+            .AddSingleton<IOptions<MvcOptions>>(Options.Create(mvcOptions)).BuildServiceProvider();
         var http = new DefaultHttpContext { RequestServices = services };
         http.Request.Path = "/owlai/ai/word-detail";
         http.Request.Method = "POST";
         http.Request.ContentType = "application/json";
         http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"word":"hola"}"""));
         http.Request.Headers.Authorization = "Bearer " + new JwtTokenService(Config, Clock).CreateDeviceToken(key, "premium").Token;
         var challenge = await new ChallengeService(db, Clock).IssueAssertionAsync(key, http.Request.Path);
         http.Request.Headers["X-App-Attest-Key-Id"] = key;
         http.Request.Headers["X-App-Attest-Challenge-Id"] = challenge.ChallengeId;
         http.Request.Headers["X-App-Attest-Assertion"] = "AQID";
         var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
         var context = new ResourceExecutingContext(action, [], new List<IValueProviderFactory>());
diff --git a/tests/TestModeRouteTests.cs b/tests/TestModeRouteTests.cs
index 45ee6a1..7653235 100644
--- a/tests/TestModeRouteTests.cs
+++ b/tests/TestModeRouteTests.cs
@@ -33,30 +33,31 @@ public class TestModeRouteTests(PostgresContainerFixture postgres) : IClassFixtu
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
-        // A valid word exercises the subscription gate; empty words below exercise test-mode input validation.
+        // Valid requests exercise both the subscription gate and test-mode AI/quota bypass.
         Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(path, new { word = "valid-word" })).StatusCode);
         var config = factory.Services.GetRequiredService<IConfiguration>();
         config["TestMode:Enabled"] = "true";
         for (var i = 0; i < 65; i++)
-            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);
+            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(path, new { word = "valid-" + session.Profile.Id + i })).StatusCode);
+        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { word = "" })).StatusCode);
 
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == "account:" + session.Profile.Id));
         Assert.False(await db.Subscriptions.AnyAsync(x => x.OwnerAccountId == session.Profile.Id));
         var ent = await client.GetFromJsonAsync<JsonElement>("/owlai/account/entitlement");
         Assert.Equal("free", ent.GetProperty("status").GetString());
 
         // A forged client header cannot keep test mode enabled after the server turns it off.
         config["TestMode:Enabled"] = "false";
         client.DefaultRequestHeaders.Add("X-Test-Mode", "true");
         Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync(path, new { word = "valid-word" })).StatusCode);
     }
@@ -108,25 +109,26 @@ public class TestModeRouteTests(PostgresContainerFixture postgres) : IClassFixtu
         await db.SaveChangesAsync();
         var token = scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(key, "account_required").Token;
         client.DefaultRequestHeaders.Authorization = new("Bearer", token);
         for (var i = 0; i < 12; i++)
         {
             var response = await client.PostAsJsonAsync("/owlai/device-words/upsert", new {
                 normalized_word = "word-" + i, display_word = "word-" + i, native_language = "en", learning_language = "es"
             });
             Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
         }
         Assert.Equal(12, await db.DeviceWords.CountAsync(x => x.DeviceUuid == id));
         for (var i = 0; i < 65; i++)
-            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
+            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "valid-" + id + i })).StatusCode);
+        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "" })).StatusCode);
         Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == key));
         Assert.False(await db.Subscriptions.AnyAsync(x => x.DeviceUuid == id));
         factory.Services.GetRequiredService<IConfiguration>()["TestMode:Enabled"] = "false";
         Assert.Equal(HttpStatusCode.PaymentRequired, (await client.PostAsJsonAsync("/owlai/ai/analyze-word", new { word = "valid-word" })).StatusCode);
     }
 
     [Theory]
     [InlineData(false)]
     [InlineData(true)]
     public async Task DevelopmentDeviceCannotRetainTestAccessWhenServerTurnsItOff(bool expired)
     {
         await using var factory = Factory("false", protectionEnabled: false);
