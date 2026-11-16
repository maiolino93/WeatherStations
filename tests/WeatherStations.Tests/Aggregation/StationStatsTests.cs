using WeatherStations.Api.Aggregation;

namespace WeatherStations.Tests.Aggregation
{
    public sealed class StationStatsTests
    {
        [Fact]
        public void Single_measurement_is_its_own_min_mean_and_max()
        {
            var stats = new StationStats(-123);

            Assert.Equal(-123, stats.MinTenths);
            Assert.Equal(-123, stats.MeanTenths);
            Assert.Equal(-123, stats.MaxTenths);
            Assert.Equal(1, stats.Count);
        }

        [Fact]
        public void Tracks_min_and_max_across_measurements()
        {
            StationStats stats = StatsOf(120, -5, 342, 0, -999, 999, 17);

            Assert.Equal(-999, stats.MinTenths);
            Assert.Equal(999, stats.MaxTenths);
            Assert.Equal(7, stats.Count);
        }

        [Fact]
        public void Mean_that_is_exact_needs_no_rounding()
        {
            Assert.Equal(200, StatsOf(100, 200, 300).MeanTenths);
            Assert.Equal(231, StatsOf(120, 342).MeanTenths);
        }

        // 8.9 and 35.2 average 22.05. Default .NET rounding would give 22.0,
        // because it rounds halves to the nearest even digit.
        [Theory]
        [InlineData(89, 352, 221)]
        [InlineData(-89, -352, -221)]
        [InlineData(2, 3, 3)]
        [InlineData(-2, -3, -3)]
        [InlineData(0, 1, 1)]
        [InlineData(0, -1, -1)]
        public void Mean_exactly_halfway_rounds_away_from_zero(int first, int second, int expectedMean)
        {
            Assert.Equal(expectedMean, StatsOf(first, second).MeanTenths);
        }

        [Theory]
        [InlineData(new[] { 1, 1, 2 }, 1)]
        [InlineData(new[] { 1, 2, 2 }, 2)]
        [InlineData(new[] { -1, -1, -2 }, -1)]
        [InlineData(new[] { -1, -2, -2 }, -2)]
        [InlineData(new[] { 999, -999 }, 0)]
        public void Mean_rounds_to_the_nearest_tenth(int[] tenths, int expectedMean)
        {
            Assert.Equal(expectedMean, StatsOf(tenths).MeanTenths);
        }

        // Three million readings of 99.9 already add up to more than an int can hold.
        [Fact]
        public void Sum_keeps_growing_past_the_int_range()
        {
            var stats = new StationStats(999);
            for (int i = 1; i < 3_000_000; i++)
            {
                stats.Add(999);
            }

            Assert.Equal(2_997_000_000L, stats.SumTenths);
            Assert.Equal(999, stats.MeanTenths);
        }

        private static StationStats StatsOf(params int[] tenths)
        {
            var stats = new StationStats(tenths[0]);
            foreach (int value in tenths.AsSpan(1))
            {
                stats.Add(value);
            }

            return stats;
        }
    }
}