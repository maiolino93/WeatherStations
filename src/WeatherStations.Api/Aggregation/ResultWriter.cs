using System.Buffers;
using System.IO.Pipelines;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WeatherStations.Api.Aggregation
{
    public static class ResultWriter
    {
        private const int LinesPerFlush = 1024;

        // Writes one JSON line per station, sorted by name.
        public static async Task WriteAsync(
            IReadOnlyDictionary<string, StationStats> stations,
            PipeWriter output,
            CancellationToken cancellationToken)
        {
            int linesWritten = 0;

            foreach ((string name, StationStats stats) in SortByOutputName(stations))
            {
                WriteLine(output, name, stats);

                // Hand the response over in pieces, so a very large result
                // is not held in memory in full before any of it is sent.
                if (++linesWritten % LinesPerFlush == 0)
                {
                    await output.FlushAsync(cancellationToken);
                }
            }

            await output.FlushAsync(cancellationToken);
        }

        // Ordinal order compares character codes, so the result is the same
        // on every machine whatever its language settings are.
        private static SortedDictionary<string, StationStats> SortByOutputName(
            IReadOnlyDictionary<string, StationStats> stations)
        {
            var sorted = new SortedDictionary<string, StationStats>(StringComparer.Ordinal);

            foreach ((string name, StationStats stats) in stations)
            {
                string outputName = StationNameNormalizer.Normalize(name);

                // A few rare characters (the Kelvin sign and the letter K, for
                // example) are different stations while aggregating but end up
                // with the same output name. Join them so that every name
                // appears on exactly one line.
                sorted[outputName] = sorted.TryGetValue(outputName, out StationStats? existing)
                    ? StationStats.Combine(existing, stats)
                    : stats;
            }

            return sorted;
        }

        private static void WriteLine(IBufferWriter<byte> output, string name, StationStats stats)
        {
            // The default encoder escapes apostrophes and every non-ASCII
            // character, so "St. John's" would come out as "St. John\u0027s".
            // The relaxed one keeps them readable and is safe here because the
            // response is JSON, not HTML.
            JsonEncodedText encodedName = JsonEncodedText.Encode(name, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

            output.Write("{\"stationName\":\""u8);
            output.Write(encodedName.EncodedUtf8Bytes);
            output.Write("\",\"min\":"u8);
            WriteTenths(output, stats.MinTenths);
            output.Write(",\"mean\":"u8);
            WriteTenths(output, stats.MeanTenths);
            output.Write(",\"max\":"u8);
            WriteTenths(output, stats.MaxTenths);
            output.Write("}\n"u8);
        }

        // Always one fractional digit: 120 is written as 12.0 and -5 as -0.5.
        // A JSON number writer would print 12.0 as 12.
        private static void WriteTenths(IBufferWriter<byte> output, int tenths)
        {
            // The longest value is "-99.9".
            Span<byte> buffer = output.GetSpan(5);
            int length = 0;
            int magnitude = Math.Abs(tenths);

            if (tenths < 0)
            {
                buffer[length++] = (byte)'-';
            }

            if (magnitude >= 100)
            {
                buffer[length++] = (byte)('0' + magnitude / 100);
            }

            buffer[length++] = (byte)('0' + magnitude / 10 % 10);
            buffer[length++] = (byte)'.';
            buffer[length++] = (byte)('0' + magnitude % 10);

            output.Advance(length);
        }
    }
}