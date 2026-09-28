using Mavrylo.Areas.OwlAI.Controllers;
using Mavrylo.Dtos;
using Mavrylo.Mapping;
using Mavrylo.Models;
using Xunit;

namespace Mavrylo.Tests;

public class MappingAndLegalTests
{
    [Fact]
    public void ParseStringArray_ReturnsEmptyForInvalidJson()
    {
        Assert.Empty(EntityMappers.ParseStringArray("not json"));
        Assert.Equal(["a", "b"], EntityMappers.ParseStringArray("""["a","b"]"""));
    }

    [Fact]
    public void ApplyWordUpsert_ChangesOnlyProvidedFields()
    {
        var word = new WordEntity { Word = "old", Translation = "old", ReviewInterval = 1 };
        EntityMappers.ApplyUpsert(word, new WordUpsert("new", null, null, null, ["ex"], null, null, null, null, null, null, 3, null, null, true, null));

        Assert.Equal("new", word.Word);
        Assert.Equal("old", word.Translation);
        Assert.Equal(3, word.ReviewInterval);
        Assert.True(word.IsMastered);
        Assert.Equal(["ex"], EntityMappers.ParseStringArray(word.ExamplesJson));
    }

    [Fact]
    public void LegalPages_ReturnHtmlWithExpectedContact()
    {
        var controller = new LegalController();

        var terms = controller.Terms();
        var privacy = controller.Privacy();

        Assert.Equal("text/html", terms.ContentType);
        Assert.Contains("support@mavrylo.com", terms.Content);
        Assert.Contains("privacy@mavrylo.com", privacy.Content);
        Assert.Contains("Owl AI", privacy.Content);
    }
}
