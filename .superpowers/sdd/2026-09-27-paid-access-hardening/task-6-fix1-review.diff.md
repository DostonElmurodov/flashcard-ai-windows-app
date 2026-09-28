# Task 6 fix round 1 review package
Base: 3092b09f2e95668a4302b8e38c747fba2a668e24
Head: e7dfd1aa3874a724b51af126c4979d50ca8f0992

e7dfd1a Validate MVC form language aliases before AI quota
 .../Services/ExtractionRequestValidator.cs         | 29 +++++--
 tests/PaidAccessBoundaryHttpTests.cs               | 98 ++++++++++++++++++++++
 2 files changed, 121 insertions(+), 6 deletions(-)
diff --git a/src/Mavrylo.Services/Services/ExtractionRequestValidator.cs b/src/Mavrylo.Services/Services/ExtractionRequestValidator.cs
index 78d7870..496984b 100644
--- a/src/Mavrylo.Services/Services/ExtractionRequestValidator.cs
+++ b/src/Mavrylo.Services/Services/ExtractionRequestValidator.cs
@@ -1,11 +1,13 @@
 using Microsoft.AspNetCore.Http;
+using Microsoft.AspNetCore.Mvc;
+using Microsoft.AspNetCore.Mvc.ModelBinding;
 
 namespace Mavrylo.Services;
 
 /// <summary>Validates the same cached form and image bytes MVC later binds for extraction.</summary>
 public static class ExtractionRequestValidator
 {
     public const long MaxBodyBytes = 20_000_000;
 
     public static async Task<string?> ValidateAsync(HttpRequest request, CancellationToken ct)
     {
@@ -21,31 +23,46 @@ public static class ExtractionRequestValidator
             || form.Files[0].Length == 0)
             return "image required";
         var image = form.Files[0];
         await using var stream = image.OpenReadStream();
         var header = new byte[Math.Min(12, (int)image.Length)];
         try { await stream.ReadExactlyAsync(header, ct); }
         catch (EndOfStreamException) { return "invalid image payload"; }
         if (!TryDetectImageMime(header, out _))
             return "unsupported image payload";
 
-        var target = form["targetLanguage"];
-        var native = form["nativeLanguage"];
-        if (target.Count > 1 || native.Count > 1)
-            return "duplicate language field";
-        if (!SupportedLanguages.TryNormalize(target.Count == 0 ? "en" : target.ToString(), out _))
+        // Resolve the cached form with MVC's own jQuery normalization. Aliases such as
+        // targetLanguage[] must be validated, while mixed or duplicate values are ambiguous.
+        var mvcContext = new ValueProviderFactoryContext(new ActionContext { HttpContext = request.HttpContext });
+        await new JQueryFormValueProviderFactory().CreateValueProviderAsync(mvcContext);
+        var mvcValues = mvcContext.ValueProviders.Single();
+        if (!TryGetLanguageValue(form, mvcValues.GetValue("targetLanguage"), "targetLanguage", out var target)
+            || !TryGetLanguageValue(form, mvcValues.GetValue("nativeLanguage"), "nativeLanguage", out var native))
+            return "ambiguous language field";
+        if (!SupportedLanguages.TryNormalize(target, out _))
             return "unsupported target language";
-        if (!SupportedLanguages.TryNormalize(native.Count == 0 ? "en" : native.ToString(), out _))
+        if (!SupportedLanguages.TryNormalize(native, out _))
             return "unsupported native language";
         return null;
     }
 
+    private static bool TryGetLanguageValue(IFormCollection form, ValueProviderResult mvc, string canonical, out string? value)
+    {
+        var literal = form[canonical];
+        var normalized = mvc.Values;
+        value = normalized.Count == 1 ? normalized[0] : literal.Count == 1 ? literal[0] : "en";
+        return literal.Count <= 1 && normalized.Count <= 1
+            && (literal.Count == 0 || !form.Keys.Any(key =>
+                key.StartsWith(canonical + "[", StringComparison.OrdinalIgnoreCase)))
+            && (literal.Count == 0 || normalized.Count == 0 || literal[0] == normalized[0]);
+    }
+
     public static bool TryDetectImageMime(ReadOnlySpan<byte> bytes, out string mime)
     {
         mime = "";
         if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
         { mime = "image/jpeg"; return true; }
         if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
             && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
         { mime = "image/png"; return true; }
         if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
             && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
diff --git a/tests/PaidAccessBoundaryHttpTests.cs b/tests/PaidAccessBoundaryHttpTests.cs
index c7bd6a2..74ed90b 100644
--- a/tests/PaidAccessBoundaryHttpTests.cs
+++ b/tests/PaidAccessBoundaryHttpTests.cs
@@ -51,20 +51,69 @@ public sealed class PaidAccessBoundaryHttpTests(PostgresContainerFixture postgre
             form.Add(new StringContent("fr"), "targetLanguage");
         }
         using var response = await client.PostAsync(Path(accountRoute), form);
         Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
         Assert.Equal(0, provider.Calls);
         Assert.Equal(beforeSpend, await db.AiSpendReservations.CountAsync());
         Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == (accountRoute ? "account:" + subject : subject)));
         Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == subject));
     }
 
+    [Theory]
+    [InlineData(false, "target-alias")]
+    [InlineData(true, "target-alias")]
+    [InlineData(false, "native-alias")]
+    [InlineData(true, "native-alias")]
+    [InlineData(false, "target-mixed")]
+    [InlineData(true, "target-mixed")]
+    [InlineData(false, "native-mixed")]
+    [InlineData(true, "native-mixed")]
+    [InlineData(false, "target-duplicate-alias")]
+    [InlineData(true, "target-duplicate-alias")]
+    [InlineData(false, "native-duplicate-alias")]
+    [InlineData(true, "native-duplicate-alias")]
+    [InlineData(false, "target-mixed-same")]
+    [InlineData(true, "target-mixed-same")]
+    [InlineData(false, "native-mixed-same")]
+    [InlineData(true, "native-mixed-same")]
+    [InlineData(false, "target-duplicate-same")]
+    [InlineData(true, "target-duplicate-same")]
+    [InlineData(false, "target-case-mixed")]
+    [InlineData(true, "target-case-mixed")]
+    public async Task InvalidOrAmbiguousMvcLanguageAliasesDoNotSpend(bool accountRoute, string variant)
+    {
+        var provider = new CountingProvider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var subject = accountRoute ? await Account(db, client, scope) : await Device(db, client, scope);
+        var beforeSpend = await db.AiSpendReservations.CountAsync();
+        using var form = new MultipartFormDataContent();
+        form.Add(new ByteArrayContent([0xff, 0xd8, 0xff, 1]), "image", "photo.jpg");
+        if (variant.StartsWith("target-mixed", StringComparison.Ordinal) || variant == "target-case-mixed")
+            form.Add(new StringContent("es"), "targetLanguage");
+        if (variant.StartsWith("native-mixed", StringComparison.Ordinal))
+            form.Add(new StringContent("fr"), "nativeLanguage");
+        var target = variant.StartsWith("target", StringComparison.Ordinal);
+        var alias = variant == "target-case-mixed" ? "TARGETLANGUAGE[]" : target ? "targetLanguage[]" : "nativeLanguage[]";
+        var duplicate = variant.Contains("duplicate", StringComparison.Ordinal);
+        var same = variant.EndsWith("same", StringComparison.Ordinal) || variant == "target-case-mixed";
+        form.Add(new StringContent(duplicate || same ? (target ? "es" : "fr") : "unsupported-language"), alias);
+        if (duplicate) form.Add(new StringContent(same ? (target ? "es" : "fr") : (target ? "fr" : "es")), alias);
+        using var response = await client.PostAsync(Path(accountRoute), form);
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+        Assert.Equal(0, provider.Calls);
+        Assert.Equal(beforeSpend, await db.AiSpendReservations.CountAsync());
+        Assert.False(await db.AiUsage.AnyAsync(x => x.KeyId == (accountRoute ? "account:" + subject : subject)));
+        Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == subject));
+    }
+
     [Theory]
     [InlineData(false)]
     [InlineData(true)]
     public async Task MalformedMultipartDoesNotConsumeQuota(bool accountRoute)
     {
         var provider = new CountingProvider();
         await using var factory = Factory(provider);
         using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
         var subject = accountRoute ? await Account(db, client, scope) : await Device(db, client, scope);
@@ -120,20 +169,69 @@ public sealed class PaidAccessBoundaryHttpTests(PostgresContainerFixture postgre
         Assert.Equal(HttpStatusCode.OK, response.StatusCode);
         Assert.Equal(1, provider.Calls);
         Assert.Equal("image/jpeg", provider.Mime);
         Assert.Equal(new byte[] { 0xff, 0xd8, 0xff, 1 }, provider.Image);
         Assert.Contains("Spanish", provider.Prompt);
         Assert.Contains("French", provider.Prompt);
         Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == subject));
         Assert.Equal(accountRoute ? 2 : 1, await db.AiUsage.Where(x => x.KeyId == (accountRoute ? "account:" + subject : subject)).SumAsync(x => x.Count));
     }
 
+    [Theory]
+    [InlineData(false, false)]
+    [InlineData(true, false)]
+    [InlineData(false, true)]
+    [InlineData(true, true)]
+    public async Task MissingLanguageFieldsKeepEnglishDefaults(bool accountRoute, bool targetProvided)
+    {
+        var provider = new CountingProvider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var subject = accountRoute ? await Account(db, client, scope) : await Device(db, client, scope);
+        using var form = new MultipartFormDataContent();
+        form.Add(new ByteArrayContent([0xff, 0xd8, 0xff, 1]), "image", "photo.jpg");
+        if (targetProvided) form.Add(new StringContent("es"), "targetLanguage");
+        using var response = await client.PostAsync(Path(accountRoute), form);
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        Assert.Equal(1, provider.Calls);
+        Assert.Contains(targetProvided ? "Spanish" : "English", provider.Prompt);
+        Assert.Contains("English", provider.Prompt);
+        Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == subject));
+        Assert.Equal(accountRoute ? 2 : 1,
+            await db.AiUsage.Where(x => x.KeyId == (accountRoute ? "account:" + subject : subject)).SumAsync(x => x.Count));
+    }
+
+    [Theory]
+    [InlineData(false, "targetLanguage[]", "es", "Spanish")]
+    [InlineData(true, "targetLanguage[]", "es", "Spanish")]
+    [InlineData(false, "nativeLanguage[]", "fr", "French")]
+    [InlineData(true, "nativeLanguage[]", "fr", "French")]
+    public async Task SingleSupportedMvcLanguageAliasUsesValidatedValue(bool accountRoute, string field, string language, string name)
+    {
+        var provider = new CountingProvider();
+        await using var factory = Factory(provider);
+        using var client = factory.CreateClient(); using var scope = factory.Services.CreateScope();
+        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
+        var subject = accountRoute ? await Account(db, client, scope) : await Device(db, client, scope);
+        using var form = new MultipartFormDataContent();
+        form.Add(new ByteArrayContent([0xff, 0xd8, 0xff, 1]), "image", "photo.jpg");
+        form.Add(new StringContent(language), field);
+        using var response = await client.PostAsync(Path(accountRoute), form);
+        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
+        Assert.Equal(1, provider.Calls);
+        Assert.Contains(name, provider.Prompt);
+        Assert.False(await db.DeviceWords.AnyAsync(x => x.DeviceUuid == subject));
+        Assert.Equal(accountRoute ? 2 : 1,
+            await db.AiUsage.Where(x => x.KeyId == (accountRoute ? "account:" + subject : subject)).SumAsync(x => x.Count));
+    }
+
     [Fact]
     public async Task MultipartAssertionVerifiesExactRawBodyBeforeValidationAndSpending()
     {
         var provider = new CountingProvider();
         var verifier = new AppAttestVerifier(TestConfig.Create(), new FakeEnvironment("Production"), NullLogger<AppAttestVerifier>.Instance);
         await using var factory = new ApiFactory(postgres.ConnectionString, services =>
         {
             services.RemoveAll<IAiJsonService>(); services.AddSingleton<IAiJsonService>(provider);
             services.RemoveAll<IAppAttestVerifier>(); services.AddSingleton<IAppAttestVerifier>(verifier);
         }, new Dictionary<string, string?> { ["TestMode:Enabled"] = "false", ["AiProtection:RequireAssertion"] = "true", ["AiProtection:FreeDailyQuota"] = "40" });
