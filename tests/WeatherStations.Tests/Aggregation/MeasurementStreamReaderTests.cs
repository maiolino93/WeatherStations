using System.IO.Pipelines;
using System.Text;
using WeatherStations.Api.Aggregation;

namespace WeatherStations.Tests.Aggregation
{
    public sealed class MeasurementStreamReaderTests
    {
        private const string Example =
            """
            {"stationName":"Hamburg","temperature":12.0}
            {"stationName":"Bulawayo","temperature":8.9}
            {"stationName":"Palembang","temperature":38.8}
            {"stationName":"Hamburg","temperature":34.2}
            {"stationName":"Bulawayo","temperature":35.2}
            {"stationName":"Palembang","temperature":41.0}

            """;

        private const string ExampleResult =
            """
            {"stationName":"Bulawayo","min":8.9,"mean":22.1,"max":35.2}
            {"stationName":"Hamburg","min":12.0,"mean":23.1,"max":34.2}
            {"stationName":"Palembang","min":38.8,"mean":39.9,"max":41.0}

            """;

        [Fact]
        public async Task Reads_every_line_of_the_body()
        {
            Outcome outcome = await ReadAsync(Lf(Example));

            Assert.Equal(6, outcome.Reader.LinesRead);
            Assert.Equal(6, outcome.Reader.AcceptedLines);
            Assert.Equal(0, outcome.Rejected.Count);
            Assert.Equal(Lf(ExampleResult), await outcome.ResultAsync());
        }

        // The network delivers a body in pieces of any size, and a piece can
        // end in the middle of a line, a number or even a single character.
        // Whatever the pieces are, the result has to be the same.
        [Theory]
        [InlineData(1, 16)]
        [InlineData(2, 16)]
        [InlineData(3, 32)]
        [InlineData(7, 64)]
        [InlineData(16, 16)]
        [InlineData(100, 128)]
        [InlineData(4096, 4096)]
        [InlineData(65536, 65536)]
        public async Task Result_does_not_depend_on_how_the_body_is_split(int bytesPerRead, int bufferSize)
        {
            var body = new StringBuilder();
            string[] names = ["Hamburg", "são paulo", "ST. JOHN'S", "st. john's", "Zürich", "\U0001F326 station"];
            for (int i = 0; i < 300; i++)
            {
                body.Append($$"""{"stationName":"{{names[i % names.Length]}}","temperature":{{(i % 199 - 99) / 10.0:F1}}}""").Append('\n');
            }

            Outcome whole = await ReadAsync(body.ToString());
            Outcome split = await ReadAsync(body.ToString(), bytesPerRead, bufferSize);

            Assert.Equal(300, split.Reader.AcceptedLines);
            Assert.Equal(0, split.Rejected.Count);
            Assert.Equal(await whole.ResultAsync(), await split.ResultAsync());
        }

        [Fact]
        public async Task Windows_line_endings_are_accepted()
        {
            Outcome outcome = await ReadAsync(Lf(Example).Replace("\n", "\r\n"), bytesPerRead: 5, bufferSize: 16);

            Assert.Equal(6, outcome.Reader.AcceptedLines);
            Assert.Equal(0, outcome.Rejected.Count);
            Assert.Equal(Lf(ExampleResult), await outcome.ResultAsync());
        }

        [Fact]
        public async Task Last_line_does_not_need_a_line_break()
        {
            Outcome outcome = await ReadAsync(Lf(Example).TrimEnd('\n'));

            Assert.Equal(6, outcome.Reader.AcceptedLines);
            Assert.Equal(Lf(ExampleResult), await outcome.ResultAsync());
        }

        [Fact]
        public async Task Byte_order_mark_at_the_start_is_ignored()
        {
            Outcome outcome = await ReadAsync("\uFEFF" + Lf(Example));

            Assert.Equal(6, outcome.Reader.AcceptedLines);
            Assert.Equal(0, outcome.Rejected.Count);
        }

        [Fact]
        public async Task Empty_body_has_no_lines()
        {
            Outcome outcome = await ReadAsync("");

            Assert.Equal(0, outcome.Reader.LinesRead);
            Assert.Equal(0, outcome.Reader.AcceptedLines);
            Assert.Equal(0, outcome.Rejected.Count);
            Assert.Equal("", await outcome.ResultAsync());
        }

        // Blank lines are neither accepted nor rejected, but they still count
        // as lines: the bad one below is reported as line 5, where it sits in the file.
        [Fact]
        public async Task Blank_lines_are_skipped_but_still_numbered()
        {
            string body =
                "\n" +
                """{"stationName":"Hamburg","temperature":12.0}""" + "\n" +
                "   \t\n" +
                "\r\n" +
                "not json\n" +
                """{"stationName":"Hamburg","temperature":34.2}""" + "\n" +
                "\n";

            Outcome outcome = await ReadAsync(body);

            Assert.Equal(7, outcome.Reader.LinesRead);
            Assert.Equal(2, outcome.Reader.AcceptedLines);
            RejectedLine rejected = Assert.Single(outcome.Rejected.Samples);
            Assert.Equal(5, rejected.LineNumber);
        }

        [Fact]
        public async Task Bad_lines_are_set_aside_and_the_rest_is_still_aggregated()
        {
            string body =
                """{"stationName":"Hamburg","temperature":12.0}""" + "\n" +
                """{"stationName":"Hamburg","temperature":"warm"}""" + "\n" +
                """{"stationName":"Hamburg","temperature":34.2}""" + "\n" +
                "garbage\n" +
                """{"temperature":1.0}""" + "\n";

            Outcome outcome = await ReadAsync(body, bytesPerRead: 3, bufferSize: 16);

            Assert.Equal(2, outcome.Reader.AcceptedLines);
            Assert.Equal(3, outcome.Rejected.Count);
            Assert.Equal([2, 4, 5], outcome.Rejected.Samples.Select(sample => sample.LineNumber));
            Assert.Contains("'temperature' must be a number", outcome.Rejected.Samples[0].Reason);
            Assert.Equal("The line is not valid JSON.", outcome.Rejected.Samples[1].Reason);
            Assert.Equal("'stationName' is missing.", outcome.Rejected.Samples[2].Reason);
            Assert.Equal("""{"stationName":"Hamburg","min":12.0,"mean":23.1,"max":34.2}""" + "\n", await outcome.ResultAsync());
        }

        [Theory]
        [InlineData(MeasurementStreamReader.MaxLineBytes, true)]
        [InlineData(MeasurementStreamReader.MaxLineBytes + 1, false)]
        public async Task Line_can_be_up_to_4096_bytes_long(int lineLength, bool accepted)
        {
            Outcome outcome = await ReadAsync(LineOfLength(lineLength) + "\n", bytesPerRead: 1000, bufferSize: 1024);

            Assert.Equal(accepted ? 1 : 0, outcome.Reader.AcceptedLines);
            Assert.Equal(accepted ? 0 : 1, outcome.Rejected.Count);
        }

        // Without the limit, a body that never sends a line break would make
        // the read buffer grow until memory runs out.
        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(4096)]
        [InlineData(100_000)]
        public async Task Overlong_line_is_dropped_and_reading_goes_on(int bytesPerRead)
        {
            string body =
                """{"stationName":"Hamburg","temperature":12.0}""" + "\n" +
                LineOfLength(50_000) + "\n" +
                "garbage\n" +
                """{"stationName":"Hamburg","temperature":34.2}""" + "\n";

            Outcome outcome = await ReadAsync(body, bytesPerRead, bufferSize: 4096);

            Assert.Equal(4, outcome.Reader.LinesRead);
            Assert.Equal(2, outcome.Reader.AcceptedLines);
            Assert.Equal([2, 3], outcome.Rejected.Samples.Select(sample => sample.LineNumber));
            Assert.Equal("The line is longer than 4096 bytes.", outcome.Rejected.Samples[0].Reason);
        }

        [Fact]
        public async Task Overlong_last_line_without_a_line_break_is_rejected()
        {
            string body = """{"stationName":"Hamburg","temperature":12.0}""" + "\n" + LineOfLength(10_000);

            Outcome outcome = await ReadAsync(body, bytesPerRead: 512, bufferSize: 512);

            Assert.Equal(2, outcome.Reader.LinesRead);
            Assert.Equal(1, outcome.Reader.AcceptedLines);
            Assert.Equal(2, Assert.Single(outcome.Rejected.Samples).LineNumber);
        }

        [Fact]
        public async Task Reading_stops_when_there_are_too_many_stations()
        {
            string body =
                """{"stationName":"a","temperature":1.0}""" + "\n" +
                """{"stationName":"b","temperature":1.0}""" + "\n" +
                """{"stationName":"A","temperature":1.0}""" + "\n" +
                """{"stationName":"c","temperature":1.0}""" + "\n" +
                """{"stationName":"d","temperature":1.0}""" + "\n";

            Outcome outcome = await ReadAsync(body, maxStations: 2);

            Assert.True(outcome.Reader.StationLimitReached);
            Assert.Equal(4, outcome.Reader.LinesRead);
            Assert.Equal(3, outcome.Reader.AcceptedLines);
        }

        [Fact]
        public async Task Rejected_line_is_saved_as_it_was_received()
        {
            string directory = Path.Combine(Path.GetTempPath(), "weather-stations-tests", Guid.NewGuid().ToString("N"));
            try
            {
                string filePath;
                using (var rejected = new RejectedLines(directory, maxSaved: 10))
                {
                    var reader = new MeasurementStreamReader(new StationAggregator(maxStations: 10), rejected);
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"stationName\":\"São Paulo\"}\r\n"));

                    await reader.ReadAsync(PipeReader.Create(stream), CancellationToken.None);
                    filePath = rejected.FilePath!;
                }

                Assert.Contains("\"content\":\"{\\\"stationName\\\":\\\"São Paulo\\\"}\"", File.ReadAllText(filePath));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task Reading_stops_when_the_request_is_cancelled()
        {
            var reader = new MeasurementStreamReader(new StationAggregator(maxStations: 10), new RejectedLines(directory: null, maxSaved: 0));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Example));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => reader.ReadAsync(PipeReader.Create(stream), new CancellationToken(canceled: true)));
        }

        private static string Lf(string text) => text.ReplaceLineEndings("\n");

        // A valid measurement padded with an ignored property to an exact size.
        private static string LineOfLength(int length)
        {
            const string start = """{"stationName":"Hamburg","temperature":1.0,"padding":" """;
            const string end = "\"}";

            return start + new string('x', length - start.Length - end.Length) + end;
        }

        private static async Task<Outcome> ReadAsync(string body, int bytesPerRead = int.MaxValue, int bufferSize = 4096, int maxStations = 100)
        {
            var aggregator = new StationAggregator(maxStations);
            var rejected = new RejectedLines(directory: null, maxSaved: 0);
            var reader = new MeasurementStreamReader(aggregator, rejected);

            using var stream = new SlowStream(Encoding.UTF8.GetBytes(body), bytesPerRead);
            PipeReader input = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: bufferSize, minimumReadSize: 1));

            await reader.ReadAsync(input, CancellationToken.None);
            await input.CompleteAsync();

            return new Outcome(reader, aggregator, rejected);
        }

        private sealed record Outcome(MeasurementStreamReader Reader, StationAggregator Aggregator, RejectedLines Rejected)
        {
            public async Task<string> ResultAsync()
            {
                using var stream = new MemoryStream();
                PipeWriter writer = PipeWriter.Create(stream);

                await ResultWriter.WriteAsync(Aggregator.Stations, writer, CancellationToken.None);
                await writer.CompleteAsync();

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        // Hands out the body a few bytes at a time, the way a slow connection would.
        private sealed class SlowStream(byte[] content, int bytesPerRead) : MemoryStream(content)
        {
            public override int Read(byte[] buffer, int offset, int count)
            {
                return base.Read(buffer, offset, Math.Min(count, bytesPerRead));
            }

            public override int Read(Span<byte> buffer)
            {
                return base.Read(buffer[..Math.Min(buffer.Length, bytesPerRead)]);
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return base.ReadAsync(buffer[..Math.Min(buffer.Length, bytesPerRead)], cancellationToken);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return base.ReadAsync(buffer, offset, Math.Min(count, bytesPerRead), cancellationToken);
            }
        }
    }
}