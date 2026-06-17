using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AuraScan.Server.Infrastructure;

public class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";
}

public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private readonly IConfiguration _config;

    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration config)
        : base(options, logger, encoder)
    {
        _config = config;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Health and Swagger endpoints are always accessible
        var path = Request.Path.Value ?? "";
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue(ApiKeyAuthOptions.HeaderName, out var providedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing API key header"));
        }

        var configuredKey = _config["Security:ApiKey"];
        if (string.IsNullOrEmpty(configuredKey))
        {
            Logger.LogWarning("Security:ApiKey not configured — denying all requests");
            return Task.FromResult(AuthenticateResult.Fail("Server API key not configured"));
        }

        if (!string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        // Extract workstation identity from optional header
        var workstationId = Request.Headers["X-Workstation-Id"].FirstOrDefault() ?? "Unknown";

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "ApiKeyClient"),
            new Claim("WorkstationId", workstationId),
            new Claim(ClaimTypes.Role, "Workstation")
        };

        var identity = new ClaimsIdentity(claims, ApiKeyAuthOptions.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthOptions.SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
