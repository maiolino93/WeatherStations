using System.Diagnostics;
using System.Text;
using WeatherStations.Client;

if (!ClientOptions.TryParse(args, out ClientOptions? options, out string? error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(ClientOptions.Usage);
    return 2;
}

// Station names are UTF-8. Without this, a Windows console shows "São Paulo" garbled.
Console.OutputEncoding = Encoding.UTF8;

// A large upload takes minutes, well past the default timeout of 100 seconds.
using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
using var content = new MeasurementContent(options);
using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Url, "measurements")) { Content = content };
request.Headers.Add("X-Api-Key", options.ApiKey);

// Asks the service to approve the request before the body is sent. With a
// wrong key the answer is an immediate 401 instead of a 10 GB upload.
request.Headers.ExpectContinue = true;

// Everything except the result goes to stderr, so stdout can be piped to a file.
Console.Error.WriteLine($"Sending {options.Count:N0} measurements for {options.Stations:N0} stations to {options.Url} (seed {options.Seed}).");

var stopwatch = Stopwatch.StartNew();
using var progress = new Timer(
    _ => Console.Error.WriteLine($"  {content.LinesSent:N0} sent, {stopwatch.Elapsed.TotalSeconds:F0} s"),
    state: null,
    dueTime: TimeSpan.FromSeconds(5),
    period: TimeSpan.FromSeconds(5));

try
{
    // ResponseHeadersRead hands the response over as soon as its headers
    // arrive, so the body can be copied out as a stream.
    using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    await progress.DisposeAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"The service answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        Console.Error.WriteLine(await response.Content.ReadAsStringAsync());
        return 1;
    }

    await using (Stream stdout = Console.OpenStandardOutput())
    {
        await response.Content.CopyToAsync(stdout);
    }

    double seconds = stopwatch.Elapsed.TotalSeconds;
    Console.Error.WriteLine($"Accepted {Header(response, "X-Accepted-Lines")}, rejected {Header(response, "X-Rejected-Lines")}.");
    Console.Error.WriteLine($"Done in {seconds:F1} s ({options.Count / Math.Max(seconds, 0.001):N0} measurements per second).");
    return 0;
}
catch (HttpRequestException exception)
{
    Console.Error.WriteLine($"Could not complete the request to {options.Url}: {exception.Message}");
    return 1;
}

static string Header(HttpResponseMessage response, string name)
{
    return response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : "?";
}