using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using WeatherStations.Api.Authentication;
using WeatherStations.Api.Aggregation;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ApiKeyOptions>()
    .BindConfiguration(ApiKeyOptions.SectionName)
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApiKey),
        "Authentication:ApiKey is not configured. Set it in configuration or through the Authentication__ApiKey environment variable.")
    .ValidateOnStart();

builder.Services
    .AddOptions<AggregationOptions>()
    .BindConfiguration(AggregationOptions.SectionName)
    .Validate(options => options.MaxStations > 0, "Aggregation:MaxStations must be greater than zero.")
    .Validate(options => options.MaxRejectedLinesSaved >= 0, "Aggregation:MaxRejectedLinesSaved cannot be negative.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, configureOptions: null);

builder.Services
    .AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost(MeasurementsEndpoint.Route, MeasurementsEndpoint.HandleAsync);
app.MapFallback("{*path}", () => Results.NotFound());
app.Run();
