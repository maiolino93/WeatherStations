using System.Net;
using System.Text;
using System.Text.Json;
using WeatherStations.Tests.Authentication;

namespace WeatherStations.Tests.Aggregation
{
    public sealed class MeasurementsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

        private readonly HttpClient _client = factory.CreateClient();

        [Fact]
        public async Task Returns_min_mean_and_max_per_station_sorted_by_name()
        {
            using HttpResponseMessage response = await PostAsync(Lf(Example));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Lf(ExampleResult), await response.Content.ReadAsStringAsync());
            Assert.Equal("6", Header(response, "X-Accepted-Lines"));
            Assert.Equal("0", Header(response, "X-Rejected-Lines"));
            Assert.False(response.Headers.Contains("X-Rejected-Line-Numbers"));
        }

        [Fact]
        public async Task Spellings_of_one_station_come_back_as_a_single_line()
        {
            string body =
                """
                {"stationName":"st. john's","temperature":10.0}
                {"stationName":"ST. JOHN'S","temperature":20.0}
                {"stationName":"St. John's","temperature":30.0}
                {"stationName":"  st. JOHN'S","temperature":40.0}
                {"stationName":"st. JOHN'S ","temperature":50.0}

                """;

            using HttpResponseMessage response = await PostAsync(Lf(body));

            Assert.Equal("""{"stationName":"St. John's","min":10.0,"mean":30.0,"max":50.0}""" + "\n", await response.Content.ReadAsStringAsync());
        }

        // Files saved on Windows, files with a byte order mark, files without
        // a final line break and files with blank lines all have to work.
        [Fact]
        public async Task File_format_quirks_do_not_change_the_result()
        {
            string body = "\uFEFF" + Lf(Example).Replace("\n", "\r\n\r\n").TrimEnd();

            using HttpResponseMessage response = await PostAsync(body);

            Assert.Equal(Lf(ExampleResult), await response.Content.ReadAsStringAsync());
            Assert.Equal("6", Header(response, "X-Accepted-Lines"));
            Assert.Equal("0", Header(response, "X-Rejected-Lines"));
        }

        [Fact]
        public async Task Body_sent_in_small_pieces_gives_the_same_result()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/measurements")
            {
                Content = new TrickleContent(Encoding.UTF8.GetBytes(Lf(Example)), bytesPerWrite: 7),
            };
            request.Headers.Add("X-Api-Key", ApiFactory.ApiKey);

            using HttpResponseMessage response = await _client.SendAsync(request);

            Assert.Equal(Lf(ExampleResult), await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Empty_body_is_a_success_with_nothing_in_it()
        {
            using HttpResponseMessage response = await PostAsync("");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("", await response.Content.ReadAsStringAsync());
            Assert.Equal("0", Header(response, "X-Accepted-Lines"));
            Assert.Equal("0", Header(response, "X-Rejected-Lines"));
        }

        [Fact]
        public async Task Bad_line_is_left_out_reported_and_saved()
        {
            string marker = Guid.NewGuid().ToString("N");
            string body =
                """{"stationName":"Hamburg","temperature":12.0}""" + "\n" +
                """{"stationName":"Hamburg","temperature":34.2}""" + "\n" +
                $"not json {marker}\n" +
                """{"stationName":"Bulawayo","temperature":8.9}""" + "\n";

            using HttpResponseMessage response = await PostAsync(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                """
                {"stationName":"Bulawayo","min":8.9,"mean":8.9,"max":8.9}
                {"stationName":"Hamburg","min":12.0,"mean":23.1,"max":34.2}

                """.ReplaceLineEndings("\n"),
                await response.Content.ReadAsStringAsync());
            Assert.Equal("3", Header(response, "X-Accepted-Lines"));
            Assert.Equal("1", Header(response, "X-Rejected-Lines"));
            Assert.Equal("3", Header(response, "X-Rejected-Line-Numbers"));

            string saved = Assert.Single(
                Directory.GetFiles(factory.RejectedLinesDirectory).Select(File.ReadAllText),
                content => content.Contains(marker));
            using JsonDocument record = JsonDocument.Parse(saved);
            Assert.Equal(3, record.RootElement.GetProperty("line").GetInt64());
            Assert.Equal("The line is not valid JSON.", record.RootElement.GetProperty("reason").GetString());
            Assert.Equal($"not json {marker}", record.RootElement.GetProperty("content").GetString());
        }

        [Fact]
        public async Task Only_the_first_ten_rejected_line_numbers_are_listed()
        {
            var body = new StringBuilder();
            body.Append("""{"stationName":"Hamburg","temperature":12.0}""").Append('\n');
            for (int i = 0; i < 15; i++)
            {
                body.Append("oops\n");
            }

            using HttpResponseMessage response = await PostAsync(body.ToString());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("15", Header(response, "X-Rejected-Lines"));
            Assert.Equal("2,3,4,5,6,7,8,9,10,11", Header(response, "X-Rejected-Line-Numbers"));
        }

        // Sending the wrong file should not look like a success with no data.
        [Fact]
        public async Task Body_without_a_single_valid_line_is_a_bad_request()
        {
            using HttpResponseMessage response = await PostAsync("station;temperature\nHamburg;12.0\nBulawayo;8.9\n");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("0", Header(response, "X-Accepted-Lines"));
            Assert.Equal("3", Header(response, "X-Rejected-Lines"));

            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("No valid measurements", problem.RootElement.GetProperty("title").GetString());
            Assert.Contains("Line 1: The line is not valid JSON.", problem.RootElement.GetProperty("detail").GetString());
        }

        [Fact]
        public async Task More_stations_than_the_limit_is_a_bad_request()
        {
            HttpClient client = factory
                .WithWebHostBuilder(builder => builder.UseSetting("Aggregation:MaxStations", "2"))
                .CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/measurements")
            {
                Content = new StringContent(Lf(Example)),
            };
            request.Headers.Add("X-Api-Key", ApiFactory.ApiKey);

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Too many stations", problem.RootElement.GetProperty("title").GetString());
            Assert.Contains("more than 2 different stations", problem.RootElement.GetProperty("detail").GetString());
        }

        [Fact]
        public async Task Body_is_not_read_without_the_key()
        {
            using var content = new StringContent(Lf(Example));

            using HttpResponseMessage response = await _client.PostAsync("/measurements", content);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains("X-Accepted-Lines"));
        }

        private static string Lf(string text) => text.ReplaceLineEndings("\n");

        private static string? Header(HttpResponseMessage response, string name)
        {
            return response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : null;
        }

        private async Task<HttpResponseMessage> PostAsync(string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/measurements")
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)),
            };
            request.Headers.Add("X-Api-Key", ApiFactory.ApiKey);

            return await _client.SendAsync(request);
        }

        // Writes the body a few bytes at a time and flushes after each piece,
        // so the request is streamed instead of being sent in one go.
        private sealed class TrickleContent(byte[] body, int bytesPerWrite) : HttpContent
        {
            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                for (int offset = 0; offset < body.Length; offset += bytesPerWrite)
                {
                    int count = Math.Min(bytesPerWrite, body.Length - offset);
                    await stream.WriteAsync(body.AsMemory(offset, count));
                    await stream.FlushAsync();
                }
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }
    }
}