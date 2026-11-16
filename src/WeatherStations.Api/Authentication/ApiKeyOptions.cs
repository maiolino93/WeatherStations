namespace WeatherStations.Api.Authentication
{
    public sealed class ApiKeyOptions
    {
        public const string SectionName = "Authentication";
        public string ApiKey { get; set; } = string.Empty;
    }
}
