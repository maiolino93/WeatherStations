using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using WeatherStations.Api.Aggregation;

namespace WeatherStations.Tests.Aggregation
{
    public sealed class ResultWriterTests
    {
        [Fact]
        public async Task Writes_one_sorted_line_per_station()
        {
            var aggregator = new StationAggregator(maxStations: 10);
            aggregator.TryAdd("Hamburg", 120);
            aggregator.TryAdd("Bulawayo", 89);
            aggregator.TryAdd("Palembang", 388);
            aggregator.TryAdd("Hamburg", 342);
            aggregator.TryAdd("Bulawayo", 352);
            aggregator.TryAdd("Palembang", 410);

            string output = await WriteAsync(aggregator);

            Assert.Equal(
                """
                {"stationName":"Bulawayo","min":8.9,"mean":22.1,"max":35.2}
                {"stationName":"Hamburg","min":12.0,"mean":23.1,"max":34.2}
                {"stationName":"Palembang","min":38.8,"mean":39.9,"max":41.0}

                """.ReplaceLineEndings("\n"),
                output);
        }

        [Fact]
        public async Task No_stations_gives_an_empty_result()
        {
            Assert.Equal("", await WriteAsync(new StationAggregator(maxStations: 10)));
        }

        // The usual trap is the trailing zero: a JSON number writer prints 12.0 as 12.
        [Theory]
        [InlineData(120, "12.0")]
        [InlineData(100, "10.0")]
        [InlineData(5, "0.5")]
        [InlineData(0, "0.0")]
        [InlineData(-5, "-0.5")]
        [InlineData(-100, "-10.0")]
        [InlineData(999, "99.9")]
        [InlineData(-999, "-99.9")]
        public async Task Temperatures_always_have_one_fractional_digit(int tenths, string expected)
        {
            var aggregator = new StationAggregator(maxStations: 10);
            aggregator.TryAdd("a", tenths);

            string output = await WriteAsync(aggregator);

            Assert.Equal($$"""{"stationName":"A","min":{{expected}},"mean":{{expected}},"max":{{expected}}}""" + "\n", output);
        }

        [Theory]
        [InlineData("st. john's", "St. John's")]
        [InlineData("são paulo", "São Paulo")]
        [InlineData("zürich <west> & co", "Zürich <West> & Co")]
        public async Task Names_are_written_as_readable_text(string name, string expected)
        {
            var aggregator = new StationAggregator(maxStations: 10);
            aggregator.TryAdd(name, 10);

            string output = await WriteAsync(aggregator);

            Assert.StartsWith($$"""{"stationName":"{{expected}}",""", output);
        }

        // Quotes, backslashes and control characters have to be escaped or the
        // line stops being JSON. Reading it back is the simplest proof.
        [Theory]
        [InlineData("say \"hi\"", "Say \"Hi\"")]
        [InlineData("back\\slash", "Back\\slash")]
        [InlineData("tab\there", "Tab\tHere")]
        [InlineData("\U0001F326 station", "\U0001F326 Station")]
        public async Task Names_with_special_characters_survive_a_round_trip(string name, string expected)
        {
            var aggregator = new StationAggregator(maxStations: 10);
            aggregator.TryAdd(name, 10);

            string output = await WriteAsync(aggregator);

            using JsonDocument line = JsonDocument.Parse(output);
            Assert.Equal(expected, line.RootElement.GetProperty("stationName").GetString());
        }

        // Sorting is by character code. Every ASCII letter comes before any
        // accented one, so "Århus" is listed after "Zürich".
        [Fact]
        public async Task Order_is_by_character_code()
        {
            var aggregator = new StationAggregator(maxStations: 10);
            foreach (string name in new[] { "zürich", "århus", "berlin", "Aachen", "st. john's", "new york" })
            {
                aggregator.TryAdd(name, 10);
            }

            string output = await WriteAsync(aggregator);

            Assert.Equal(
                ["Aachen", "Berlin", "New York", "St. John's", "Zürich", "Århus"],
                StationNames(output));
        }

        // U+212A is the Kelvin sign. It looks like K and lowercases to k, but
        // it is a different character, so it starts out as a separate station.
        [Fact]
        public async Task Stations_that_share_an_output_name_are_joined()
        {
            var aggregator = new StationAggregator(maxStations: 10);
            aggregator.TryAdd("aK", 100);
            aggregator.TryAdd("a\u212A", 300);
            Assert.Equal(2, aggregator.Stations.Count);

            string output = await WriteAsync(aggregator);

            Assert.Equal("""{"stationName":"Ak","min":10.0,"mean":20.0,"max":30.0}""" + "\n", output);
        }

        [Fact]
        public async Task Writing_twice_gives_the_same_result()
        {
            var aggregator = new StationAggregator(maxStations: 10);
            aggregator.TryAdd("aK", 100);
            aggregator.TryAdd("a\u212A", 300);

            Assert.Equal(await WriteAsync(aggregator), await WriteAsync(aggregator));
        }

        [Fact]
        public async Task Large_result_is_written_completely()
        {
            const int stationCount = 5_000;
            var aggregator = new StationAggregator(maxStations: stationCount);
            for (int i = 0; i < stationCount; i++)
            {
                aggregator.TryAdd($"station {i:D4}", i % 1000);
            }

            string output = await WriteAsync(aggregator);

            string[] names = StationNames(output);
            Assert.Equal(stationCount, names.Length);
            Assert.Equal("Station 0000", names[0]);
            Assert.Equal("Station 4999", names[^1]);
            Assert.EndsWith("}\n", output);
        }

        private static async Task<string> WriteAsync(StationAggregator aggregator)
        {
            using var stream = new MemoryStream();
            PipeWriter writer = PipeWriter.Create(stream);

            await ResultWriter.WriteAsync(aggregator.Stations, writer, CancellationToken.None);
            await writer.CompleteAsync();

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string[] StationNames(string output)
        {
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    return document.RootElement.GetProperty("stationName").GetString()!;
                })
                .ToArray();
        }
    }
}