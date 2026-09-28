using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mavrylo.Areas.OwlAI.Controllers;

[Area("OwlAI")]
[ApiController]
[Route("owlai/legal")]
[AllowAnonymous]
public class LegalController : ControllerBase
{
    // Keep this in sync whenever the legal text below changes materially.
    private const string LastUpdated = "June 6, 2026";

    [HttpGet("terms")]
    [Produces("text/html")]
    public ContentResult Terms() => Content(BuildPage(
        "Terms of Service",
        @"<p>These Terms of Service govern your use of the Owl AI iOS app (the &ldquo;App&rdquo;).
           By installing or using the App you agree to these terms.</p>

          <h2>1. The service</h2>
          <p>Owl AI helps you translate words and phrases, save them as flash cards,
             review them, and schedule study reminders. Translations are produced with the help
             of third-party AI providers.</p>

          <h2>2. No account required</h2>
          <p>The App does not require you to create an account, sign in, or provide an email address.
             Your device is registered automatically using Apple App Attest so the App can talk to
             our servers securely. There is no username or password to manage.</p>

          <h2>3. Subscriptions and free use</h2>
          <p>The App offers a free tier with a limited number of AI-translated words, and an
             optional <strong>Owl AI Premium</strong> subscription (monthly or yearly) that unlocks
             unlimited AI translations and adding words.</p>
          <p>Subscriptions are sold and billed by Apple through your App Store account. Where offered,
             a free trial converts to a paid subscription unless you cancel at least 24 hours before
             the trial ends. Subscriptions renew automatically until cancelled. You can manage or
             cancel a subscription in your Apple ID settings. Refunds are handled by Apple under the
             App Store policy.</p>

          <h2>4. Acceptable use</h2>
          <p>You agree to use the App for personal vocabulary learning and not to abuse, overload,
             reverse engineer, or attempt to bypass the security or usage limits of the service.</p>

          <h2>5. AI-generated content</h2>
          <p>Translations and examples are generated automatically and may contain errors. They are
             provided for learning purposes and should not be relied upon as professional translation.</p>

          <h2>6. Disclaimer and liability</h2>
          <p>The App is provided &ldquo;as is&rdquo; without warranties of any kind. To the extent
             permitted by law, we are not liable for any indirect or consequential damages arising
             from your use of the App.</p>

          <h2>7. Changes</h2>
          <p>We may update these terms from time to time. Continued use of the App after an update
             means you accept the revised terms.</p>

          <h2>8. Contact</h2>
          <p>Questions about these terms: <a href=""mailto:support@mavrylo.com"">support@mavrylo.com</a></p>"
    ), "text/html");

    [HttpGet("privacy")]
    [Produces("text/html")]
    public ContentResult Privacy() => Content(BuildPage(
        "Privacy Policy",
        @"<p>This Privacy Policy explains how the Owl AI iOS app (the &ldquo;App&rdquo;) handles your data.
           Owl AI is built to work <strong>without an account</strong>: we do not ask for your name,
           email address, phone number, or any social login, and we do not use Sign in with Apple,
           Google, or Facebook.</p>

          <h2>How your device is identified</h2>
          <p>To protect our servers and your usage limits without a login, the App registers your
             device using Apple App Attest. This creates an anonymous, device-scoped identifier and a
             cryptographic key for your device. This identifier is not linked to your Apple ID, your
             name, or any contact information, and it cannot be used to identify you personally.</p>

          <h2>What we store</h2>
          <ul>
            <li><strong>On your device:</strong> the words and flash cards you create, your language
                preferences, and your reminder settings.</li>
            <li><strong>On our servers (linked only to the anonymous device identifier):</strong>
                the App Attest public key for your device, the words and translations you save (so the
                App can enforce free limits and support future device sync), and your subscription
                status received from Apple.</li>
          </ul>

          <h2>What we do NOT collect</h2>
          <ul>
            <li>No name, email, phone number, or postal address.</li>
            <li>No social or third-party login identities.</li>
            <li>No payment card details &mdash; all billing is handled by Apple; we only receive your
                subscription status (active, trial, expired, etc.).</li>
            <li>No advertising identifiers and no third-party advertising or analytics trackers.</li>
          </ul>

          <h2>Third-party AI processing</h2>
          <p>The words and phrases you choose to translate are sent to our AI providers
             (OpenAI and/or Google Gemini) to generate translations, examples, and pronunciation.
             Only the text you are translating and the chosen languages are sent &mdash; not your
             device identifier or any contact information.</p>

          <h2>Apple subscriptions</h2>
          <p>If you subscribe, Apple processes your purchase. We use Apple&rsquo;s App Store Server API
             to verify and refresh your subscription status. We do not receive your payment details
             from Apple.</p>

          <h2>Data retention and deletion</h2>
          <p>Deleting the App removes the data stored on your device. Because the App has no account,
             server-side data is tied only to your anonymous device identifier. To request deletion of
             the data associated with your device, contact us at the address below.</p>

          <h2>Children</h2>
          <p>Owl AI is not directed to children under 13, and we do not knowingly collect personal
             information from children.</p>

          <h2>Changes</h2>
          <p>We may update this policy from time to time. Material changes will be reflected on this
             page with a new &ldquo;last updated&rdquo; date.</p>

          <h2>Contact</h2>
          <p>Privacy questions: <a href=""mailto:privacy@mavrylo.com"">privacy@mavrylo.com</a></p>"
    ), "text/html");

    private static string BuildPage(string title, string bodyHtml) => $@"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<title>{title} — Owl AI</title>
<style>
  body {{ font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", sans-serif;
          max-width: 720px; margin: 2rem auto; padding: 0 1.25rem; line-height: 1.55; color: #1c1c1e; }}
  h1 {{ font-size: 1.75rem; margin-bottom: 0.25rem; }}
  h2 {{ font-size: 1.15rem; margin-top: 1.5rem; }}
  a {{ color: #342b99; }}
  .meta {{ color: #8a8a8e; font-size: 0.875rem; margin-bottom: 1.5rem; }}
</style>
</head>
<body>
  <h1>{title}</h1>
  <p class=""meta"">Owl AI &middot; Last updated {LastUpdated}</p>
  {bodyHtml}
</body>
</html>";
}
