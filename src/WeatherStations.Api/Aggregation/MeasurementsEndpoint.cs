using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace WeatherStations.Api.Aggregation
{
    public static class MeasurementsEndpoint
    {
        public const string Route = "/measurements";

        public const string AcceptedLinesHeader = "X-Accepted-Lines";
        public const string RejectedLinesHeader = "X-Rejected-Lines";
        public const string RejectedLineNumbersHeader = "X-Rejected-Line-Numbers";

        public static async Task HandleAsync(
            HttpContext context,
            IOptions<AggregationOptions> options,
            IHostEnvironment environment,
            ILogger<Program> logger)
        {
            AggregationOptions settings = options.Value;

            // Kestrel refuses bodies over about 30 MB unless told otherwise,
            // and this endpoint exists to take much larger ones. It has to be
            // lifted before the first byte of the body is read.
            IHttpMaxRequestBodySizeFeature? bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false })
            {
                bodySize.MaxRequestBodySize = null;
            }

            var aggregator = new StationAggregator(settings.MaxStations);
            using var rejected = new RejectedLines(RejectedLinesDirectory(settings, environment), settings.MaxRejectedLinesSaved);
            var reader = new MeasurementStreamReader(aggregator, rejected);
            long started = Stopwatch.GetTimestamp();

            await reader.ReadAsync(context.Request.BodyReader, context.RequestAborted);

            // Nothing has been written to the response yet, which is why the
            // totals can still travel as headers and an error can still be a 400.
            WriteSummaryHeaders(context.Response, reader, rejected);
            LogSummary(logger, context, reader, aggregator, rejected, Stopwatch.GetElapsedTime(started));

            if (reader.StationLimitReached)
            {
                await Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Too many stations",
                        detail: $"The body has more than {settings.MaxStations} different stations. The limit was reached at line {reader.LinesRead}.")
                    .ExecuteAsync(context);
                return;
            }

            if (reader.AcceptedLines == 0 && rejected.Count > 0)
            {
                // A wrong file would otherwise come back as an
                // empty 200 and look like a success.
                RejectedLine first = rejected.Samples[0];
                await Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "No valid measurements",
                        detail: $"None of the {rejected.Count} lines could be used. Line {first.LineNumber}: {first.Reason}")
                    .ExecuteAsync(context);
                return;
            }

            context.Response.ContentType = "application/x-ndjson";
            await ResultWriter.WriteAsync(aggregator.Stations, context.Response.BodyWriter, context.RequestAborted);
        }

        private static string? RejectedLinesDirectory(AggregationOptions settings, IHostEnvironment environment)
        {
            return string.IsNullOrWhiteSpace(settings.RejectedLinesDirectory)
                ? null
                : Path.GetFullPath(settings.RejectedLinesDirectory, environment.ContentRootPath);
        }

        private static void WriteSummaryHeaders(HttpResponse response, MeasurementStreamReader reader, RejectedLines rejected)
        {
            response.Headers[AcceptedLinesHeader] = reader.AcceptedLines.ToString(CultureInfo.InvariantCulture);
            response.Headers[RejectedLinesHeader] = rejected.Count.ToString(CultureInfo.InvariantCulture);

            if (rejected.Count > 0)
            {
                // Only the first few, so the header stays small however many lines were bad.
                response.Headers[RejectedLineNumbersHeader] = string.Join(',', rejected.Samples.Select(sample => sample.LineNumber));
            }
        }

        private static void LogSummary(
            ILogger logger,
            HttpContext context,
            MeasurementStreamReader reader,
            StationAggregator aggregator,
            RejectedLines rejected,
            TimeSpan elapsed)
        {
            if (rejected.Count == 0)
            {
                logger.LogInformation(
                    "Request {RequestId}: {Accepted} lines accepted, {Stations} stations, {ElapsedMs} ms.",
                    context.TraceIdentifier, reader.AcceptedLines, aggregator.Stations.Count, (long)elapsed.TotalMilliseconds);
                return;
            }

            // Reasons are our own fixed messages. The content of the lines is
            // never logged: it comes from the client and could be anything.
            string firstRejected = string.Join("; ", rejected.Samples.Select(sample => $"line {sample.LineNumber}: {sample.Reason}"));

            logger.LogWarning(
                "Request {RequestId}: {Accepted} lines accepted, {Rejected} rejected, {Stations} stations, {ElapsedMs} ms. Rejected lines saved to {File}. First rejected: {FirstRejected}",
                context.TraceIdentifier, reader.AcceptedLines, rejected.Count, aggregator.Stations.Count, (long)elapsed.TotalMilliseconds,
                rejected.FilePath ?? "(not saved)", firstRejected);
        }
    }
}