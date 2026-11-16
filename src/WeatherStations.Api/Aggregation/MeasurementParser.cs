using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace WeatherStations.Api.Aggregation
{
    public static class MeasurementParser
    {
        public const int MaxStationNameBytes = 100;

        // A name of 100 bytes can take up to 600 bytes when every
        // character is written as a \uXXXX escape.
        private const int MaxEscapedStationNameBytes = MaxStationNameBytes * 6;

        // Reads one line such as {"stationName":"Hamburg","temperature":12.0}.
        public static bool TryParse(
            ReadOnlySpan<byte> line,
            Span<char> stationName,
            out int stationNameLength,
            out int temperatureTenths,
            [NotNullWhen(false)] out string? error)
        {
            if (stationName.Length < MaxStationNameBytes)
            {
                throw new ArgumentException($"The buffer must hold at least {MaxStationNameBytes} characters.", nameof(stationName));
            }

            stationNameLength = 0;
            temperatureTenths = 0;

            try
            {
                return TryRead(line, stationName, out stationNameLength, out temperatureTenths, out error);
            }
            catch (JsonException)
            {
                error = "The line is not valid JSON.";
                return false;
            }
        }

        private static bool TryRead(
            ReadOnlySpan<byte> line,
            Span<char> stationName,
            out int stationNameLength,
            out int temperatureTenths,
            [NotNullWhen(false)] out string? error)
        {
            stationNameLength = 0;
            temperatureTenths = 0;
            bool hasStationName = false;
            bool hasTemperature = false;

            var reader = new Utf8JsonReader(line);

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                error = "The line must be a JSON object.";
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("stationName"u8))
                {
                    reader.Read();
                    if (!TryReadStationName(ref reader, stationName, out stationNameLength, out error))
                    {
                        return false;
                    }

                    hasStationName = true;
                }
                else if (reader.ValueTextEquals("temperature"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.Number
                        || !TemperatureParser.TryParse(reader.ValueSpan, out temperatureTenths))
                    {
                        error = "'temperature' must be a number between -99.9 and 99.9 with at most one fractional digit.";
                        return false;
                    }

                    hasTemperature = true;
                }
                else
                {
                    // Unknown properties are ignored, whatever their value is.
                    reader.Skip();
                }
            }

            // The object is closed. Reading once more makes the reader throw
            // if anything other than whitespace follows it on the line.
            reader.Read();

            if (!hasStationName)
            {
                error = "'stationName' is missing.";
                return false;
            }

            if (!hasTemperature)
            {
                error = "'temperature' is missing.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryReadStationName(
            ref Utf8JsonReader reader,
            Span<char> stationName,
            out int length,
            [NotNullWhen(false)] out string? error)
        {
            length = 0;

            if (reader.TokenType != JsonTokenType.String)
            {
                error = "'stationName' must be a string.";
                return false;
            }

            try
            {
                int byteCount;

                if (!reader.ValueIsEscaped)
                {
                    // The usual case: the bytes on the wire are the name itself.
                    byteCount = reader.ValueSpan.Length;
                    if (byteCount is >= 1 and <= MaxStationNameBytes)
                    {
                        length = reader.CopyString(stationName);
                    }
                }
                else if (reader.ValueSpan.Length > MaxEscapedStationNameBytes)
                {
                    byteCount = int.MaxValue;
                }
                else
                {
                    // The limit applies to the decoded name, so decode it first
                    // into a scratch buffer and measure that.
                    Span<char> decoded = stackalloc char[MaxEscapedStationNameBytes];
                    int decodedLength = reader.CopyString(decoded);
                    byteCount = Encoding.UTF8.GetByteCount(decoded[..decodedLength]);

                    if (byteCount is >= 1 and <= MaxStationNameBytes)
                    {
                        decoded[..decodedLength].CopyTo(stationName);
                        length = decodedLength;
                    }
                }

                if (byteCount is < 1 or > MaxStationNameBytes)
                {
                    error = $"'stationName' must be between 1 and {MaxStationNameBytes} bytes long in UTF-8.";
                    return false;
                }
            }
            catch (InvalidOperationException)
            {
                // Thrown by CopyString for bytes that are not valid UTF-8 and
                // for escapes that do not form a valid character.
                error = "'stationName' is not valid Unicode text.";
                return false;
            }

            // Whitespace around the name is not part of it, so " hamburg" and
            // "hamburg " are the same station. Spaces between words are left alone.
            Span<char> trimmed = stationName[..length].Trim();
            if (trimmed.IsEmpty)
            {
                length = 0;
                error = "'stationName' must contain at least one character that is not whitespace.";
                return false;
            }

            trimmed.CopyTo(stationName);
            length = trimmed.Length;

            error = null;
            return true;
        }
    }
}
