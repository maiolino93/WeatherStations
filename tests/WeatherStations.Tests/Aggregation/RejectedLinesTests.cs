using System.Text.Json;
using WeatherStations.Api.Aggregation;

namespace WeatherStations.Tests.Aggregation
{
    public sealed class RejectedLinesTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "weather-stations-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public void Clean_request_leaves_no_file_behind()
        {
            using (var rejected = new RejectedLines(_directory, maxSaved: 10))
            {
                Assert.Equal(0, rejected.Count);
                Assert.Empty(rejected.Samples);
                Assert.Null(rejected.FilePath);
            }

            Assert.False(Directory.Exists(_directory));
        }

        [Fact]
        public void Each_rejected_line_is_saved_with_its_number_reason_and_content()
        {
            string filePath;
            using (var rejected = new RejectedLines(_directory, maxSaved: 10))
            {
                rejected.Add(3, "The line is not valid JSON.", "hello"u8);
                rejected.Add(7, "'temperature' is missing.", """{"stationName":"São Paulo"}"""u8);
                filePath = rejected.FilePath!;
            }

            string[] saved = File.ReadAllLines(filePath);

            Assert.Equal(2, saved.Length);
            AssertRecord(saved[0], 3, "The line is not valid JSON.", "hello");
            AssertRecord(saved[1], 7, "'temperature' is missing.", """{"stationName":"São Paulo"}""");
        }

        // Counting never stops, but only the first few lines are kept as
        // examples for the response and the log.
        [Fact]
        public void Every_line_is_counted_but_only_the_first_ten_are_kept_as_samples()
        {
            using var rejected = new RejectedLines(directory: null, maxSaved: 0);

            for (int line = 1; line <= 25; line++)
            {
                rejected.Add(line, "bad", "x"u8);
            }

            Assert.Equal(25, rejected.Count);
            Assert.Equal(RejectedLines.MaxSamples, rejected.Samples.Count);
            Assert.Equal(1, rejected.Samples[0].LineNumber);
            Assert.Equal(10, rejected.Samples[^1].LineNumber);
        }

        // A body made of bad lines must not be able to fill the disk.
        [Fact]
        public void File_stops_growing_at_the_limit_while_the_count_goes_on()
        {
            string filePath;
            using (var rejected = new RejectedLines(_directory, maxSaved: 2))
            {
                for (int line = 1; line <= 5; line++)
                {
                    rejected.Add(line, "bad", "x"u8);
                }

                Assert.Equal(5, rejected.Count);
                filePath = rejected.FilePath!;
            }

            Assert.Equal(2, File.ReadAllLines(filePath).Length);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void No_directory_means_no_file(string? directory)
        {
            using var rejected = new RejectedLines(directory, maxSaved: 10);

            rejected.Add(1, "bad", "x"u8);

            Assert.Equal(1, rejected.Count);
            Assert.Null(rejected.FilePath);
        }

        [Fact]
        public void Content_that_is_not_valid_text_is_still_saved()
        {
            string filePath;
            using (var rejected = new RejectedLines(_directory, maxSaved: 10))
            {
                rejected.Add(1, "bad", [(byte)'a', 0xFF, (byte)'b']);
                filePath = rejected.FilePath!;
            }

            using JsonDocument record = JsonDocument.Parse(File.ReadAllLines(filePath)[0]);
            Assert.Equal("a\uFFFDb", record.RootElement.GetProperty("content").GetString());
        }

        [Fact]
        public void Each_request_writes_to_its_own_file()
        {
            using var first = new RejectedLines(_directory, maxSaved: 10);
            using var second = new RejectedLines(_directory, maxSaved: 10);

            first.Add(1, "bad", "x"u8);
            second.Add(1, "bad", "y"u8);

            Assert.NotEqual(first.FilePath, second.FilePath);
            Assert.Equal(2, Directory.GetFiles(_directory).Length);
        }

        private static void AssertRecord(string json, long line, string reason, string content)
        {
            using JsonDocument record = JsonDocument.Parse(json);

            Assert.Equal(line, record.RootElement.GetProperty("line").GetInt64());
            Assert.Equal(reason, record.RootElement.GetProperty("reason").GetString());
            Assert.Equal(content, record.RootElement.GetProperty("content").GetString());
        }
    }
}