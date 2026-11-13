using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace Bhoomisetu.Api.Places;

/// <summary>A place to fly the map to. Source says where it came from: our own listings, or OpenStreetMap.</summary>
/// <summary>
/// A place to go to on the map. <see cref="BoundingBox"/> is [south, north, west, east]. <see cref="Level"/> is
/// "village", "mandal" or "district" for Bhoomisetu places, null for OpenStreetMap ones.
/// </summary>
public sealed record PlaceResult(string Name, string Detail, double Latitude, double Longitude, int Listings, string Source, double[]? BoundingBox = null, string? Level = null);

/// <summary>Finds places that have no Bhoomisetu listings yet (towns, landmarks), for exploring the map.</summary>
public interface IExternalGeocoder
{
    Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, string language, CancellationToken cancellationToken);
}

public sealed class GeocodingOptions
{
    public const string SectionName = "Geocoding";

    /// <summary>"nominatim" (OpenStreetMap place search) or "none".</summary>
    public string Provider { get; set; } = "nominatim";

    public string NominatimUrl { get; set; } = "https://nominatim.openstreetmap.org/search";

    /// <summary>Nominatim's usage policy asks for an identifying user agent with a contact address for the app.</summary>
    public string UserAgent { get; set; } = "Bhoomisetu/1.0 (+https://github.com/Nishithsai-web/Bhoomisetu-web)";
}

public sealed class NoExternalGeocoder : IExternalGeocoder
{
    public Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, string language, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlaceResult>>([]);
}

/// <summary>
/// OpenStreetMap Nominatim, limited to India. Its policy allows at most one request per second and asks for
/// caching, so requests are serialised with a one-second gap and answers are kept for a day. Failures return
/// nothing: the map still works with our own places.
/// </summary>
public sealed class NominatimGeocoder(HttpClient http, GeocodingOptions options, IMemoryCache cache, ILogger<NominatimGeocoder> logger) : IExternalGeocoder
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset lastCall = DateTimeOffset.MinValue;

    public async Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, string language, CancellationToken cancellationToken)
    {
        var key = $"nominatim:{language}:{query.Trim().ToLowerInvariant()}";
        if (cache.TryGetValue(key, out IReadOnlyList<PlaceResult>? cached) && cached is not null)
        {
            return cached;
        }

        // Wait at most 2 s for our turn: a queue of other searches must never stall this one.
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken))
        {
            return [];
        }

        try
        {
            var wait = lastCall.AddSeconds(1) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            lastCall = DateTimeOffset.UtcNow;
            var url = $"{options.NominatimUrl}?format=jsonv2&countrycodes=in&limit=5&addressdetails=0&q={Uri.EscapeDataString(query)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(options.UserAgent);
            request.Headers.AcceptLanguage.ParseAdd(language);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Place search returned {Status}", (int)response.StatusCode);
                return [];
            }

            var rows = await response.Content.ReadFromJsonAsync<List<NominatimRow>>(cancellationToken) ?? [];
            var results = rows
                .Where(r => double.TryParse(r.Lat, CultureInfo.InvariantCulture, out _) && double.TryParse(r.Lon, CultureInfo.InvariantCulture, out _))
                .Select(r => new PlaceResult(
                    r.Name is { Length: > 0 } n ? n : (r.DisplayName ?? query).Split(',')[0],
                    r.DisplayName ?? string.Empty,
                    double.Parse(r.Lat!, CultureInfo.InvariantCulture),
                    double.Parse(r.Lon!, CultureInfo.InvariantCulture),
                    0,
                    "openstreetmap",
                    r.BoundingBox is { Length: 4 } b && b.All(x => double.TryParse(x, CultureInfo.InvariantCulture, out _))
                        ? [.. b.Select(x => double.Parse(x, CultureInfo.InvariantCulture))]
                        : null))
                .ToList();
            cache.Set(key, (IReadOnlyList<PlaceResult>)results, TimeSpan.FromDays(1));
            return results;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Place search failed");
            return [];
        }
        finally
        {
            Gate.Release();
        }
    }

    private sealed class NominatimRow
    {
        [JsonPropertyName("lat")]
        public string? Lat { get; set; }

        [JsonPropertyName("lon")]
        public string? Lon { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        /// <summary>south, north, west, east as strings.</summary>
        [JsonPropertyName("boundingbox")]
        public string[]? BoundingBox { get; set; }
    }
}
