using System.Text;
using WeatherStations.Api.Aggregation;

namespace WeatherStations.Tests.Aggregation
{
    public sealed class MeasurementParserTests
    {
        [Fact]
        public void Reads_the_station_name_and_the_temperature()
        {
            Assert.True(TryParse("""{"stationName":"Hamburg","temperature":12.0}""", out string name, out int tenths, out string? error));

            Assert.Equal("Hamburg", name);
            Assert.Equal(120, tenths);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("""{"temperature":-0.5,"stationName":"Hamburg"}""")]
        [InlineData("""  { "stationName" : "Hamburg" , "temperature" : -0.5 }  """)]
        [InlineData("{\"stationName\":\"Hamburg\",\"temperature\":-0.5}\r")]
        [InlineData("""{"id":7,"stationName":"Hamburg","tags":["a",{"b":1}],"temperature":-0.5,"extra":null}""")]
        public void Property_order_whitespace_and_unknown_properties_do_not_matter(string line)
        {
            Assert.True(TryParse(line, out string name, out int tenths, out _));

            Assert.Equal("Hamburg", name);
            Assert.Equal(-5, tenths);
        }

        // Whitespace around a name is not part of it. Case and the spaces
        // between words are left for the aggregator and the normaliser.
        [Theory]
        [InlineData(" hamburg", "hamburg")]
        [InlineData("hamburg ", "hamburg")]
        [InlineData("  st. JOHN'S\\t", "st. JOHN'S")]
        [InlineData("new  york", "new  york")]
        public void Whitespace_around_the_name_is_dropped(string sent, string expectedName)
        {
            Assert.True(TryParse($$"""{"stationName":"{{sent}}","temperature":1.0}""", out string name, out _, out _));

            Assert.Equal(expectedName, name);
        }

        [Theory]
        [InlineData(" ")]
        [InlineData("   ")]
        [InlineData("\\t\\u00a0")]
        public void Name_with_nothing_but_whitespace_is_rejected(string sent)
        {
            Assert.False(TryParse($$"""{"stationName":"{{sent}}","temperature":1.0}""", out _, out _, out string? error));

            Assert.Contains("at least one character that is not whitespace", error);
        }

        // The 100 bytes are counted on the value as it was sent, before trimming.
        [Fact]
        public void Length_limit_counts_the_surrounding_whitespace()
        {
            string sent = new string('a', 100) + " ";

            Assert.False(TryParse($$"""{"stationName":"{{sent}}","temperature":1.0}""", out _, out _, out string? error));

            Assert.Contains("between 1 and 100 bytes", error);
        }

        [Theory]
        [InlineData("""{"stationName":"S\u00e3o Paulo","temperature":1.0}""", "São Paulo")]
        [InlineData("""{"stationName":"S\u00E3o \"Paulo\"","temperature":1.0}""", "São \"Paulo\"")]
        [InlineData("""{"stationName":"\ud83c\udf26","temperature":1.0}""", "\U0001F326")]
        public void Escaped_names_are_decoded(string line, string expectedName)
        {
            Assert.True(TryParse(line, out string name, out _, out _));

            Assert.Equal(expectedName, name);
        }

        // If a property appears twice, the last one wins. It is what System.Text.Json
        // does by default, so the service behaves like the rest of the platform.
        [Fact]
        public void Repeated_property_takes_the_last_value()
        {
            Assert.True(TryParse("""{"stationName":"A","temperature":1.0,"stationName":"B","temperature":2.0}""", out string name, out int tenths, out _));

            Assert.Equal("B", name);
            Assert.Equal(20, tenths);
        }

        // The limit is in bytes, not characters, so the same number of
        // characters can be fine in ASCII and too long in another script.
        [Theory]
        [InlineData("a", 100, true)]
        [InlineData("a", 101, false)]
        [InlineData("é", 50, true)]
        [InlineData("é", 51, false)]
        [InlineData("€", 33, true)]
        [InlineData("€", 34, false)]
        [InlineData("\\u0041", 100, true)]
        [InlineData("\\u0041", 101, false)]
        [InlineData("\\u00e9", 50, true)]
        [InlineData("\\u00e9", 51, false)]
        [InlineData("a", 5000, false)]
        public void Name_length_is_limited_to_100_utf8_bytes(string unit, int count, bool expected)
        {
            string name = string.Concat(Enumerable.Repeat(unit, count));

            bool parsed = TryParse($$"""{"stationName":"{{name}}","temperature":1.0}""", out _, out _, out string? error);

            Assert.Equal(expected, parsed);
            if (!expected)
            {
                Assert.Contains("between 1 and 100 bytes", error);
            }
        }

        [Fact]
        public void Empty_name_is_rejected()
        {
            Assert.False(TryParse("""{"stationName":"","temperature":1.0}""", out _, out _, out string? error));

            Assert.Contains("between 1 and 100 bytes", error);
        }

        [Theory]
        [InlineData("""{"temperature":1.0}""", "'stationName' is missing")]
        [InlineData("""{"stationName":"Hamburg"}""", "'temperature' is missing")]
        [InlineData("""{}""", "'stationName' is missing")]
        [InlineData("""{"StationName":"Hamburg","temperature":1.0}""", "'stationName' is missing")]
        public void Missing_property_is_rejected(string line, string expectedError)
        {
            Assert.False(TryParse(line, out _, out _, out string? error));

            Assert.Contains(expectedError, error);
        }

        [Theory]
        [InlineData("""{"stationName":12,"temperature":1.0}""", "'stationName' must be a string")]
        [InlineData("""{"stationName":null,"temperature":1.0}""", "'stationName' must be a string")]
        [InlineData("""{"stationName":"Hamburg","temperature":"12.0"}""", "'temperature' must be a number")]
        [InlineData("""{"stationName":"Hamburg","temperature":null}""", "'temperature' must be a number")]
        [InlineData("""{"stationName":"Hamburg","temperature":true}""", "'temperature' must be a number")]
        [InlineData("""{"stationName":"Hamburg","temperature":100.0}""", "'temperature' must be a number")]
        [InlineData("""{"stationName":"Hamburg","temperature":12.34}""", "'temperature' must be a number")]
        public void Wrong_type_or_value_is_rejected(string line, string expectedError)
        {
            Assert.False(TryParse(line, out _, out _, out string? error));

            Assert.Contains(expectedError, error);
        }

        [Theory]
        [InlineData("")]
        [InlineData("hello")]
        [InlineData("""{"stationName":"Hamburg","temperature":1.0""")]
        [InlineData("""{"stationName":"Hamburg","temperature":1.0,}""")]
        [InlineData("""{"stationName":"Hamburg","temperature":01.0}""")]
        [InlineData("""{"stationName":"Hamburg","temperature":+1.0}""")]
        [InlineData("""{'stationName':'Hamburg','temperature':1.0}""")]
        [InlineData("""{"stationName":"Hamburg","temperature":1.0} {"stationName":"Hamburg","temperature":1.0}""")]
        [InlineData("""{"stationName":"Hamburg","temperature":1.0}x""")]
        public void Malformed_json_is_rejected(string line)
        {
            Assert.False(TryParse(line, out _, out _, out string? error));

            Assert.Equal("The line is not valid JSON.", error);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("42")]
        [InlineData("\"Hamburg\"")]
        [InlineData("null")]
        public void Json_that_is_not_an_object_is_rejected(string line)
        {
            Assert.False(TryParse(line, out _, out _, out string? error));

            Assert.Equal("The line must be a JSON object.", error);
        }

        // Bad text in the name must come back as a normal validation error.
        // Left alone, the JSON reader throws here and the request would end in a 500.
        [Fact]
        public void Name_with_invalid_utf8_bytes_is_rejected()
        {
            byte[] line = [.. """{"stationName":"Ham"""u8, 0xFF, 0xFE, .. """burg","temperature":1.0}"""u8];
            Span<char> buffer = stackalloc char[MeasurementParser.MaxStationNameBytes];

            Assert.False(MeasurementParser.TryParse(line, buffer, out _, out _, out string? error));

            Assert.Equal("'stationName' is not valid Unicode text.", error);
        }

        [Theory]
        [InlineData("""{"stationName":"\ud800","temperature":1.0}""")]
        [InlineData("""{"stationName":"\udc00\ud800","temperature":1.0}""")]
        public void Name_with_half_a_surrogate_pair_is_rejected(string line)
        {
            Assert.False(TryParse(line, out _, out _, out string? error));

            Assert.Equal("'stationName' is not valid Unicode text.", error);
        }

        [Fact]
        public void Buffer_smaller_than_the_longest_name_is_a_programming_error()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                Span<char> tooSmall = stackalloc char[MeasurementParser.MaxStationNameBytes - 1];
                MeasurementParser.TryParse("{}"u8, tooSmall, out _, out _, out _);
            });
        }

        private static bool TryParse(string line, out string name, out int tenths, out string? error)
        {
            Span<char> buffer = stackalloc char[MeasurementParser.MaxStationNameBytes];

            bool parsed = MeasurementParser.TryParse(Encoding.UTF8.GetBytes(line), buffer, out int length, out tenths, out error);

            name = new string(buffer[..length]);
            return parsed;
        }
    }
}