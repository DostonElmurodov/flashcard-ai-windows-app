using System.Formats.Cbor;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Mavrylo.Tests;

public class AccountClaimProofTests
{
    [Theory]
    [InlineData("valid", 0)]
    [InlineData("account", 403)]
    [InlineData("transaction", 403)]
    [InlineData("path", 403)]
    [InlineData("audience", 401)]
    [InlineData("key", 403)]
    public async Task RealAssertionBindsRawAccountBodyPathAndDeviceIdentity(string mutation, int expected)
    {
        using var testDb = TestDb.Create();
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        testDb.Db.Devices.Add(new DeviceEntity { KeyId = "attested-key", PublicKey = signing.ExportSubjectPublicKeyInfo() });
        await testDb.Db.SaveChangesAsync();
        const string path = "/owlai/account/subscription/apple/claim";
        const string body = """{"account_id":"alice","jws_transaction":"purchase-proof"}""";
        var challenges = new ChallengeService(testDb.Db, TimeProvider.System);
        var challenge = await challenges.IssueAssertionAsync("attested-key", path);
        var clientHash = AppAttestClientData.ComputeAssertionHash(challenge.Nonce, Encoding.UTF8.GetBytes(body), path, challenge.ChallengeId);
        var authData = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes("TEAMID1234.com.mavrylo.owlai")).CopyTo(authData, 0);
        authData[36] = 1;
        var signature = signing.SignData(authData.Concat(clientHash).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var writer = new CborWriter(); writer.WriteStartMap(2);
        writer.WriteTextString("signature"); writer.WriteByteString(signature);
        writer.WriteTextString("authenticatorData"); writer.WriteByteString(authData); writer.WriteEndMap();
        var config = TestConfig.Create();
        var tokens = new JwtTokenService(config, TimeProvider.System);
        var bearer = mutation == "audience" ? tokens.CreateToken(new AppUser()) : tokens.CreateDeviceToken("attested-key", "free").Token;
        var rawBody = mutation switch { "account" => body.Replace("alice", "bob"), "transaction" => body.Replace("purchase-proof", "different"), _ => body };
        using var services = new ServiceCollection().AddSingleton(testDb.Db).BuildServiceProvider();
        var account = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice")], "Account"));
        var http = new DefaultHttpContext { User = account, RequestServices = services };
        http.Request.Path = mutation == "path" ? "/owlai/account/wrong" : path;
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(rawBody));
        http.Request.Headers["X-Device-Authorization"] = "Bearer " + bearer;
        http.Request.Headers["X-App-Attest-Key-Id"] = mutation == "key" ? "other-key" : "attested-key";
        http.Request.Headers["X-App-Attest-Challenge-Id"] = challenge.ChallengeId;
        http.Request.Headers["X-App-Attest-Assertion"] = Convert.ToBase64String(writer.Encode());
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ResourceExecutingContext(action, [], new List<IValueProviderFactory>());
        var verifier = new AppAttestVerifier(config, new FakeEnvironment("Production"), NullLogger<AppAttestVerifier>.Instance);
        var filter = new AccountClaimProofFilter(new(config, new(testDb.Db, config, TimeProvider.System)), new(verifier, challenges, TimeProvider.System, NullLogger<AppAttestAssertionFilter>.Instance));
        var called = false;
        await filter.OnResourceExecutionAsync(context, async () =>
        {
            called = true;
            Assert.Same(account, http.User);
            Assert.Equal(body, await new StreamReader(http.Request.Body, leaveOpen: true).ReadToEndAsync());
            return new ResourceExecutedContext(action, []);
        });
        Assert.Equal(expected == 0, called);
        Assert.Same(account, http.User);
        Assert.Equal(expected, (context.Result as ObjectResult)?.StatusCode ?? 0);
        if (mutation == "valid")
        {
            http.Request.Body.Position = 0;
            var replay = new ResourceExecutingContext(action, [], new List<IValueProviderFactory>());
            await filter.OnResourceExecutionAsync(replay, () => throw new InvalidOperationException("Replay reached action."));
            Assert.Equal(403, Assert.IsType<ObjectResult>(replay.Result).StatusCode);
        }
    }
}
