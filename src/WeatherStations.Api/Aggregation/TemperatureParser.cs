using System.Globalization;

namespace WeatherStations.Api.Aggregation
{
    public static class TemperatureParser
    {
        private const NumberStyles JsonNumber =
            NumberStyles.AllowLeadingSign | 
            NumberStyles.AllowDecimalPoint | 
            NumberStyles.AllowExponent;



        // Temperatures are handled as whole tenths of a degree (12.3 becomes 123),
        // so sums and comparisons are exact integer arithmetic.
        public static bool TryParse(ReadOnlySpan<byte> utf8Number, out int tenths)
        {
            ReadOnlySpan<byte> digits = utf8Number;
            bool isNegative = digits.Length > 0 && digits[0] == (byte)'-';

            if (isNegative)
                digits = digits[1..];

           
            if (digits.Length == 3 && IsDigit(digits[0]) && digits[1] == (byte)'.' && IsDigit(digits[2]))
                tenths = (digits[0] - '0') * 10 + (digits[2] - '0');

            else if (digits.Length == 4 && IsDigit(digits[0]) && IsDigit(digits[1]) && digits[2] == (byte)'.' && IsDigit(digits[3]))
                tenths = (digits[0] - '0') * 100 + (digits[1] - '0') * 10 + (digits[3] - '0');

            else
                return TryParseUnusualShape(utf8Number, out tenths);

            if (isNegative)
                tenths = -tenths;

            return true;
        }

        // Other valid JSON spellings of the same values, such as 12, 12.00 or 1.2e1.
        private static bool TryParseUnusualShape(ReadOnlySpan<byte> utf8Number, out int tenths)
        {
            tenths = 0;

            if (!decimal.TryParse(utf8Number, JsonNumber, CultureInfo.InvariantCulture, out decimal value))
            {
                return false;
            }

            if (value < -99.9m || value > 99.9m)
            {
                return false;
            }

            // More than one fractional digit (12.34) leaves a remainder here.
            decimal scaled = value * 10;
            if (scaled != decimal.Truncate(scaled))
            {
                return false;
            }

            tenths = (int)scaled;
            return true;
        }

        private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';
    }
}

