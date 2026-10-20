using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using KekeBeauty.Modules.Identity.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using OtpNet;

namespace KekeBeauty.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed partial class AuthFlowTests(ApiFixture api)
{
    private static string Password => TestSecrets.Password;

    private static string NewEmail() => $"user{Guid.NewGuid():N}@example.com";

    private string TotpCode(string secret) => new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(api.Clock.GetUtcNow().UtcDateTime);

    private static async Task<T> Json<T>(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<T>())!;

    [GeneratedRegex(@"token=([^\s&]+)")]
    private static partial Regex TokenPattern();

    private string LatestToken(string email, string subjectFragment)
    {
        var message = api.Emails.Messages.Last(m => m.To == email && m.Subject.Contains(subjectFragment, StringComparison.OrdinalIgnoreCase));
        return Uri.UnescapeDataString(TokenPattern().Match(message.TextBody).Groups[1].Value);
    }

    private async Task RegisterAndConfirmAsync(HttpClient client, string email, string role = "Client")
    {
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, "Awa", "Koné", role, true));
        register.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var confirm = await client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequest(LatestToken(email, "Confirmez")));
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<ChallengeResponse> LoginAsync(HttpClient client, string email, string? password = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password ?? Password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await Json<ChallengeResponse>(response);
    }

    /// <summary>Enrols Google Authenticator and returns the TOTP secret plus the issued session.</summary>
    private async Task<(string Secret, AuthResponse Session)> EnrollAsync(HttpClient client, ChallengeResponse challenge)
    {
        challenge.Status.Should().Be(ChallengeStatus.TwoFactorSetupRequired);

        var setup = await client.PostAsJsonAsync("/api/v1/auth/2fa/setup", new ChallengeRequest(challenge.ChallengeToken));
        setup.StatusCode.Should().Be(HttpStatusCode.OK);
        var secret = (await Json<TwoFactorSetupResponse>(setup)).Secret;

        var enable = await client.PostAsJsonAsync("/api/v1/auth/2fa/enable", new ChallengeCodeRequest(challenge.ChallengeToken, TotpCode(secret)));
        enable.StatusCode.Should().Be(HttpStatusCode.OK);
        return (secret, await Json<AuthResponse>(enable));
    }

    private static HttpClient Authorized(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static string RefreshCookieOf(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("kb_rt=", StringComparison.Ordinal));
        return header.Split(';')[0]["kb_rt=".Length..];
    }

    [Fact]
    public async Task Login_is_refused_until_the_email_is_confirmed()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        (await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, Password, "Awa", "Koné", "Client", true)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));

        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await login.Content.ReadAsStringAsync()).Should().Contain("auth.email_not_confirmed");
    }

    [Fact]
    public async Task Weak_passwords_and_unknown_roles_are_rejected()
    {
        using var client = api.CreateClient();

        var weak = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(NewEmail(), "short1", "Awa", "Koné", "Client", true));
        var admin = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(NewEmail(), Password, "Awa", "Koné", "SuperAdmin", true));

        weak.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await weak.Content.ReadAsStringAsync()).Should().Contain("password");
        admin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Registering_an_existing_email_looks_identical_and_warns_the_owner()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAndConfirmAsync(client, email);

        var again = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "Another-Pass-77", "Eve", "Autre", "Client", true));

        again.StatusCode.Should().Be(HttpStatusCode.Accepted);
        api.Emails.Messages.Should().Contain(m => m.To == email && m.Subject.Contains("déjà un compte", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Password_alone_never_opens_a_session()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAndConfirmAsync(client, email);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));

        // A client needs no second factor, but the password alone still never yields a session: login returns a
        // short-lived challenge (no access token, no cookie); the session is opened by a separate /complete-login call.
        var body = await login.Content.ReadAsStringAsync();
        body.Should().Contain("PasswordOnly").And.NotContain("accessToken");
        login.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task A_client_signs_in_without_a_second_factor_but_a_partner_must_enrol_one()
    {
        using var client = api.CreateClient();
        var clientEmail = NewEmail();
        await RegisterAndConfirmAsync(client, clientEmail);

        var challenge = await LoginAsync(client, clientEmail);
        challenge.Status.Should().Be(ChallengeStatus.PasswordOnly);

        var complete = await client.PostAsJsonAsync("/api/v1/auth/complete-login", new ChallengeRequest(challenge.ChallengeToken));
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await Json<AuthResponse>(complete);
        session.User.TwoFactorEnabled.Should().BeFalse();
        complete.Headers.Contains("Set-Cookie").Should().BeTrue();

        // A partner cannot skip the second factor, and the password-only shortcut is refused for its challenge.
        using var partnerClient = api.CreateClient();
        var partnerEmail = NewEmail();
        await RegisterAndConfirmAsync(partnerClient, partnerEmail, "PartnerOwner");
        var partnerChallenge = await LoginAsync(partnerClient, partnerEmail);
        partnerChallenge.Status.Should().Be(ChallengeStatus.TwoFactorSetupRequired);

        var sneaky = await partnerClient.PostAsJsonAsync("/api/v1/auth/complete-login", new ChallengeRequest(partnerChallenge.ChallengeToken));
        sneaky.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Full_flow_enrol_2fa_sign_in_again_refresh_and_sign_out()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAndConfirmAsync(client, email, "PartnerOwner");

        // First sign-in: enrol Google Authenticator and receive recovery codes once.
        var (secret, enrolled) = await EnrollAsync(client, await LoginAsync(client, email));
        enrolled.RecoveryCodes.Should().HaveCount(10);
        enrolled.User.Roles.Should().Equal("PartnerOwner");
        enrolled.User.TwoFactorEnabled.Should().BeTrue();

        using (var authorized = Authorized(api.CreateClient(), enrolled.AccessToken))
        {
            var me = await authorized.GetAsync("/api/v1/me");
            me.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Json<UserDto>(me)).Email.Should().Be(email);
        }

        (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Second sign-in: a TOTP code is now required, and a code can only be used once.
        var challenge = await LoginAsync(client, email);
        challenge.Status.Should().Be(ChallengeStatus.TwoFactorRequired);

        var replay = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new ChallengeCodeRequest(challenge.ChallengeToken, TotpCode(secret)));
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        api.Clock.Advance(TimeSpan.FromSeconds(30));
        var verified = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new ChallengeCodeRequest(challenge.ChallengeToken, TotpCode(secret)));
        verified.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json<AuthResponse>(verified)).RecoveryCodes.Should().BeNull();

        // The refresh cookie rotates the session.
        var refreshed = await client.PostAsync("/api/v1/auth/refresh", null);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json<AuthResponse>(refreshed)).AccessToken.Should().NotBeNullOrEmpty();

        // After logout the refresh cookie is dead.
        (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAndConfirmAsync(client, email, "PartnerOwner");
        var (_, enrolled) = await EnrollAsync(client, await LoginAsync(client, email));
        var recovery = enrolled.RecoveryCodes![0];

        var first = await LoginAsync(client, email);
        (await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new ChallengeCodeRequest(first.ChallengeToken, recovery.ToLowerInvariant())))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await LoginAsync(client, email);
        (await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new ChallengeCodeRequest(second.ChallengeToken, recovery)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_a_while()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAndConfirmAsync(client, email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Wrong-Password-1")))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password)))
            .StatusCode.Should().Be(HttpStatusCode.Locked);

        api.Clock.Advance(TimeSpan.FromMinutes(16));
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Five_wrong_codes_burn_the_challenge()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAndConfirmAsync(client, email, "PartnerOwner");
        var (secret, _) = await EnrollAsync(client, await LoginAsync(client, email));

        api.Clock.Advance(TimeSpan.FromSeconds(30));
        var challenge = await LoginAsync(client, email);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new ChallengeCodeRequest(challenge.ChallengeToken, "000000"));
        }

        var late = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new ChallengeCodeRequest(challenge.ChallengeToken, TotpCode(secret)));
        late.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Password_reset_is_single_use_and_ends_existing_sessions()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAndConfirmAsync(client, email, "PartnerOwner");
        await EnrollAsync(client, await LoginAsync(client, email));

        (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(email))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("nobody@example.com"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var token = LatestToken(email, "Réinitialisation");

        const string newPassword = "Brand-New-Pass-99";
        (await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, newPassword))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "Another-One-123"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, newPassword))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_session_family()
    {
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = NewEmail();
        await RegisterAndConfirmAsync(client, email, "PartnerOwner");

        var challenge = await LoginAsync(client, email);
        var setup = await client.PostAsJsonAsync("/api/v1/auth/2fa/setup", new ChallengeRequest(challenge.ChallengeToken));
        var secret = (await Json<TwoFactorSetupResponse>(setup)).Secret;
        var enable = await client.PostAsJsonAsync("/api/v1/auth/2fa/enable", new ChallengeCodeRequest(challenge.ChallengeToken, TotpCode(secret)));
        var firstCookie = RefreshCookieOf(enable);

        HttpRequestMessage RefreshWith(string cookie)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            request.Headers.Add("Cookie", $"kb_rt={cookie}");
            return request;
        }

        var rotated = await client.SendAsync(RefreshWith(firstCookie));
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondCookie = RefreshCookieOf(rotated);

        (await client.SendAsync(RefreshWith(firstCookie))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.SendAsync(RefreshWith(secondCookie))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sessions_can_be_listed_and_revoked()
    {
        using var client = api.CreateClient();
        var email = NewEmail();
        await RegisterAndConfirmAsync(client, email, "PartnerOwner");
        var (_, enrolled) = await EnrollAsync(client, await LoginAsync(client, email));

        using var authorized = Authorized(api.CreateClient(), enrolled.AccessToken);
        var sessions = await Json<List<SessionDto>>(await authorized.GetAsync("/api/v1/me/sessions"));
        var current = sessions.Should().ContainSingle().Subject;
        current.IsCurrent.Should().BeTrue();

        (await authorized.DeleteAsync($"/api/v1/me/sessions/{current.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Malformed_json_is_a_client_error_not_a_server_error()
    {
        using var client = api.CreateClient();
        using var body = new StringContent("{bad json", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Protected_endpoints_require_a_bearer_token()
    {
        using var client = api.CreateClient();

        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Authorized(client, "not-a-jwt");
        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Google_sign_in_creates_a_confirmed_client_account_without_a_second_factor()
    {
        using var client = api.CreateClient();
        var email = NewEmail();

        var login = await client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequest(FakeGoogleVerifier.TokenFor(email, "Awa", "Koné")));

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var challenge = await Json<ChallengeResponse>(login);
        // A Google account is a plain client: no second factor, the session opens with the follow-up call.
        challenge.Status.Should().Be(ChallengeStatus.PasswordOnly);

        var complete = await client.PostAsJsonAsync("/api/v1/auth/complete-login", new ChallengeRequest(challenge.ChallengeToken));
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await Json<AuthResponse>(complete);
        session.User.Email.Should().Be(email);
        session.User.Roles.Should().Equal("Client");
        session.User.TwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Google_sign_in_rejects_invalid_or_unverified_tokens()
    {
        using var client = api.CreateClient();

        (await client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequest("garbage"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/auth/google", new GoogleLoginRequest(FakeGoogleVerifier.TokenFor(NewEmail(), "A", "B", verified: false))))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Providers_report_google_enabled_and_whatsapp_disabled()
    {
        using var client = api.CreateClient();

        var providers = await Json<ProvidersResponse>(await client.GetAsync("/api/v1/auth/providers"));

        providers.Password.Should().BeTrue();
        providers.Google.Enabled.Should().BeTrue();
        providers.Google.ClientId.Should().Be("test-google-client");
        providers.WhatsApp.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task The_seeded_super_admin_must_enrol_in_2fa_like_everyone_else()
    {
        using var client = api.CreateClient();

        var challenge = await LoginAsync(client, ApiFixture.AdminEmail, ApiFixture.AdminPassword);
        var (_, session) = await EnrollAsync(client, challenge);

        session.User.Roles.Should().Equal("SuperAdmin");
    }
}
