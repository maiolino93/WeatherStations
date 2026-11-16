using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace WeatherStations.Client
{
    public sealed record ClientOptions(long Count, Uri Url, string ApiKey, int Stations, int Seed)
    {
        public const string Usage =
            """
            Usage: WeatherStations.Client [options]

              --count <n>      Measurements to generate (default 1000)
              --url <url>      Address of the service (default http://localhost:5080)
              --api-key <key>  API key (default: the WEATHER_API_KEY variable, then dev-only-key)
              --stations <n>   Number of different stations (default 400)
              --seed <n>       Seed that makes a run repeatable (default: random)
            """;

        public static bool TryParse(
            string[] args,
            [NotNullWhen(true)] out ClientOptions? options,
            [NotNullWhen(false)] out string? error)
        {
            options = null;

            long count = 1000;
            var url = new Uri("http://localhost:5080");
            string apiKey = Environment.GetEnvironmentVariable("WEATHER_API_KEY") ?? "dev-only-key";
            int stations = 400;

            // Picked here rather than left to Random, so it can be printed
            // and the same run repeated later with --seed.
            int seed = Random.Shared.Next();

            for (int i = 0; i < args.Length; i += 2)
            {
                string name = args[i];
                if (i + 1 >= args.Length)
                {
                    error = $"Missing value for {name}.";
                    return false;
                }

                string value = args[i + 1];
                bool valid;

                switch (name)
                {
                    case "--count":
                        valid = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count);
                        break;
                    case "--url":
                        valid = Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed) && parsed.Scheme is "http" or "https";
                        url = parsed ?? url;
                        break;
                    case "--api-key":
                        apiKey = value;
                        valid = value.Length > 0;
                        break;
                    case "--stations":
                        valid = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out stations) && stations > 0;
                        break;
                    case "--seed":
                        valid = int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out seed);
                        break;
                    default:
                        valid = false;
                        break;
                }

                if (!valid)
                {
                    error = $"Unknown option or invalid value: {name} {value}";
                    return false;
                }
            }

            options = new ClientOptions(count, url, apiKey, stations, seed);
            error = null;
            return true;
        }
    }
}