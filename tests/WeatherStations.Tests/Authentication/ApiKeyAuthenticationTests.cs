using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace WeatherStations.Tests.Authentication
{
    // These go through the whole pipeline on purpose. The failure we care about
    // is a request slipping through without a key, and only a real request shows that.
    public sealed class ApiKeyAuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
    {

        private const string HeaderName = "X-Api-Key";

        private readonly HttpClient _client = factory.CreateClient();

        [Fact]
        public async Task Request_without_key_is_rejected()
        {
            using var response = await _client.PostAsync("/measurements", content: null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }


        // "dev-only-key" is here deliberately: tests run as Development, where
        // appsettings sets that key. It has to lose to the one from ApiFactory.
        [Theory]
        [InlineData("wrong-key")]
        [InlineData("dev-only-key")]
        [InlineData("")]
        public async Task Request_with_wrong_key_is_rejected(string apiKey)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/measurements");
            request.Headers.Add(HeaderName, apiKey);

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Request_with_correct_key_is_accepted()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/measurements");
            request.Headers.Add(HeaderName, ApiFactory.ApiKey);

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Two X-Api-Key headers is ambiguous, so we refuse instead of guessing which one counts.
        [Fact]
        public async Task Request_with_key_sent_twice_is_rejected()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/measurements");
            request.Headers.Add(HeaderName, [ApiFactory.ApiKey, ApiFactory.ApiKey]);

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }


        // Without a key you should not be able to tell which routes exist.
        // "/favicon.ico" is the tricky one: the default catch-all skips file-like paths.
        [Theory]
        [InlineData("/unknown")]
        [InlineData("/favicon.ico")]
        public async Task Unknown_route_without_key_is_rejected_instead_of_not_found(string path)
        {
            using var response = await _client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Unknown_route_with_correct_key_is_not_found()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/unknown");
            request.Headers.Add(HeaderName, ApiFactory.ApiKey);

            using var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // A 401 is supposed to say how to authenticate , and the body
        // should be something a client can parse rather than an empty response.
        [Fact]
        public async Task Rejection_carries_a_challenge_and_problem_details()
        {
            using var response = await _client.PostAsync("/measurements", content: null);

            Assert.Equal("ApiKey", response.Headers.WwwAuthenticate.ToString());
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        // A missing key should stop the service at startup, not show up later as unexplained 401s.
        [Fact]
        public void Startup_fails_when_no_key_is_configured()
        {
            using var unconfigured = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder => builder.UseSetting("Authentication:ApiKey", ""));

            Assert.Throws<OptionsValidationException>(() => unconfigured.CreateClient());
        }
    }
}
