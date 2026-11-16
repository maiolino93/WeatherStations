namespace WeatherStations.Api.Aggregation
{
    public static class StationNameNormalizer
    {
        public static string Normalize(string name)
        {
            
            var result = new char[name.Length];

            // True once the current word has had its first letter.
            bool wordHasLetter = false;

            for (int i = 0; i < name.Length; i++)
            {
                // Stations are grouped ignoring case, which compares names by their
                // uppercase form. Starting from that same form means every spelling
                // of a station produces the same output, whichever one arrived first.
                char upper = char.ToUpperInvariant(name[i]);

                if (char.IsWhiteSpace(upper))
                {
                    // Whitespace ends the word, so the next letter gets the capital.
                    wordHasLetter = false;
                    result[i] = upper;
                }
                else if (char.IsLetter(upper))
                {
                    // First letter of the word stays uppercase, the rest go lowercase.
                    result[i] = wordHasLetter ? char.ToLowerInvariant(upper) : upper;
                    wordHasLetter = true;
                }
                else
                {
                    // Digits and punctuation are copied as they are and do not start
                    // a new word. That is why the "s" in "John's" stays lowercase.
                    result[i] = upper;
                }
            }

            return new string(result);
        }
    }
}
