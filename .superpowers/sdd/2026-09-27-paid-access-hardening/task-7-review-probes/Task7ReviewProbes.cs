using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mavrylo.Data;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;

internal static class ReviewEntry
{
    private const string RuntimeDir = @"D:\07 Hobby\FlashcardAI\test-results\paid-access-hardening\backend\tests\bin\Release\net10.0";
    public static async Task Main()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(RuntimeDir, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        Console.WriteLine("RUNTIME " + RuntimeInformation.FrameworkDescription);
        try { await Probes.Run(); } catch (Exception ex) { Console.WriteLine("PROBE_HARNESS_FAILURE " + ex); Environment.ExitCode = 1; }
    }
}

internal static class Probes
{
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AiSpend:Enabled"] = "true", ["AiSpend:GlobalMicroUsd"] = "10000000", ["AiSpend:GuestMicroUsd"] = "10000000",
        ["AiSpend:AccountMicroUsd"] = "10000000", ["AiSpend:MaxConcurrentAttempts"] = "1",
        ["AiSpend:PriceProfile"] = "2026-09-27-model-ceilings-v1", ["AiSpend:MaxOutputTokens"] = "512",
        ["AiSpend:MaxPromptBytes"] = "16000", ["AiSpend:MaxImageBytes"] = "1024", ["AiSpend:MaxResponseBytes"] = "16000",
        ["AiSpend:DeadlineMilliseconds"] = "5000", ["OpenAI:ApiKey"] = "synthetic-key", ["Gemini:ApiKey"] = "synthetic-key", ["AI:Provider"] = "Gemini"
    }).Build();
    private static AppDbContext Db(string connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    private static AiSpendContext Context()
    {
        var http = new DefaultHttpContext(); AiSpendContext.AuthorizeAcceptedRequest(http, null, "review-authenticated-synthetic-key");
        return new AiSpendContext(new HttpContextAccessor { HttpContext = http });
    }
    private static AiProviderTransport Transport(HttpClient client, IAiSpendGuard guard) => new(new Factory(client), guard, Context(), new Events());
    private static IAiProviderJsonService Gemini(IConfiguration config, AiProviderTransport transport) => new GeminiJsonService(config, transport, NullLogger<GeminiJsonService>.Instance);
    private static void Check(bool ok, string assertion) { if (!ok) throw new Exception("Probe failed: " + assertion); }
    private static readonly string GeminiUsageOmittedZeros = """{"modelVersion":"gemini-2.5-flash","usageMetadata":{"serviceTier":"standard","promptTokenCount":100,"candidatesTokenCount":10,"totalTokenCount":110},"candidates":[{"content":{"parts":[{"text":"{\"translation\":\"Hello\"}"}]}}]}""";
    private static readonly string GeminiUsageExplicitZeros = GeminiUsageOmittedZeros.Replace("\"totalTokenCount\":110", "\"totalTokenCount\":110,\"cachedContentTokenCount\":0,\"thoughtsTokenCount\":0");

    public static async Task Run()
    {
        var fixture = new PostgresContainerFixture();
        await fixture.InitializeAsync();
        try
        {
            await using (var db = Db(fixture.ConnectionString))
            {
                await db.Database.MigrateAsync();
                await db.Database.OpenConnectionAsync(); Console.WriteLine("POSTGRES " + db.Database.GetDbConnection().ServerVersion);
            }
            await OmittedZeros(fixture.ConnectionString);
            await Reset(fixture.ConnectionString);
            await ExplicitZeros(fixture.ConnectionString);
            await Reset(fixture.ConnectionString);
            await CancellationRetains(fixture.ConnectionString);
            await LedgerUnavailable();
            await TimeoutBoundary();
            await Reset(fixture.ConnectionString);
            await DeadlineRoute(fixture.ConnectionString);
        }
        finally { await fixture.DisposeAsync(); }
        Console.WriteLine("ALL PROBES COMPLETED; NO REMOTE HTTP");
    }
    private static async Task Reset(string connection)
    {
        await using var db = Db(connection); await db.AiSpendReservations.ExecuteDeleteAsync(); await db.AiSpendBuckets.ExecuteDeleteAsync();
    }
    private static async Task OmittedZeros(string connection)
    {
        var config = Config(); var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(GeminiUsageOmittedZeros) });
        using var client = new HttpClient(handler); await using var db = Db(connection);
        var provider = Gemini(config, Transport(client, new AiSpendGuard(db, config, new Events())));
        var result = await provider.CompleteJsonAsync("word-detail", "return JSON for a synthetic word");
        Check(result != null, "Gemini succeeded");
        var row = await db.AiSpendReservations.AsNoTracking().SingleAsync();
        var bucket = await db.AiSpendBuckets.AsNoTracking().SingleAsync(x => x.Id == "global");
        Console.WriteLine($"OMITTED_ZERO_SUCCESS calls={handler.Calls} result={result!.Value.GetProperty("translation")} charged={row.ChargedMicroUsd} settled={row.SettledAt != null} pending={bucket.PendingAttempts}");
        try { await provider.CompleteJsonAsync("word-detail", "second synthetic word"); throw new Exception("Second call unexpectedly permitted"); }
        catch (AiSpendDeniedException e) { Console.WriteLine($"OMITTED_ZERO_NEXT reason={e.Reason} calls={handler.Calls}"); }
        Check(handler.Calls == 1 && row.SettledAt == null && bucket.PendingAttempts == 1, "omitted zeros reproduced persistent denial");
    }
    private static async Task ExplicitZeros(string connection)
    {
        var config = Config(); var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(GeminiUsageExplicitZeros) });
        using var client = new HttpClient(handler); await using var db = Db(connection);
        var provider = Gemini(config, Transport(client, new AiSpendGuard(db, config, new Events())));
        await provider.CompleteJsonAsync("word-detail", "first synthetic word");
        await provider.CompleteJsonAsync("word-detail", "second synthetic word");
        var rows = await db.AiSpendReservations.AsNoTracking().ToListAsync();
        var bucket = await db.AiSpendBuckets.AsNoTracking().SingleAsync(x => x.Id == "global");
        Check(handler.Calls == 2 && rows.All(x => x.SettledAt != null && x.ChargedMicroUsd == 99) && bucket.PendingAttempts == 0, "explicit zero control");
        Console.WriteLine($"EXPLICIT_ZERO_CONTROL calls={handler.Calls} eachCharged=99 settled={rows.Count(x => x.SettledAt != null)} pending={bucket.PendingAttempts}");
    }
    private static async Task CancellationRetains(string connection)
    {
        var config = Config(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(_ => { entered.SetResult(); return new(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream()) }; });
        using var client = new HttpClient(handler); await using var db = Db(connection); using var cancel = new CancellationTokenSource();
        var provider = Gemini(config, Transport(client, new AiSpendGuard(db, config, new Events())));
        var call = provider.CompleteJsonAsync("word-detail", "synthetic word", cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancel.Cancel();
        try { await call; throw new Exception("Client cancellation not honored"); } catch (OperationCanceledException) { }
        await using var restarted = Db(connection);
        var row = await restarted.AiSpendReservations.AsNoTracking().SingleAsync();
        var bucket = await restarted.AiSpendBuckets.AsNoTracking().SingleAsync(x => x.Id == "global");
        Check(row.SettledAt == null && row.ChargedMicroUsd == 568536 && bucket.PendingAttempts == 1, "postdispatch cancellation retained");
        Console.WriteLine($"POSTDISPATCH_CANCEL calls={handler.Calls} charged={row.ChargedMicroUsd} settled={row.SettledAt != null} pendingAfterNewContext={bucket.PendingAttempts}");
    }
    private static async Task LedgerUnavailable()
    {
        // Port 1 is loopback only. No remote database or API is contacted.
        var config = Config(); var handler = new Handler(_ => throw new Exception("Unreserved HTTP occurred"));
        using var client = new HttpClient(handler); await using var db = Db("Host=127.0.0.1;Port=1;Database=unused;Username=synthetic;Timeout=1;Pooling=false");
        var provider = Gemini(config, Transport(client, new AiSpendGuard(db, config, new Events())));
        try { await provider.CompleteJsonAsync("word-detail", "synthetic word"); throw new Exception("Ledger failure not denied"); }
        catch (AiSpendDeniedException e) { Check(e.Reason == "ledger_unavailable" && handler.Calls == 0, "ledger closed"); Console.WriteLine($"LEDGER_UNAVAILABLE reason={e.Reason} calls={handler.Calls}"); }
    }
    private static async Task TimeoutBoundary()
    {
        var config = Config(); config["AiSpend:DeadlineMilliseconds"] = "30";
        var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream()) });
        using var client = new HttpClient(handler); var guard = new FakeGuard();
        var provider = Gemini(config, Transport(client, guard));
        var alerts = new Alerts();
        var fallback = new FallbackAiJsonService(new[] { provider }, config, alerts, NullLogger<FallbackAiJsonService>.Instance);
        try { await fallback.CompleteJsonAsync("word-detail", "synthetic word"); throw new Exception("Deadline not honored"); }
        catch (OperationCanceledException) { Check(handler.Calls == 1 && alerts.Calls == 0 && guard.Completions == 1, "timeout terminal"); Console.WriteLine($"BODY_DEADLINE calls={handler.Calls} alerts={alerts.Calls} completionUnknown={guard.Unknown}"); }
    }
    private static async Task DeadlineRoute(string connection)
    {
        var config = Config().AsEnumerable().ToDictionary(x => x.Key, x => x.Value);
        config["AiSpend:DeadlineMilliseconds"] = "200";
        config["TestMode:Enabled"] = "true";
        config["AiProtection:RequireAssertion"] = "false";
        var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream()) });
        using var wire = new HttpClient(handler); var alerts = new Alerts();
        await using var factory = new ApiFactory(connection, services =>
        {
            services.RemoveAll<IHttpClientFactory>(); services.AddSingleton<IHttpClientFactory>(new Factory(wire));
            services.RemoveAll<IDevAlertEmailService>(); services.AddSingleton<IDevAlertEmailService>(alerts);
        }, config);
        factory.UseKestrel(0);
        using var api = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var session = await scope.ServiceProvider.GetRequiredService<AccountService>()
                .SignInAsync(new("review-deadline-" + Guid.NewGuid(), null, "Synthetic review"), default, allowCreation: true);
            api.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        }
        var result = await api.PostAsJsonAsync("/owlai/account/ai/word-detail", new
        { word = "review" + Guid.NewGuid().ToString("N"), native_language = "en", learning_language = "es" }, CancellationToken.None);
        var body = await result.Content.ReadAsStringAsync();
        await using var db = Db(connection); var row = await db.AiSpendReservations.AsNoTracking().SingleAsync();
        Console.WriteLine($"DEADLINE_HTTP_ROUTE status={(int)result.StatusCode} responseHasEstablishedCode={body.Contains("ai_translation_unavailable")} calls={handler.Calls} alerts={alerts.Calls} settled={row.SettledAt != null} callerCancellation=None");
        Check(result.StatusCode == HttpStatusCode.InternalServerError && !body.Contains("ai_translation_unavailable") && handler.Calls == 1 && alerts.Calls == 0 && row.SettledAt == null, "actual internal deadline HTTP behavior");
    }
    private sealed class Events : IAiSpendEvents { public void Record(string name, string scope, long value = 1) { } }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return Task.FromResult(response(request)); }
    }
    private sealed class FakeGuard : IAiSpendGuard
    {
        public int Completions; public bool Unknown;
        public Task<SpendReservation?> TryReserveAsync(AiAttempt attempt, CancellationToken ct) => Task.FromResult<SpendReservation?>(new(attempt.AttemptId, attempt.UpperBoundMicroUsd));
        public Task CompleteAsync(Guid id, AiAttemptUsage? usage, CancellationToken ct) { Completions++; Unknown = usage == null; return Task.CompletedTask; }
    }
    private sealed class Alerts : IDevAlertEmailService
    {
        public int Calls;
        public Task NotifyAiProvidersFailedAsync(string operation, string path, IReadOnlyList<AiProviderFailure> failures, CancellationToken ct = default) { Calls++; return Task.CompletedTask; }
    }
    private sealed class SlowStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return 0; }
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}



