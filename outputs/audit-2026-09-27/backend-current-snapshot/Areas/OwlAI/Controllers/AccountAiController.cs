using Mavrylo.Dtos;
using Mavrylo.Filters;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mavrylo.Areas.OwlAI.Controllers;

[ApiController, Route("owlai/account/ai")]
[Authorize(AuthenticationSchemes = AccountAuth.Scheme)]
[EnableRateLimiting("account-usage")]
[ServiceFilter(typeof(AccountAiProtectionFilter))]
public sealed class AccountAiController(WordAiService ai) : ControllerBase
{
    [HttpPost("analyze-word"), RequestSizeLimit(16000)]
    public async Task<IActionResult> AnalyzeWord(AnalyzeWordRequest request, CancellationToken ct) => Map(await ai.AnalyzeWordAsync(request, ct));
    [HttpPost("word-detail"), RequestSizeLimit(16000)]
    public async Task<IActionResult> WordDetail(WordDetailRequest request, CancellationToken ct) => Map(await ai.WordDetailAsync(request, ct));
    [HttpPost("review-translation"), RequestSizeLimit(16000)]
    public async Task<IActionResult> ReviewTranslation(ReviewTranslationRequest request, CancellationToken ct) => Map(await ai.ReviewTranslationAsync(request, ct));
    [HttpPost("extract-words"), RequestSizeLimit(20000000)]
    public async Task<IActionResult> ExtractWords([FromForm] IFormFile image, [FromForm] string? targetLanguage, [FromForm] string? nativeLanguage, CancellationToken ct)
    {
        await using var stream = new MemoryStream();
        if (image != null) await image.CopyToAsync(stream, ct);
        return Map(await ai.ExtractWordsAsync(stream.ToArray(), targetLanguage, nativeLanguage, ct));
    }
    private IActionResult Map(WordAiService.AiResult result) => result.Status == 200 ? Content(result.Json!, "application/json") : StatusCode(result.Status, result.Body);
}
