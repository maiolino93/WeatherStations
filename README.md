# Weather Stations

An ASP.NET Core service that receives weather station measurements as a JSON Lines stream and returns the minimum, mean and maximum temperature of each station. A console client that generates test data is included.

Built with .NET 10 and ASP.NET Core minimal APIs. The service and the client use no third-party packages; the tests use xUnit.

## Build and run

You need the .NET 10 SDK.

```bash
dotnet build
dotnet test
dotnet run --project src/WeatherStations.Api
```

The service listens on `http://localhost:5080`. In a second terminal, send it some generated data:

```bash
dotnet run --project src/WeatherStations.Client -- --count 1000
```

No configuration is needed for these commands. `dotnet run` starts the service in the `Development` environment, where the API key is `dev-only-key`, and the client sends that same key by default.

## API key

Every request must send the key in the `X-Api-Key` header. A request without it, or with the wrong one, gets `401`. This includes requests to routes that do not exist.

The service reads the key from the `Authentication:ApiKey` setting, which can come from any ASP.NET Core configuration source:

```bash
# environment variable (bash)
export Authentication__ApiKey="my-secret-key"

# environment variable (PowerShell)
$env:Authentication__ApiKey = "my-secret-key"

# command line
dotnet run --project src/WeatherStations.Api -- --Authentication:ApiKey=my-secret-key
```

In the `Development` environment, `appsettings.Development.json` sets the key to `dev-only-key` so that `dotnet run` works without any setup. It is not a secret. In every other environment there is no default, and the service refuses to start until a key is configured.

The client takes the key from `--api-key`, then from the `WEATHER_API_KEY` environment variable, and falls back to `dev-only-key`.

To run both with a key of your own:

```bash
dotnet run --project src/WeatherStations.Api -- --Authentication:ApiKey=my-secret-key
dotnet run --project src/WeatherStations.Client -- --count 1000 --api-key my-secret-key
```

Once a key is set this way, `dev-only-key` is no longer accepted.

The key travels in a header, so a real deployment needs TLS, normally terminated at the gateway in front of the service.

## Using the service

`POST /measurements` with a JSON Lines body: one measurement per line, of any total size.

`sample.jsonl` in this repository is a small input with one bad line in it:

```json
{"stationName":"Hamburg","temperature":12.0}
{"stationName":"hamburg ","temperature":34.2}
this is not json
{"stationName":"st. JOHN'S","temperature":-0.5}
```

```bash
curl -i -X POST -H "X-Api-Key: dev-only-key" --data-binary "@sample.jsonl" http://localhost:5080/measurements
```

In Windows PowerShell, write `curl.exe` instead of `curl`.

```
HTTP/1.1 200 OK
Content-Type: application/x-ndjson
X-Accepted-Lines: 3
X-Rejected-Lines: 1
X-Rejected-Line-Numbers: 3

{"stationName":"Hamburg","min":12.0,"mean":23.1,"max":34.2}
{"stationName":"St. John's","min":-0.5,"mean":-0.5,"max":-0.5}
```

The result has one line per station, sorted by name.

| Status | When |
| --- | --- |
| `200` | The body was processed. An empty body gives an empty result. |
| `400` | Lines were sent and none of them was valid, or the body has more distinct stations than the limit. |
| `401` | The API key is missing or wrong. |
| `404` | Unknown route, with a valid key. |

| Response header | Meaning |
| --- | --- |
| `X-Accepted-Lines` | Lines that were aggregated. |
| `X-Rejected-Lines` | Lines that could not be used. |
| `X-Rejected-Line-Numbers` | Line numbers of the first ten rejected lines. |

Errors are returned as problem details (`application/problem+json`).

## Test client

```bash
dotnet run --project src/WeatherStations.Client -c Release -- --count 5000000 > result.jsonl
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--count` | `1000` | Measurements to generate. |
| `--url` | `http://localhost:5080` | Address of the service. |
| `--api-key` | `WEATHER_API_KEY`, then `dev-only-key` | API key. |
| `--stations` | `400` | Number of different stations. |
| `--seed` | random | Makes a run repeatable. The seed in use is always printed. |

The body is generated while it is being sent, so the client's memory use does not depend on `--count`. Each line picks one of three spellings of the station name (as is, lowercase, uppercase), which exercises case-insensitive aggregation. The result goes to standard output; progress and timing go to standard error.

## Configuration

| Setting | Default | Meaning |
| --- | --- | --- |
| `Authentication:ApiKey` | none | Required. See above. |
| `Aggregation:MaxStations` | `1000000` | Largest number of distinct stations in one request. |
| `Aggregation:RejectedLinesDirectory` | `rejected-lines` | Folder for the files of rejected lines. Empty turns them off. |
| `Aggregation:MaxRejectedLinesSaved` | `1000` | Rejected lines saved per request. All of them are still counted. |

## Assumptions

The specification leaves these points open. This is what the service does.

- **Invalid lines are skipped, not fatal.** A line that is not valid JSON, lacks a property, or breaks a constraint is left out of the result. It is counted, reported in the response headers, logged, and saved with its line number and the reason to a file in `rejected-lines`.
- **A body with no valid line at all is a `400`.** Otherwise sending the wrong file would look like a success with no data. An empty body is valid and returns an empty `200`.
- **Whitespace around a station name is not part of it.** `" hamburg"` and `"hamburg "` are the same station as `"hamburg"`. Whitespace between words is kept as it is.
- **The 1–100 byte limit** applies to the decoded name as sent, before trimming, counted in UTF-8 bytes.
- **Case-insensitive means ordinal, culture-independent comparison.** No Unicode normalisation is applied, and `ß` is not the same as `SS`.
- **A word is delimited by whitespace only.** Punctuation does not start a new word, so `winston-salem` becomes `Winston-salem`.
- **The output name is derived from the uppercase form of the name**, so every spelling of a station gives the same result whichever arrived first.
- **Temperatures** may be written in any valid JSON way (`12`, `12.00`, `1.2e1`) as long as the value is within range and has at most one fractional digit. `12.34` and `100.0` are rejected.
- **The mean is rounded to one fractional digit, halves away from zero**: 22.05 becomes 22.1 and -22.05 becomes -22.1.
- **Output is sorted by ordinal (character code) order.** It is the same on every machine, but it is not dictionary order: `Århus` comes after `Zürich`.
- **JSON details.** Unknown properties are ignored. Property names are case-sensitive. If a property appears twice, the last one wins.
- **File format.** Blank lines are skipped. Windows line endings, a byte order mark, and a last line without a line break are all accepted.
- **Nothing is assumed about how the body is delivered.** The network can hand it over in pieces of any size, and a piece can end in the middle of a line or of a character. The result is the same wherever the cuts fall; a test sends one body cut in eight different ways to check it.

## Design decisions

- **The body is read as a stream.** Lines are parsed as they arrive from the request pipe and the body is never buffered whole. The default request size limit is lifted for this endpoint.
- **Memory grows with the number of stations, not the number of measurements.** Per station the service keeps four integers: min, max, sum and count.
- **Temperatures are integers, in tenths of a degree.** `12.3` is stored as `123`. Sums and comparisons are exact, which a `double` sum over a billion values would not be.
- **No allocation per line.** Lines are parsed with `Utf8JsonReader` straight from the read buffer, and the station is looked up with a span (`Dictionary.GetAlternateLookup`). 
- **Nothing is written until the whole body has been read.** That is what makes a clean `400` possible and lets the totals travel as response headers.
- **Limits protect the service.** A line is at most 4096 bytes, so a body without line breaks cannot grow the read buffer. The number of stations and the number of saved rejected lines are capped too.
- **The API key is checked by an ASP.NET Core authentication scheme** with a fallback authorization policy, so a new endpoint is protected by default. A catch-all route makes unknown paths answer `401` as well. Keys are compared in constant time.

## Performance

Measured in Release on a laptop, with client and service on the same machine:

| Measurements | Stations | Body size | Time | Rate |
| --- | --- | --- | --- | --- |
| 5,000,000 | 400 | about 0.2 GB | 1.7 s | 2.9 million/s |
| 100,000,000 | 10,000 | about 5 GB | 23.3 s | 4.3 million/s |
| 1,000,000,000 | 10,000 | about 52 GB | 239 s | 4.2 million/s |

The service's peak memory was 77 MB after the first two runs and 72 MB in the run with a billion measurements. It does not grow with the size of the body: it depends on the number of stations.

Throughput is bounded by parsing.

## How I would scale this

- **More instances.** Requests share no state, so the service runs behind a load balancer without coordination.
- **As a worker in a distributed job.** Min, max, sum and count can be merged without the original measurements (`StationStats.Combine` does exactly that), so a large input can be split, aggregated by several instances and combined by a coordinator. The missing piece is the output: the service returns the mean, and two means cannot be merged without their counts. A partial-result format with `sum` and `count` would be needed, with the mean computed only at the end.
- **Behind a message broker.** The same aggregation works on a stream of messages instead of an uploaded file. Arrival order does not matter for these four values, and partitioning by station name keeps each station on one consumer. A stream has no end, so results would be produced per time window, and since brokers usually deliver at least once, duplicates would have to be detected. Rejected lines map directly to a dead-letter queue.
- **Retries.** A 10 GB upload that fails has to start over. Smaller chunks make a retry cheap, as long as a chunk is never counted twice.
- **Parallel parsing inside one request** hand complete blocks of lines to worker threads and merge their results.
- **For data that already sits in files**, a batch job reading from storage is a better fit than an HTTP upload.

## Project layout

```
src/WeatherStations.Api/        the service
  Authentication/               API key scheme
  Aggregation/                  parsing, aggregation, output and the endpoint
src/WeatherStations.Client/     console test client
tests/WeatherStations.Tests/    unit and integration tests
```
