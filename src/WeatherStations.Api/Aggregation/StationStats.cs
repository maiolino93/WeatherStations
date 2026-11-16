namespace WeatherStations.Api.Aggregation
{
    // Running totals for one station. Everything is in tenths of a degree,
    // so no floating point is involved until the result is written out.
    public sealed class StationStats
    {
        public StationStats(int firstTenths)
        {
            MinTenths = firstTenths;
            MaxTenths = firstTenths;
            SumTenths = firstTenths;
            Count = 1;
        }

        private StationStats(int minTenths, int maxTenths, long sumTenths, long count)
        {
            MinTenths = minTenths;
            MaxTenths = maxTenths;
            SumTenths = sumTenths;
            Count = count;
        }

        // Two sets of totals can be joined without going back to the
        // measurements, which is also what would let this work be split
        // across threads or machines.
        public static StationStats Combine(StationStats first, StationStats second)
        {
            return new StationStats(
                Math.Min(first.MinTenths, second.MinTenths),
                Math.Max(first.MaxTenths, second.MaxTenths),
                first.SumTenths + second.SumTenths,
                first.Count + second.Count);
        }

        public int MinTenths { get; private set; }

        public int MaxTenths { get; private set; }

        public long SumTenths { get; private set; }

        public long Count { get; private set; }

        public void Add(int tenths)
        {
            if (tenths < MinTenths)
            {
                MinTenths = tenths;
            }

            if (tenths > MaxTenths)
            {
                MaxTenths = tenths;
            }

            SumTenths += tenths;
            Count++;
        }

        // Mean rounded to the nearest tenth, with exact halves rounded away
        // from zero: 22.05 becomes 22.1 and -22.05 becomes -22.1.
        public int MeanTenths
        {
            get
            {
                long magnitude = Math.Abs(SumTenths);
                long rounded = (2 * magnitude + Count) / (2 * Count);
                return (int)(SumTenths < 0 ? -rounded : rounded);
            }
        }
    }
}