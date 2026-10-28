using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using TUnit.Core.Interfaces;

namespace HouseholdApp.E2ETests.Infrastructure;

public sealed class FakeOidcFixture : IAsyncInitializer, IAsyncDisposable
{
    private WebApplication? _app;

    public string Url { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var port = GetFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";

        var rsa = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(rsa) { KeyId = "fake-key-1" };
        var codes = new ConcurrentDictionary<string, (FakeOidcUser User, string? Nonce)>();
        var accessTokens = new ConcurrentDictionary<string, FakeOidcUser>();

        var webBuilder = WebApplication.CreateSlimBuilder();
        webBuilder.WebHost.UseUrls(baseUrl);
        webBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = webBuilder.Build();

        app.MapGet("/.well-known/openid-configuration", (HttpContext ctx) =>
        {
            var url = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            return Results.Json(new
            {
                issuer = url,
                authorization_endpoint = $"{url}/authorize",
                token_endpoint = $"{url}/token",
                jwks_uri = $"{url}/.well-known/jwks",
                userinfo_endpoint = $"{url}/userinfo",
                response_types_supported = new[] { "code" },
                subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" },
                scopes_supported = new[] { "openid", "profile", "email", "groups" },
                token_endpoint_auth_methods_supported = new[] { "client_secret_post", "client_secret_basic" },
                code_challenge_methods_supported = new[] { "S256" },
                claims_supported = new[] { "sub", "email", "name", "groups" },
                grant_types_supported = new[] { "authorization_code" },
            });
        });

        app.MapGet("/.well-known/jwks", () =>
        {
            var rsaParams = rsa.ExportParameters(false);
            return Results.Json(new
            {
                keys = new[]
                {
                    new
                    {
                        kty = "RSA",
                        use = "sig",
                        kid = signingKey.KeyId,
                        alg = "RS256",
                        n = Base64UrlEncoder.Encode(rsaParams.Modulus!),
                        e = Base64UrlEncoder.Encode(rsaParams.Exponent!),
                    }
                }
            });
        });

        app.MapGet("/authorize", (HttpContext ctx) =>
        {
            var redirectUri = ctx.Request.Query["redirect_uri"].ToString();
            var state = ctx.Request.Query["state"].ToString();
            var nonce = ctx.Request.Query["nonce"].ToString();
            var responseMode = ctx.Request.Query["response_mode"].ToString();
            var code = Guid.NewGuid().ToString("N");
            var selected = ctx.Request.Cookies[FakeOidcUser.CookieName];
            var user = string.IsNullOrEmpty(selected) ? FakeOidcUser.Owner : FakeOidcUser.Find(selected);
            if (user is null)
                return Results.BadRequest($"Unknown {FakeOidcUser.CookieName} '{selected}'");
            codes[code] = (user, string.IsNullOrEmpty(nonce) ? null : nonce);

            // ASP.NET Core OIDC defaults to response_mode=form_post for security.
            // The /signin-oidc callback only handles HTTP POST, so we must return
            // an auto-submitting HTML form rather than a GET redirect.
            if (responseMode == "form_post")
            {
                var html = $"""
                    <html><body>
                    <form id="f" method="post" action="{System.Web.HttpUtility.HtmlAttributeEncode(redirectUri)}">
                        <input type="hidden" name="code" value="{System.Web.HttpUtility.HtmlEncode(code)}" />
                        <input type="hidden" name="state" value="{System.Web.HttpUtility.HtmlEncode(state)}" />
                    </form>
                    <script>document.getElementById('f').submit();</script>
                    </body></html>
                    """;
                return Results.Content(html, "text/html");
            }

            return Results.Redirect($"{redirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}");
        });

        app.MapPost("/token", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var code = form["code"].ToString();
            if (!codes.TryRemove(code, out var grant))
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            var (user, nonce) = grant;

            var issuer = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var now = DateTime.UtcNow;
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Subject),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new(JwtRegisteredClaimNames.Name, user.Name),
                new("preferred_username", user.Subject),
                new(JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64),
            };
            if (!string.IsNullOrEmpty(nonce))
                claims.Add(new Claim(JwtRegisteredClaimNames.Nonce, nonce));

            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: "household-app",
                claims: claims,
                notBefore: now,
                expires: now.AddHours(1),
                signingCredentials: credentials);
            var idToken = new JwtSecurityTokenHandler().WriteToken(token);
            accessTokens[idToken] = user;

            return Results.Json(new
            {
                access_token = idToken,
                id_token = idToken,
                token_type = "Bearer",
                expires_in = 3600,
                scope = "openid profile email groups",
            });
        });

        app.MapGet("/userinfo", (HttpContext ctx) =>
        {
            var header = ctx.Request.Headers.Authorization.ToString();
            const string bearer = "Bearer ";
            if (!header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
                || !accessTokens.TryGetValue(header[bearer.Length..].Trim(), out var user))
                return Results.Unauthorized();

            return Results.Json(new
            {
                sub = user.Subject,
                email = user.Email,
                name = user.Name,
                groups = user.Groups,
            });
        });

        await app.StartAsync();
        _app = app;
        Url = baseUrl;
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}

/// <summary>A user the fake OIDC server can sign in, chosen per browser by the <see cref="CookieName"/> cookie.</summary>
public sealed record FakeOidcUser(string Subject, string Email, string Name, string[] Groups)
{
    public const string CookieName = "fake_oidc_user";

    public static readonly FakeOidcUser Owner =
        new("test-user", "test@household.local", "Test User", ["household-admins"]);

    public static readonly FakeOidcUser Partner =
        new("partner-user", "partner@household.local", "Partner User", []);

    public static FakeOidcUser? Find(string subject) =>
        new[] { Owner, Partner }.FirstOrDefault(u => u.Subject == subject);
}
