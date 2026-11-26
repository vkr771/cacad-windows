using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Cacad;

/// <summary>The weather where you are, from Open-Meteo (open-meteo.com: free, no account). Where you are comes from
/// Windows' location (if you allow apps to use it) or the city typed in Settings; only those coordinates are sent.
/// Refreshed every 20 minutes. Read on the UI thread.</summary>
public sealed class WeatherService
{
    public sealed record Hour(DateTime At, double Temp, int Code, int Rain);
    public sealed record Now(string Place, double Temp, double Feels, int Code, bool Day, double High, double Low, int RainChance, List<Hour> Hours, DateTime Fetched,
        DateTime Sunrise = default, DateTime Sunset = default);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static WeatherService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("cacad/1.2 (Windows desktop app)");

    private readonly SettingsStore _s;
    private DateTime _next = DateTime.MinValue;
    private bool _busy;

    public Now? Current { get; private set; }
    public string Status { get; private set; } = "";
    public event Action? Changed;

    public WeatherService(SettingsStore s) => _s = s;

    /// <summary>Called from the island's poll; fetches when due.</summary>
    public void Poll(Action<Action> post)
    {
        if (!_s.Weather || _busy || DateTime.Now < _next) return;
        _busy = true;
        _next = DateTime.Now.AddMinutes(20);
        _ = Task.Run(async () =>
        {
            Now? got = null;
            string status;
            try { (got, status) = await Fetch(); }
            catch (Exception ex) { status = ex is HttpRequestException or TaskCanceledException ? "Offline" : "Couldn't get the weather"; _next = DateTime.Now.AddMinutes(3); }
            post(() =>
            {
                _busy = false;
                Status = status;
                if (got != null) Current = got;
                Changed?.Invoke();
            });
        });
    }

    /// <summary>Look again now (the city changed).</summary>
    public void Refresh() { _next = DateTime.MinValue; Current = null; }

    private async Task<(Now?, string)> Fetch()
    {
        var (lat, lon, place) = await Where();
        if (lat == null) return (null, _s.WeatherCity.Length > 0 ? $"Couldn't find \"{_s.WeatherCity}\"" : "Set your city in Settings › Live");
        string f(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string unit = _s.WeatherFahrenheit ? "&temperature_unit=fahrenheit" : "";
        string url = $"https://api.open-meteo.com/v1/forecast?latitude={f(lat.Value)}&longitude={f(lon!.Value)}" +
                     "&current=temperature_2m,apparent_temperature,weather_code,is_day" +
                     "&hourly=temperature_2m,weather_code,precipitation_probability&forecast_hours=12" +
                     "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset&forecast_days=1&timezone=auto" + unit;
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var root = doc.RootElement;
        var cur = root.GetProperty("current");
        var hourly = root.GetProperty("hourly");
        var times = hourly.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!, CultureInfo.InvariantCulture)).ToList();
        var temps = hourly.GetProperty("temperature_2m").EnumerateArray().Select(Num).ToList();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().Select(e => (int)Num(e)).ToList();
        var rain = hourly.GetProperty("precipitation_probability").EnumerateArray().Select(e => (int)Num(e)).ToList();
        var hours = times.Select((t, i) => new Hour(t, temps[i], codes[i], rain[i])).ToList();
        var daily = root.GetProperty("daily");
        return (new Now(place, Num(cur.GetProperty("temperature_2m")), Num(cur.GetProperty("apparent_temperature")),
            (int)Num(cur.GetProperty("weather_code")), Num(cur.GetProperty("is_day")) > 0,
            Num(daily.GetProperty("temperature_2m_max")[0]), Num(daily.GetProperty("temperature_2m_min")[0]),
            (int)Num(daily.GetProperty("precipitation_probability_max")[0]), hours, DateTime.Now,
            Time(daily, "sunrise"), Time(daily, "sunset")), "");
    }

    private static double Num(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;

    private static DateTime Time(JsonElement daily, string name) =>
        daily.TryGetProperty(name, out var arr) && arr.GetArrayLength() > 0 && arr[0].GetString() is { } s
            ? DateTime.Parse(s, CultureInfo.InvariantCulture) : default;

    /// <summary>The typed city (looked up with Open-Meteo's geocoding), else Windows' location if allowed.</summary>
    private async Task<(double?, double?, string)> Where()
    {
        string city = _s.WeatherCity.Trim();
        if (city.Length > 0)
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?count=1&name={Uri.EscapeDataString(city)}"));
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0) return (null, null, city);
            var r = res[0];
            return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), r.GetProperty("name").GetString() ?? city);
        }
        try
        {
            var access = await Windows.Devices.Geolocation.Geolocator.RequestAccessAsync();
            if (access != Windows.Devices.Geolocation.GeolocationAccessStatus.Allowed) return (null, null, "");
            var geo = new Windows.Devices.Geolocation.Geolocator { DesiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.Default };
            var pos = await geo.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10));
            var p = pos.Coordinate.Point.Position;
            // Rounded to about a kilometre: plenty for weather.
            return (Math.Round(p.Latitude, 2), Math.Round(p.Longitude, 2), "Your location");
        }
        catch { return (null, null, ""); }
    }

    // ---------- WMO weather codes ----------

    public static bool Rainy(int code) => code is >= 51 and <= 67 or >= 80 and <= 82 or >= 95;
    public static bool Snowy(int code) => code is >= 71 and <= 77 or 85 or 86;

    public static string Describe(int code) => code switch
    {
        0 => "Clear", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Cloudy",
        45 or 48 => "Fog", >= 51 and <= 55 => "Drizzle", 56 or 57 => "Freezing drizzle",
        61 => "Light rain", 63 => "Rain", 65 => "Heavy rain", 66 or 67 => "Freezing rain",
        71 => "Light snow", 73 => "Snow", 75 => "Heavy snow", 77 => "Snow grains",
        80 => "Showers", 81 => "Heavy showers", 82 => "Violent showers", 85 or 86 => "Snow showers",
        95 => "Thunderstorm", 96 or 99 => "Thunderstorm, hail", _ => "—",
    };

    public static string Icon(int code, bool day) => code switch
    {
        0 or 1 => day ? "sun" : "moon",
        2 => day ? "cloud-sun" : "cloud",
        3 => "cloud",
        45 or 48 => "cloud-fog",
        >= 95 => "cloud-lightning",
        _ when Snowy(code) => "cloud-snow",
        _ when Rainy(code) => "cloud-rain",
        _ => "cloud",
    };
}
