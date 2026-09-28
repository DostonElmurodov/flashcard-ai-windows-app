using Mavrylo.Dtos;
using Mavrylo.Filters;
using Mavrylo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mavrylo.Areas.OwlAI.Controllers;

[Area("OwlAI")]
[ApiController]
[Route("owlai/ai")]
// AI protection always runs before model binding so it can hash the raw body.
[ServiceFilter(typeof(AiProtectionFilter))]
[EnableRateLimiting("ai")]
// Authorization is enforced by AiProtectionFilter (device-JWT + App Attest). [AllowAnonymous]
// opts out of the framework-level FallbackPolicy so the filter's device scheme is authoritative.
[AllowAnonymous]
public class AiController(WordAiService ai) : ControllerBase
{
    [HttpPost("analyze-word")]
    [RequestSizeLimit(16_000)]
    public async Task<IActionResult> AnalyzeWord([FromBody] AnalyzeWordRequest req, CancellationToken ct)
        => Map(await ai.AnalyzeWordAsync(req, ct));

    [HttpPost("word-detail")]
    [RequestSizeLimit(16_000)]
    public async Task<IActionResult> WordDetail([FromBody] WordDetailRequest req, CancellationToken ct)
        => Map(await ai.WordDetailAsync(req, ct));

    [HttpPost("review-translation")]
    [RequestSizeLimit(16_000)]
    public async Task<IActionResult> ReviewTranslation([FromBody] ReviewTranslationRequest req, CancellationToken ct)
        => Map(await ai.ReviewTranslationAsync(req, ct));

    [HttpPost("extract-words")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ExtractWords([FromForm] IFormFile image, [FromForm] string? targetLanguage, [FromForm] string? nativeLanguage, CancellationToken ct)
    {
        if (image == null || image.Length == 0)
            return Map(await ai.ExtractWordsAsync(Array.Empty<byte>(), targetLanguage, nativeLanguage, ct));
        await using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct);
        return Map(await ai.ExtractWordsAsync(ms.ToArray(), targetLanguage, nativeLanguage, ct));
    }

    private IActionResult Map(WordAiService.AiResult r)
    {
        return r.Status switch
        {
            200 => Content(r.Json!, "application/json"),
            400 => BadRequest(r.Body),
            503 => StatusCode(503, r.Body),
            _ => StatusCode(r.Status, r.Body)
        };
    }
}
