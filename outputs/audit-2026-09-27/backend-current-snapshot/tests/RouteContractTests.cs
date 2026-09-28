using System.Net;
using System.Text;
using Mavrylo.Tests.TestSupport;
using Xunit;

namespace Mavrylo.Tests;

public class RouteContractTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>
{
    [Fact]
    public async Task OwlAiLegalRoutes_Work_AndOldPublicLegalRoutesAreGone()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var current = await client.GetAsync("/owlai/legal/privacy");
        using var old = await client.GetAsync("/public/legal/privacy");

        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
    }

    [Fact]
    public async Task OwlAiAppAttestBootstrapRoute_Works_AndOldFlashCardRouteIsGone()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var current = await client.PostAsync(
            "/owlai/app-attest/bootstrap-challenge",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        using var old = await client.PostAsync(
            "/flashcardapp/device/app-attest/bootstrap-challenge",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
    }

    [Fact]
    public async Task OwlAiAppStoreNotificationRoute_IsMoved_AndOldNotificationRoutesAreGone()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var current = await client.PostAsync(
            "/owlai/app-store-notifications/notifications",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        using var oldOwlAiIap = await client.PostAsync(
            "/owlai/iap/notifications",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        using var oldFlashCard = await client.PostAsync(
            "/flashcardapp/iap/notifications",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, current.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldOwlAiIap.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldFlashCard.StatusCode);
    }

    [Fact]
    public async Task PublicFlashcardSet_OldAndMisspelledNamespacesAreGone()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        using var oldPublic = await client.PostAsync(
            "/public-flashcard-sets/catalog",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        using var oldFlashCard = await client.PostAsync(
            "/flashcardapp/public-flashcard-sets/catalog",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        using var misspelled = await client.PostAsync(
            "/owlai/public-flashcards-sets/catalog",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, oldPublic.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldFlashCard.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, misspelled.StatusCode);
    }
}
