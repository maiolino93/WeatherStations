using System.Text;
using WeatherStations.Api.Aggregation;

namespace WeatherStations.Tests.Aggregation
{
    public sealed class StationAggregatorTests
    {
        [Fact]
        public void Spellings_that_differ_only_in_case_are_one_station()
        {
            var aggregator = new StationAggregator(maxStations: 10);

            aggregator.TryAdd("st. john's", 100);
            aggregator.TryAdd("ST. JOHN'S", 200);
            aggregator.TryAdd("St. John's", 300);

            StationStats stats = Assert.Single(aggregator.Stations).Value;
            Assert.Equal(3, stats.Count);
            Assert.Equal(100, stats.MinTenths);
            Assert.Equal(200, stats.MeanTenths);
            Assert.Equal(300, stats.MaxTenths);
        }

        // The whole path a line takes before the result is written: parsed,
        // trimmed, grouped, and finally given its output spelling.
        [Fact]
        public void Lines_for_the_same_station_end_up_under_one_normalised_name()
        {
            string[] lines =
            [
                """{"stationName":"st. john's","temperature":10.0}""",
                """{"stationName":"ST. JOHN'S","temperature":20.0}""",
                """{"stationName":"St. John's","temperature":30.0}""",
                """{"stationName":"  st. JOHN'S","temperature":40.0}""",
                """{"stationName":"st. JOHN'S ","temperature":50.0}""",
            ];
            var aggregator = new StationAggregator(maxStations: 10);
            Span<char> name = stackalloc char[MeasurementParser.MaxStationNameBytes];

            foreach (string line in lines)
            {
                Assert.True(MeasurementParser.TryParse(Encoding.UTF8.GetBytes(line), name, out int length, out int tenths, out _));
                Assert.True(aggregator.TryAdd(name[..length], tenths));
            }

            KeyValuePair<string, StationStats> station = Assert.Single(aggregator.Stations);
            Assert.Equal("St. John's", StationNameNormalizer.Normalize(station.Key));
            Assert.Equal(5, station.Value.Count);
            Assert.Equal(300, station.Value.MeanTenths);
        }

        [Fact]
        public void Different_names_are_different_stations()
        {
            var aggregator = new StationAggregator(maxStations: 10);

            aggregator.TryAdd("Hamburg", 120);
            aggregator.TryAdd("Bulawayo", 89);
            aggregator.TryAdd("new york", 10);
            aggregator.TryAdd("new  york", 20);
            aggregator.TryAdd("hamburg", 342);

            Assert.Equal(4, aggregator.Stations.Count);
            Assert.Equal(2, aggregator.Stations["HAMBURG"].Count);
        }

        // Without a limit, a body made of unique names would grow the
        // dictionary until the process runs out of memory.
        [Fact]
        public void New_station_beyond_the_limit_is_refused()
        {
            var aggregator = new StationAggregator(maxStations: 2);

            Assert.True(aggregator.TryAdd("a", 1));
            Assert.True(aggregator.TryAdd("b", 2));
            Assert.False(aggregator.TryAdd("c", 3));

            Assert.Equal(2, aggregator.Stations.Count);
        }

        [Fact]
        public void Known_stations_are_still_updated_at_the_limit()
        {
            var aggregator = new StationAggregator(maxStations: 1);

            Assert.True(aggregator.TryAdd("a", 1));
            Assert.True(aggregator.TryAdd("A", 3));

            Assert.Equal(2, aggregator.Stations["a"].Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Limit_must_be_positive(int maxStations)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new StationAggregator(maxStations));
        }

        // The point of the span lookup: once a station exists, further lines
        // for it create no garbage, however its name is spelled.
        [Fact]
        public void Measurements_for_a_known_station_allocate_nothing()
        {
            byte[] line = Encoding.UTF8.GetBytes("""{"stationName":"ST. JOHN'S","temperature":-12.3}""");
            var aggregator = new StationAggregator(maxStations: 10);
            aggregator.TryAdd("st. john's", 0);
            Span<char> name = stackalloc char[MeasurementParser.MaxStationNameBytes];

            // The first calls can allocate while the runtime compiles the code.
            for (int i = 0; i < 1_000; i++)
            {
                MeasurementParser.TryParse(line, name, out int length, out int tenths, out _);
                aggregator.TryAdd(name[..length], tenths);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100_000; i++)
            {
                MeasurementParser.TryParse(line, name, out int length, out int tenths, out _);
                aggregator.TryAdd(name[..length], tenths);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(101_001, aggregator.Stations["st. john's"].Count);
        }
    }
}