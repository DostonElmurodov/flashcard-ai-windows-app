using System.Reflection;
using System.Text.Json;
using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Mavrylo.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

var now=DateTime.UtcNow;
var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["TestMode:Enabled"]="false",["Jwt:Key"]=new string('a',64),["Jwt:Issuer"]="audit",["Apple:AppStoreServer:BundleId"]="app",["Apple:AppStoreServer:Environment"]="Production",["Apple:AppStoreServer:AllowedProductIds"]="monthly"}).Build();
var real=new AppStoreServerClient(new HttpClient(new RejectNetwork()),cfg,new Env(),NullLogger<AppStoreServerClient>.Instance,TimeProvider.System);
var selector=typeof(AppStoreServerClient).GetMethod("SelectBestLastTransaction",BindingFlags.Instance|BindingFlags.NonPublic)!;
var selected=selector.Invoke(real,new object[]{JsonSerializer.SerializeToElement(new {data=new[]{
    new {subscriptionGroupIdentifier="group-a",lastTransactions=new[]{new {status=5,signedTransactionInfo="refunded-A"}}},
    new {subscriptionGroupIdentifier="group-b",lastTransactions=new[]{new {status=1,signedTransactionInfo="active-B"}}}
}})})!;
Check((string)selected.GetType().GetProperty("SignedTransactionInfo")!.GetValue(selected)! == "active-B","real status selector chooses active B over refunded A across subscription groups");
var projection=typeof(AppStoreServerClient).GetMethod("ProjectTransaction",BindingFlags.Instance|BindingFlags.NonPublic)!;
var missingExpiry=(SubscriptionEntity)projection.Invoke(real,new object?[]{JsonSerializer.SerializeToElement(new {originalTransactionId="no-expiry",productId="monthly",bundleId="app",environment="Production",appAccountToken="victim-device"}),"attacker-device"})!;
Check(missingExpiry.ExpiresAt==null && EntitlementService.ComputeStatus(missingExpiry)=="premium","validated payload missing expiry becomes perpetual premium; projection-only, signature not bypassed");
using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("DataSource=:memory:").Options);
await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
var ent=new EntitlementService(db,TimeProvider.System); var context=new DeviceContextService(db,ent);
db.Devices.Add(new DeviceEntity{KeyId="attacker-key",DeviceUuid="attacker-device"});
db.Subscriptions.Add(new SubscriptionEntity{OriginalTransactionId="cached-sandbox",DeviceUuid="attacker-device",ExpiresAt=now.AddHours(1),Environment="Sandbox"}); await db.SaveChangesAsync();
Check((await context.ResolveAsync("attacker-key"))!.Entitlement.Status=="premium","cached Sandbox row grants premium in Production-configured device resolver");
await db.Subscriptions.ExecuteDeleteAsync(); db.ChangeTracker.Clear();
var fake=new Fake{Verified=Purchase("victim", "victim-device"),Canonical=Purchase("other", "victim-device")};
var iap=new IapService(fake,ent,context,new JwtTokenService(cfg,TimeProvider.System),TimeProvider.System,NullLogger<IapService>.Instance);
var result=await iap.VerifyAsync("attacker-key",new IapVerifyRequest("copied-valid-signed-proof"),default);
Check(result.Status==200 && (await db.Subscriptions.SingleAsync()).OriginalTransactionId=="other" && (await context.ResolveAsync("attacker-key"))!.Entitlement.Status=="premium","device verify accepts canonical identity mismatch and relinks purchase to caller; valid-proof/API trust modeled by fake");
await db.Subscriptions.ExecuteDeleteAsync(); db.ChangeTracker.Clear();
fake.Verified=Purchase("same-original","victim-device"); fake.Canonical=Purchase("same-original","victim-device");
result=await iap.VerifyAsync("attacker-key",new IapVerifyRequest("copied-valid-signed-proof"),default);
Check(result.Status==200 && (await db.Subscriptions.SingleAsync()).DeviceUuid=="attacker-device","same-original verified purchase can also be relinked to another device; restore policy requires explicit ownership proof");
await db.Subscriptions.ExecuteDeleteAsync(); db.ChangeTracker.Clear();
db.Users.Add(new AppUser {Id="first-claim-account",Email="local-audit@example.test"}); await db.SaveChangesAsync();
fake.Verified=Purchase("unclaimed-original","victim-device"); fake.Canonical=Purchase("unclaimed-original","victim-device");
var accounts=new AccountEntitlementService(db,ent,fake,new SubscriptionOwnershipService(db,TimeProvider.System),TimeProvider.System,new Env());
var claimed=await accounts.ClaimAsync("first-claim-account","attacker-key","copied-valid-signed-proof",default);
Check(claimed.Status==200 && (await db.Subscriptions.AsNoTracking().SingleAsync()).OwnerAccountId=="first-claim-account","first account claim accepts verified unclaimed proof with a different purchase device token; existing owner protection is separate");
await db.Subscriptions.ExecuteDeleteAsync(); db.ChangeTracker.Clear();
await ent.UpsertAsync(Purchase("victim","victim-device"));
fake.Verified=Purchase("victim","victim-device"); fake.Verified.RevokedAt=now; fake.Canonical=Purchase("other","victim-device");
fake.Notification=JsonSerializer.SerializeToElement(new {notificationType="REFUND",signedDate=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),data=new {signedTransactionInfo="valid-refund-proof"}});
var notifications=new AppStoreNotificationService(fake,ent,TimeProvider.System,NullLogger<AppStoreNotificationService>.Instance);
Check(await notifications.HandleAsync(new AppStoreNotificationEnvelope("signed-refund"),default)==200 && (await ent.FindByOriginalTransactionAsync("victim"))!.RevokedAt==null && ent.ToEntitlement(await ent.FindByOriginalTransactionAsync("victim")).Status=="premium","REFUND acknowledged but original victim stays premium when canonical response selects different active purchase");
Console.WriteLine("7/7 audit assertions passed; no HTTP requests, no external database");
static SubscriptionEntity Purchase(string id,string device)=>new(){OriginalTransactionId=id,DeviceUuid=device,ProductId="monthly",ExpiresAt=DateTime.UtcNow.AddDays(5),WasEverPaid=true,Environment="Production"};
static void Check(bool condition,string text){if(!condition)throw new Exception(text); Console.WriteLine("PASS: "+text);}
sealed class RejectNetwork:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>throw new Exception("Network forbidden");}
sealed class Env:IWebHostEnvironment{public string EnvironmentName{get;set;}="Production";public string ApplicationName{get;set;}="audit";public string WebRootPath{get;set;}="";public string ContentRootPath{get;set;}="";public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
sealed class Fake:IAppStoreServerClient{
 public SubscriptionEntity Verified{get;set;}=null!;public SubscriptionEntity Canonical{get;set;}=null!;public JsonElement Notification{get;set;}
 public bool IsLocalVerifyEnabled=>false;public bool IsServerApiConfigured=>true;
 public AppStoreServerClient.VerifiedTransaction VerifyTransaction(string j,string? d)=>new(true,Verified,true,null);
 public Task<AppStoreServerClient.VerifiedTransaction> GetTransactionInfoAsync(string t,string? d=null,CancellationToken ct=default)=>Task.FromResult(new AppStoreServerClient.VerifiedTransaction(true,Verified,true,null));
 public Task<AppStoreServerClient.SubscriptionStatusesResult> GetAllSubscriptionStatusesAsync(string t,string? d=null,CancellationToken ct=default)=>Task.FromResult(new AppStoreServerClient.SubscriptionStatusesResult(true,Canonical,null));
 public Task<AppStoreServerClient.SubscriptionStatusesResult> RefreshSubscriptionAsync(string t,string? d=null,CancellationToken ct=default)=>GetAllSubscriptionStatusesAsync(t,d,ct);
 public AppleJws.DecodeResult DecodeNotification(string j)=>new(true,Notification,true,null); public void ApplyRenewalInfo(SubscriptionEntity s,string j){}
}
