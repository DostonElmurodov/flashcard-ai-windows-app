using System.Data.Common;
using Mavrylo.Data;
using Mavrylo.Dtos;
using Mavrylo.Models;
using Mavrylo.Services;
using Mavrylo.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mavrylo.Tests;

public class PublicFlashcardSetConcurrencyTests(PostgresContainerFixture postgres)
    : IClassFixture<PostgresContainerFixture>
{
    [Fact]
    public async Task ConcurrentFirstPublish_RecoversUniqueRace_AndLeavesOnePendingRow()
    {
        var services = new ServiceCollection();
        var updateBarrier = new FirstUpdateBarrierInterceptor();
        services.AddLogging();
        services.AddSingleton(updateBarrier);
        services.AddSingleton<TimeProvider>(
            new ManualTimeProvider(DateTimeOffset.Parse("2026-08-03T12:00:00Z")));
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(postgres.ConnectionString)
            .AddInterceptors(provider.GetRequiredService<FirstUpdateBarrierInterceptor>()));
        services.AddScoped<PublicFlashcardSetService>();
        await using var provider = services.BuildServiceProvider();
        await EnsureSchemaAndSeedDeviceAsync(provider);

        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<PublicFlashcardSetService>();
        var second = secondScope.ServiceProvider.GetRequiredService<PublicFlashcardSetService>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstPublish = Task.Run(async () =>
        {
            await gate.Task;
            return await first.UpsertAsync("public-race-owner", Request("First"), default);
        });
        var secondPublish = Task.Run(async () =>
        {
            await gate.Task;
            return await second.UpsertAsync("public-race-owner", Request("Second"), default);
        });
        gate.SetResult();

        var results = await Task.WhenAll(firstPublish, secondPublish);

        Assert.All(results, result => Assert.Equal(PublicFlashcardSetStatus.Pending, result.Status));
        Assert.Equal(2, updateBarrier.ZeroFirstUpdates);
        await using var verifyScope = provider.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await verify.PublicFlashcardSets.SingleAsync(x =>
            x.OwnerDeviceId == "public-race-owner" && x.ClientSetId == "cat-race-1");
        Assert.Equal(PublicFlashcardSetStatus.Pending, row.Status);
    }

    private static async Task EnsureSchemaAndSeedDeviceAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await db.PublicFlashcardSets
            .Where(x => x.OwnerDeviceId == "public-race-owner")
            .ExecuteDeleteAsync();
        if (!await db.Devices.AnyAsync(x => x.Id == "public-race-owner"))
        {
            db.Devices.Add(new DeviceEntity
            {
                Id = "public-race-owner",
                KeyId = "public-race-key",
                DeviceUuid = "public-race-uuid"
            });
            await db.SaveChangesAsync();
        }
    }

    private static PublicFlashcardSetUpsertRequest Request(string title) => new(
        ClientSetId: "cat-race-1",
        Title: title,
        Description: null,
        Cards:
        [
            new PublicFlashcardSetCardDto(
                ClientCardId: "word-1",
                Word: "Ticket",
                Translations: ["Billete"],
                Pronunciation: null,
                PartOfSpeech: null,
                Examples: [],
                ExampleTranslations: [],
                Notes: null,
                NativeLanguage: "en-us",
                LearningLanguage: "es")
        ]);

    private sealed class FirstUpdateBarrierInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothFirstUpdates =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _zeroFirstUpdates;

        public int ZeroFirstUpdates => _zeroFirstUpdates;

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (result == 0
                && command.CommandText.Contains(
                    "UPDATE public_flashcard_sets",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref _zeroFirstUpdates) == 2)
                    _bothFirstUpdates.TrySetResult();
                await _bothFirstUpdates.Task.WaitAsync(
                    TimeSpan.FromSeconds(10), cancellationToken);
            }

            return result;
        }
    }
}
