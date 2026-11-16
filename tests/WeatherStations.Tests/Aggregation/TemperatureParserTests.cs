using System.Text;
using WeatherStations.Api.Aggregation;

namespace WeatherStations.Tests.Aggregation
{
    public sealed class TemperatureParserTests
    {
        [Theory]
        [InlineData("12.0", 120)]
        [InlineData("1.2", 12)]
        [InlineData("-0.5", -5)]
        [InlineData("0.0", 0)]
        [InlineData("-0.0", 0)]
        [InlineData("99.9", 999)]
        [InlineData("-99.9", -999)]
        public void Reads_the_usual_shape_as_tenths(string text, int expectedTenths)
        {
            Assert.True(TemperatureParser.TryParse(Encoding.UTF8.GetBytes(text), out int tenths));
            Assert.Equal(expectedTenths, tenths);
        }

        // Same values written the way other JSON serialisers might write them.
        [Theory]
        [InlineData("12", 120)]
        [InlineData("-7", -70)]
        [InlineData("12.00", 120)]
        [InlineData("1.2e1", 120)]
        [InlineData("125E-1", 125)]
        [InlineData("0", 0)]
        public void Accepts_other_json_spellings_of_a_valid_value(string text, int expectedTenths)
        {
            Assert.True(TemperatureParser.TryParse(Encoding.UTF8.GetBytes(text), out int tenths));
            Assert.Equal(expectedTenths, tenths);
        }

        [Theory]
        [InlineData("100.0")]
        [InlineData("-100.0")]
        [InlineData("100")]
        [InlineData("1e400")]
        public void Rejects_values_outside_the_range(string text)
        {
            Assert.False(TemperatureParser.TryParse(Encoding.UTF8.GetBytes(text), out _));
        }

        [Theory]
        [InlineData("12.34")]
        [InlineData("0.05")]
        [InlineData("-99.99")]
        public void Rejects_more_than_one_fractional_digit(string text)
        {
            Assert.False(TemperatureParser.TryParse(Encoding.UTF8.GetBytes(text), out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("-")]
        [InlineData("abc")]
        [InlineData("null")]
        [InlineData("\"12.0\"")]
        [InlineData("1.x")]
        [InlineData("12.0 ")]
        public void Rejects_anything_that_is_not_a_number(string text)
        {
            Assert.False(TemperatureParser.TryParse(Encoding.UTF8.GetBytes(text), out _));
        }
    }
}
