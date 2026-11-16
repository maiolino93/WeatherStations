using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WeatherStations.Api.Aggregation
{
    public readonly record struct RejectedLine(long LineNumber, string Reason);

    // Keeps track of the lines one request could not use: how many there were,
    // and a file with the lines themselves.
    public sealed class RejectedLines : IDisposable
    {
        public const int MaxSamples = 10;

        private static readonly JsonWriterOptions WriterOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly string? _directory;
        private readonly int _maxSaved;
        private readonly List<RejectedLine> _samples = [];
        private FileStream? _file;
        private Utf8JsonWriter? _writer;
        private int _saved;

        public RejectedLines(string? directory, int maxSaved)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maxSaved);

            _directory = string.IsNullOrWhiteSpace(directory) ? null : directory;
            _maxSaved = maxSaved;
        }

        public long Count { get; private set; }

        public IReadOnlyList<RejectedLine> Samples => _samples;

        // Null until the first line is saved, so a clean request leaves no file behind.
        public string? FilePath { get; private set; }

        public void Add(long lineNumber, string reason, ReadOnlySpan<byte> content)
        {
            Count++;

            if (_samples.Count < MaxSamples)
            {
                _samples.Add(new RejectedLine(lineNumber, reason));
            }

            if (_directory is not null && _saved < _maxSaved)
            {
                Save(lineNumber, reason, content);
                _saved++;
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _file?.Dispose();
        }

        private void Save(long lineNumber, string reason, ReadOnlySpan<byte> content)
        {
            if (_writer is null || _file is null)
            {
                Open();
            }

            _writer.WriteStartObject();
            _writer.WriteNumber("line", lineNumber);
            _writer.WriteString("reason", reason);

            // The content may not even be valid text. Decoding it this way
            // replaces whatever cannot be read instead of failing.
            _writer.WriteString("content", Encoding.UTF8.GetString(content));
            _writer.WriteEndObject();
            _writer.Flush();
            _file.WriteByte((byte)'\n');

            // The writer expects a single JSON value; resetting it allows the next line.
            _writer.Reset();
        }

        [MemberNotNull(nameof(_writer), nameof(_file))]
        private void Open()
        {
            Directory.CreateDirectory(_directory!);

            // The random part keeps two requests in the same second apart.
            string fileName = $"rejected-{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid():N}.jsonl";
            FilePath = Path.Combine(_directory!, fileName);

            _file = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _writer = new Utf8JsonWriter(_file, WriterOptions);
        }
    }
}