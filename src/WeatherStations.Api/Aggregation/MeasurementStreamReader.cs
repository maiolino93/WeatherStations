using System.Buffers;
using System.IO.Pipelines;

namespace WeatherStations.Api.Aggregation
{
    // Reads a JSONL body line by line as it arrives. Only the line being read
    // is held in memory, so the size of the body does not matter.
    public sealed class MeasurementStreamReader
    {
        public const int MaxLineBytes = 4096;

        private const string LineTooLong = "The line is longer than 4096 bytes.";

        private readonly StationAggregator _aggregator;
        private readonly RejectedLines _rejected;

        // Reused for every line, so reading a line allocates nothing.
        private readonly char[] _stationName = new char[MeasurementParser.MaxStationNameBytes];
        private readonly byte[] _lineCopy = new byte[MaxLineBytes];

        private bool _skippingRestOfLine;

        public MeasurementStreamReader(StationAggregator aggregator, RejectedLines rejected)
        {
            _aggregator = aggregator;
            _rejected = rejected;
        }

        public long LinesRead { get; private set; }

        public long AcceptedLines { get; private set; }

        // Set when a new station would exceed the limit. Reading stops there.
        public bool StationLimitReached { get; private set; }

        public async Task ReadAsync(PipeReader input, CancellationToken cancellationToken)
        {
            while (true)
            {
                ReadResult result = await input.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> buffer = result.Buffer;

                SequencePosition consumed = ProcessLines(buffer, result.IsCompleted);

                // Tells the pipe what has been used. The unfinished line at the
                // end stays in the buffer and comes back with more data next time.
                input.AdvanceTo(consumed, buffer.End);

                if (result.IsCompleted || StationLimitReached)
                {
                    break;
                }
            }
        }

        private SequencePosition ProcessLines(ReadOnlySequence<byte> buffer, bool isLastRead)
        {
            var reader = new SequenceReader<byte>(buffer);

            while (!StationLimitReached)
            {
                if (_skippingRestOfLine)
                {
                    // The start of this line was already rejected for being
                    // too long. Throw away the rest of it, up to the line break.
                    if (!reader.TryAdvanceTo((byte)'\n'))
                    {
                        reader.AdvanceToEnd();
                        break;
                    }

                    _skippingRestOfLine = false;
                }
                else if (reader.TryReadTo(out ReadOnlySequence<byte> line, (byte)'\n'))
                {
                    ProcessLine(line);
                }
                else
                {
                    break;
                }
            }

            if (StationLimitReached)
            {
                return reader.Position;
            }

            if (!_skippingRestOfLine && reader.Remaining > MaxLineBytes)
            {
                // No line break yet and already over the limit. Rejecting it
                // now keeps the buffer from growing while we wait for one.
                LinesRead++;
                _rejected.Add(LinesRead, LineTooLong, AsSpan(reader.UnreadSequence.Slice(0, MaxLineBytes)));
                _skippingRestOfLine = true;
                reader.AdvanceToEnd();
            }
            else if (isLastRead && !_skippingRestOfLine && reader.Remaining > 0)
            {
                // The last line of a body does not have to end with a line break.
                ProcessLine(reader.UnreadSequence);
                reader.AdvanceToEnd();
            }

            return reader.Position;
        }

        private void ProcessLine(ReadOnlySequence<byte> line)
        {
            LinesRead++;

            if (line.Length > MaxLineBytes)
            {
                _rejected.Add(LinesRead, LineTooLong, AsSpan(line.Slice(0, MaxLineBytes)));
                return;
            }

            ReadOnlySpan<byte> bytes = AsSpan(line);

            // Some editors put a byte order mark at the very start of a file.
            if (LinesRead == 1 && bytes.StartsWith("\uFEFF"u8))
            {
                bytes = bytes["\uFEFF"u8.Length..];
            }

            // Windows line endings leave a carriage return before the line break.
            if (bytes.EndsWith("\r"u8))
            {
                bytes = bytes[..^1];
            }

            if (bytes.IndexOfAnyExcept(" \t"u8) < 0)
            {
                // Blank lines are skipped. They still count as a line, so the
                // numbers reported for rejected lines match the original file.
                return;
            }

            if (!MeasurementParser.TryParse(bytes, _stationName, out int nameLength, out int tenths, out string? error))
            {
                _rejected.Add(LinesRead, error, bytes);
                return;
            }

            if (!_aggregator.TryAdd(_stationName.AsSpan(0, nameLength), tenths))
            {
                StationLimitReached = true;
                return;
            }

            AcceptedLines++;
        }

        // A line usually sits in one piece of the buffer and can be used where
        // it is. When it straddles two pieces it is copied so the parser sees
        // it as a whole.
        private ReadOnlySpan<byte> AsSpan(ReadOnlySequence<byte> line)
        {
            if (line.IsSingleSegment)
            {
                return line.FirstSpan;
            }

            line.CopyTo(_lineCopy);
            return _lineCopy.AsSpan(0, (int)line.Length);
        }
    }
}