namespace WeatherStations.Api.Aggregation
{
    public sealed class AggregationOptions
    {
        public const string SectionName = "Aggregation";

        // A request with more distinct stations than this is refused. Without
        // a limit, a body made of unique names could use up all the memory.
        public int MaxStations { get; set; } = 1_000_000;

        // Folder for the files that keep the lines a request could not use.
        // A relative path starts at the application folder. Leave it empty to
        // turn the files off.
        public string RejectedLinesDirectory { get; set; } = "rejected-lines";

        // Lines saved per request. Later ones are still counted, so a body
        // full of bad lines cannot fill the disk.
        public int MaxRejectedLinesSaved { get; set; } = 1_000;
    }
}