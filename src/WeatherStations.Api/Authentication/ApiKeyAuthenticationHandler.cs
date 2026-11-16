using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace WeatherStations.Api.Authentication
{
    public sealed class ApiKeyAuthenticationHandler(
                        IOptionsMonitor<AuthenticationSchemeOptions> options,
                        ILoggerFactory logger,
                        UrlEncoder encoder,
                        IOptions<ApiKeyOptions> apiKeyOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {

        public const string SchemeName = "ApiKey";
        public const string HeaderName = "X-Api-Key";


        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!Request.Headers.TryGetValue(HeaderName, out var header) || header.Count != 1)
                return Task.FromResult(AuthenticateResult.NoResult());

            byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(header.ToString()));
            byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKeyOptions.Value.ApiKey));


            if (!CryptographicOperations.FixedTimeEquals(providedHash, expectedHash))
                return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(Scheme.Name));
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers.WWWAuthenticate = Scheme.Name;

            return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: $"A valid API key is required in the {HeaderName} header.")
                .ExecuteAsync(Context);
        }

    }

}
