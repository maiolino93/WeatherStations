using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WeatherStations.Tests.Authentication
{

    // Boots the real API in memory with its own key, so the tests never
    // depend on whatever happens to be in appsettings.Development.json.
    public sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public const string ApiKey = "test-only-key";

        // A folder of its own, so the files one test run leaves behind are
        // never mixed up with another's and can be removed at the end.
        public string RejectedLinesDirectory { get; } =
            Path.Combine(Path.GetTempPath(), "weather-stations-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Authentication:ApiKey", ApiKey);
            builder.UseSetting("Aggregation:RejectedLinesDirectory", RejectedLinesDirectory);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing && Directory.Exists(RejectedLinesDirectory))
            {
                Directory.Delete(RejectedLinesDirectory, recursive: true);
            }
        }
    }
}