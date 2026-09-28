using System.Net;
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
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

// Bounded, local characterization tests. A passing reproduction confirms existing behavior,
// not the desired payment security invariant. No Apple or AI provider network calls.
public sealed class ServerAuditTests
{
    [Theory]
    [InlineData(false, "premium")]
    [InlineData(true, "account_required")]
    public async Task RegistrationWithExistingUuidUsesExistingSubscription(bool claimed, string expected)
    {
        using var fixture = new LocalDb();
        var db = fixture.Db;
        db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId = "original-1", DeviceUuid = "known-owner-uuid",
            ProductId = "monthly", ExpiresAt = DateTime.UtcNow.AddDays(10), WasEverPaid = true,
            Environment = "Production", ClaimedAt = claimed ? DateTime.UtcNow : null });
        await db.SaveChangesAsync();
        var challenges = new ChallengeService(db, TimeProvider.System);
        var challenge = await challenges.IssueBootstrapAsync();
        var registration = new AppAttestRegistrationService(db, challenges, new AcceptedAttestation(),
            new JwtTokenService(Config(), TimeProvider.System), TimeProvider.System, NullLogger<AppAttestRegistrationService>.Instance);
        var registered = await registration.RegisterAsync(new AppAttestRegisterRequest("new-real-key", "AQID",
            Convert.ToBase64String(challenge.Nonce), challenge.ChallengeId, "known-owner-uuid"), default);
        Assert.Equal(200, registered.Status);
        var context = await new DeviceContextService(db, new EntitlementService(db, TimeProvider.System)).ResolveAsync("new-real-key");
        Assert.Equal(expected, context!.Entitlement.Status);
        var result = await InvokeFilter(db, "new-real-key", "word-detail", """{"word":"nuevo","native_language":"en","learning_language":"es"}""", 0);
        Assert.Equal(claimed ? 402 : 200, result);
    }

    [Theory]
    [InlineData(true, false, "Production", 200)]
    [InlineData(false, true, "Production", 200)]
    [InlineData(false, false, "Sandbox", 200)]
    public async Task ActiveTrialGraceAndCachedSandboxHaveNoFreeDailyQuota(bool trial, bool grace, string environment, int expected)
    {
        using var fixture = new LocalDb(); var db = fixture.Db;
        db.Devices.Add(new DeviceEntity { KeyId = "key", DeviceUuid = "device", PublicKey = [1,2,3] });
        db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId = "sub", DeviceUuid = "device", IsTrial = trial,
            WasEverPaid = !trial, Status = grace ? "grace" : "premium", ExpiresAt = DateTime.UtcNow.AddDays(1), Environment = environment });
        await db.SaveChangesAsync();
        for (var i = 0; i < 3; i++) Assert.Equal(expected, await InvokeFilter(db,"key","word-detail", """{"word":"hola"}""", 0));
        Assert.Empty(db.AiUsage); Assert.Empty(db.DeviceWords);
    }

    [Fact]
    public async Task FreeQuotaIsPerAttestationKeyRatherThanDeviceUuid()
    {
        using var fixture = new LocalDb(); var db = fixture.Db;
        db.Devices.AddRange(new DeviceEntity { KeyId = "key-a", DeviceUuid = "same-device", PublicKey=[1] },
            new DeviceEntity { KeyId = "key-b", DeviceUuid = "same-device", PublicKey=[2] });
        await db.SaveChangesAsync();
        const string body = """{"word":"hola"}""";
        Assert.Equal(200, await InvokeFilter(db,"key-a","word-detail",body,1));
        Assert.Equal(429, await InvokeFilter(db,"key-a","word-detail",body,1));
        Assert.Equal(200, await InvokeFilter(db,"key-b","word-detail",body,1));
        Assert.Equal(2, await db.AiUsage.SumAsync(x=>x.Count));
    }

    [Theory]
    [InlineData("expired",402)]
    [InlineData("revoked",402)]
    [InlineData("free",429)]
    public async Task NegativeControlsBlockExpiredRevokedAndExhaustedFree(string state, int expected)
    {
        using var fixture = new LocalDb(); var db = fixture.Db;
        db.Devices.Add(new DeviceEntity { KeyId="key",DeviceUuid="device",PublicKey=[1] });
        if(state!="free") db.Subscriptions.Add(new SubscriptionEntity { OriginalTransactionId="sub", DeviceUuid="device", WasEverPaid=true,
            ExpiresAt=state=="expired"?DateTime.UtcNow.AddDays(-1):DateTime.UtcNow.AddDays(1), RevokedAt=state=="revoked"?DateTime.UtcNow:null });
        await db.SaveChangesAsync();
        Assert.Equal(expected,await InvokeFilter(db,"key","word-detail","""{"word":"hola"}""",0));
    }

    static IConfiguration Config()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Jwt:Key"]="audit-only-01234567890123456789012345678901234567890123456789", ["Jwt:Issuer"]="audit",
        ["TestMode:Enabled"]="false", ["Apple:AppStoreServer:Environment"]="Production" }).Build();

    static async Task<int> InvokeFilter(AppDbContext db,string key,string action,string body,int quota)
    {
        var config=Config(); var clock=TimeProvider.System;
        var filter=new AiProtectionFilter(Options.Create(new AiProtectionOptions { Enabled=true,RequireAssertion=true,FreeDailyQuota=quota }),
            config,new AcceptedAttestation(),new ChallengeService(db,clock),new DeviceContextService(db,new EntitlementService(db,clock)),
            new DeviceWordService(db,clock,config),new AiUsageService(db,clock,config),clock,NullLogger<AiProtectionFilter>.Instance);
        using var services=new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var http=new DefaultHttpContext {RequestServices=services};
        http.Request.Path="/owlai/ai/"+action; http.Request.Method="POST";
        http.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(body)); http.Request.ContentLength=Encoding.UTF8.GetByteCount(body);
        http.Request.Headers.Authorization="Bearer "+new JwtTokenService(config,clock).CreateDeviceToken(key,"premium").Token;
        var challenge=await new ChallengeService(db,clock).IssueAssertionAsync(key,http.Request.Path.Value!);
        http.Request.Headers["X-App-Attest-Key-Id"]=key;
        http.Request.Headers["X-App-Attest-Challenge-Id"]=challenge.ChallengeId;
        http.Request.Headers["X-App-Attest-Assertion"]="AQID";
        var actionContext=new ActionContext(http,new RouteData(),new ActionDescriptor());
        var context=new ResourceExecutingContext(actionContext,[],new List<IValueProviderFactory>());
        var reached=false;
        await filter.OnResourceExecutionAsync(context,()=>{reached=true; return Task.FromResult(new ResourceExecutedContext(actionContext,[]){Result=new OkResult()});});
        return reached?200:(context.Result as ObjectResult)?.StatusCode??500;
    }
    sealed class LocalDb:IDisposable
    {
        readonly SqliteConnection connection=new("DataSource=:memory:"); public AppDbContext Db {get;}
        public LocalDb(){ connection.Open(); Db=new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); Db.Database.EnsureCreated(); }
        public void Dispose(){Db.Dispose();connection.Dispose();}
    }
}

public sealed class JsonBindingAudit(PostgresContainerFixture postgres):IClassFixture<PostgresContainerFixture>
{
    [Theory]
    [InlineData("word",10,10)]
    [InlineData("Word",11,0)]
    public async Task WordPropertyCaseChangesFreeReservation(string property,int expectedProviderCalls,int expectedReservations)
    {
        var provider=new FakeProvider();
        await using var factory=new ApiFactory(postgres.ConnectionString, services=>{
            services.RemoveAll<IAppAttestVerifier>(); services.AddSingleton<IAppAttestVerifier>(new AcceptedAttestation());
            services.RemoveAll<IAiJsonService>(); services.AddSingleton<IAiJsonService>(provider);
        },new Dictionary<string,string?> { ["TestMode:Enabled"]="false",["AiProtection:Enabled"]="true",
            ["AiProtection:RequireAssertion"]="true",["AiProtection:FreeDailyQuota"]="40" });
        using var client=factory.CreateClient();
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key=Guid.NewGuid().ToString("N");
        db.Devices.Add(new DeviceEntity { KeyId=key,DeviceUuid=key,PublicKey=[1,2,3],Environment="production" }); await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization=new("Bearer",scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateDeviceToken(key,"free").Token);
        const string path="/owlai/ai/word-detail";
        for(var i=0;i<11;i++) {
            var challenge=await scope.ServiceProvider.GetRequiredService<ChallengeService>().IssueAssertionAsync(key,path);
            using var request=new HttpRequestMessage(HttpMethod.Post,path);
            request.Headers.Add("X-App-Attest-Key-Id",key); request.Headers.Add("X-App-Attest-Challenge-Id",challenge.ChallengeId); request.Headers.Add("X-App-Attest-Assertion","AQID");
            request.Content=new StringContent(JsonSerializer.Serialize(new Dictionary<string,string>{{property,key+i},{"native_language","en"},{"learning_language","es"}}),Encoding.UTF8,"application/json");
            using var response=await client.SendAsync(request);
            Assert.Equal(i<expectedProviderCalls?HttpStatusCode.OK:HttpStatusCode.PaymentRequired,response.StatusCode);
        }
        Assert.Equal(expectedProviderCalls,provider.Calls);
        Assert.Equal(expectedReservations,await db.DeviceWords.CountAsync(x=>x.DeviceUuid==key));
        Assert.Equal(expectedProviderCalls,await db.AiUsage.Where(x=>x.KeyId==key).SumAsync(x=>x.Count));
    }
    sealed class FakeProvider:IAiJsonService {
        public int Calls;
        public Task<JsonElement?> CompleteJsonAsync(string op,string prompt,CancellationToken ct=default){Calls++; return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new {translations=new[]{"sample"},translation="sample",corrected_word="sample"}));}
        public Task<JsonElement?> CompleteVisionJsonAsync(string op,string prompt,byte[] image,string mime,CancellationToken ct=default)=>throw new InvalidOperationException("Unexpected vision call");
    }
}
sealed class AcceptedAttestation:IAppAttestVerifier {
    public bool IsDevelopmentBypassEnabled=>false;
    public AppAttestVerifier.AttestationResult VerifyAttestation(string key,byte[] proof,byte[] nonce)=>AppAttestVerifier.AttestationResult.Success([1,2,3],0,"production");
    public AppAttestVerifier.AssertionResult VerifyAssertion(byte[] proof,byte[] publicKey,long count,byte[] hash)=>AppAttestVerifier.AssertionResult.Success(count+1);
}
