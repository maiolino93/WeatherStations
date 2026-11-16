namespace WeatherStations.Api.Aggregation
{
    // Holds one entry per station, so memory grows with the number of
    // stations and not with the number of measurements.
    public sealed class StationAggregator
    {
        private readonly Dictionary<string, StationStats> _stations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, StationStats>.AlternateLookup<ReadOnlySpan<char>> _lookup;
        private readonly int _maxStations;

        public StationAggregator(int maxStations)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStations);

            _maxStations = maxStations;

            // Lets the dictionary be searched with the characters in the read
            // buffer directly. A string is created only for a station that has
            // not been seen before, never for each line.
            _lookup = _stations.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        public IReadOnlyDictionary<string, StationStats> Stations => _stations;

        // Returns false when the measurement belongs to a new station and the
        // limit has been reached. Known stations keep being updated.
        public bool TryAdd(ReadOnlySpan<char> stationName, int temperatureTenths)
        {
            if (_lookup.TryGetValue(stationName, out StationStats? stats))
            {
                stats.Add(temperatureTenths);
                return true;
            }

            if (_stations.Count >= _maxStations)
            {
                return false;
            }

            _lookup.TryAdd(stationName, new StationStats(temperatureTenths));
            return true;
        }
    }
}