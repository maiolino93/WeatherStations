using WeatherStations.Api.Aggregation;

namespace WeatherStations.Tests.Aggregation
{
    public sealed class StationNameNormalizerTests
    {

        [Theory]
        [InlineData("hamburg", "Hamburg")]
        [InlineData("HAMBURG", "Hamburg")]
        [InlineData("new york", "New York")]
        [InlineData("NEW YORK", "New York")]
        [InlineData("st. john's", "St. John's")]
        [InlineData("ST. JOHN'S", "St. John's")]
        [InlineData("rio de janeiro", "Rio De Janeiro")]
        public void Capitalises_the_first_letter_of_each_word(string input, string expected)
        {
            Assert.Equal(expected, StationNameNormalizer.Normalize(input));
        }

        [Theory]
        [InlineData("St. John's", "St. John's")]
        [InlineData("a", "A")]
        [InlineData("são paulo", "São Paulo")]
        [InlineData("SÃO PAULO", "São Paulo")]
        public void Already_normalised_single_letter_and_non_ascii_names(string input, string expected)
        {
            Assert.Equal(expected, StationNameNormalizer.Normalize(input));
        }

        // Only whitespace separates words. That is what keeps the "s" in "John's"
        // lowercase, and the price is that a hyphen does not start a new word either.
        [Theory]
        [InlineData("winston-salem", "Winston-salem")]
        [InlineData("o'brien", "O'brien")]
        public void Punctuation_does_not_start_a_new_word(string input, string expected)
        {
            Assert.Equal(expected, StationNameNormalizer.Normalize(input));
        }

        [Fact]
        public void Spaces_between_words_are_kept_as_received()
        {
            Assert.Equal("New  York", StationNameNormalizer.Normalize("new  york"));
        }

        // "First letter of each word" read literally: the digit is skipped
        // and the first actual letter gets the capital.
        [Fact]
        public void Word_starting_with_a_digit_capitalises_its_first_letter()
        {
            Assert.Equal("3Rd Avenue", StationNameNormalizer.Normalize("3rd avenue"));
        }

        // Both spellings land in the same aggregation bucket, so they must also
        // come out identical. The cost: a Greek final sigma becomes a regular one.
        [Fact]
        public void Spellings_that_aggregate_together_normalise_identically()
        {
            Assert.Equal("πάτρας", "ΠΆΤΡΑΣ", StringComparer.OrdinalIgnoreCase);
            Assert.Equal("Πάτρασ", StationNameNormalizer.Normalize("πάτρας"));
            Assert.Equal("Πάτρασ", StationNameNormalizer.Normalize("ΠΆΤΡΑΣ"));
        }
    }
}
