using System.Buffers;
using System.Net;

namespace WeatherStations.Client
{
   
    public sealed class MeasurementContent(ClientOptions options) : HttpContent
    {
        private const int BufferSize = 64 * 1024;

        // Room for the longest line: a 100-byte name plus the fixed JSON around it.
        private const int MaxLineBytes = 256;

        private long _linesSent;

        // Read by the progress timer on another thread while the body is being written.
        public long LinesSent => Interlocked.Read(ref _linesSent);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            byte[][] spellings = StationCatalog.Spellings(options.Stations);
            var random = new Random(options.Seed);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

            try
            {
                int used = 0;

                for (long line = 0; line < options.Count; line++)
                {
                    if (used > BufferSize - MaxLineBytes)
                    {
                        await stream.WriteAsync(buffer.AsMemory(0, used), cancellationToken);
                        used = 0;
                        Interlocked.Exchange(ref _linesSent, line);
                    }

                    byte[] name = spellings[random.Next(spellings.Length)];
                    int tenths = random.Next(-999, 1000);
                    used += WriteLine(buffer.AsSpan(used), name, tenths);
                }

                await stream.WriteAsync(buffer.AsMemory(0, used), cancellationToken);
                Interlocked.Exchange(ref _linesSent, options.Count);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // The total size is not known up front, so the body goes out in
        // chunks instead of with a Content-Length header.
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        private static int WriteLine(Span<byte> destination, ReadOnlySpan<byte> name, int tenths)
        {
            int length = 0;

            length += Append(destination[length..], "{\"stationName\":\""u8);
            length += Append(destination[length..], name);
            length += Append(destination[length..], "\",\"temperature\":"u8);

            // Tenths of a degree written with exactly one fractional digit: -5 becomes -0.5.
            int magnitude = Math.Abs(tenths);
            if (tenths < 0)
            {
                destination[length++] = (byte)'-';
            }

            if (magnitude >= 100)
            {
                destination[length++] = (byte)('0' + magnitude / 100);
            }

            destination[length++] = (byte)('0' + magnitude / 10 % 10);
            destination[length++] = (byte)'.';
            destination[length++] = (byte)('0' + magnitude % 10);

            length += Append(destination[length..], "}\n"u8);
            return length;
        }

        private static int Append(Span<byte> destination, ReadOnlySpan<byte> text)
        {
            text.CopyTo(destination);
            return text.Length;
        }
    }
}