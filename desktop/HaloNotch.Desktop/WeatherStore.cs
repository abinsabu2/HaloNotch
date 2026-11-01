using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Avalonia.Threading;
using static HaloNotch.Loc;
#if WINDOWS
using Windows.Devices.Geolocation;
#endif

namespace HaloNotch;

/// Weather via Open-Meteo (free, no API key for non-commercial use). Port of the Mac app's WeatherStore.
/// Location: Windows location service (rounded to ~1 km before it is sent) or a city chosen in Settings.
public sealed class WeatherStore : Observable
{
    public sealed record Hour(DateTime Time, double Temperature, int Code, bool IsDay)
    {
        public string Label => Time <= DateTime.Now ? L("Now") : Time.ToString("HH", CultureInfo.CurrentCulture);
        public string Icon => WeatherCode.Icon(Code, IsDay);
        public string Temp => Fmt.Degrees(Temperature);
    }

    public sealed record Day(DateTime Date, int Code, double High, double Low, double RangeStart, double RangeWidth)
    {
        public string Label => Date.Date == DateTime.Today ? L("Today") : Date.ToString("ddd", CultureInfo.CurrentCulture);
        public string Icon => WeatherCode.Icon(Code, true);
        public string HighText => Fmt.Degrees(High);
        public string LowText => Fmt.Degrees(Low);
    }

    public sealed record Snapshot(double Temperature, double FeelsLike, double Humidity, double Wind, string WindUnit, double Pressure,
                                  int Code, bool IsDay, double High, double Low, double UvMax, DateTime? Sunrise, DateTime? Sunset,
                                  List<Hour> Hours, List<Day> Days, DateTime FetchedAt);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    Snapshot? snapshot;
    string status = "";
    string cityQuery = Prefs.Get("weather.city", "");
    string placeName = Prefs.Get("weather.place", "");
    double? lat = Prefs.Has("weather.lat") ? Prefs.Get("weather.lat", 0.0) : null;
    double? lon = Prefs.Has("weather.lon") ? Prefs.Get("weather.lon", 0.0) : null;
    DispatcherTimer? timer;

    public bool Celsius
    {
        get => Prefs.Get("weather.unit", "celsius") == "celsius";
        set { Prefs.Set("weather.unit", value ? "celsius" : "fahrenheit"); Refresh(); ChangedAll(); }
    }

    public bool Automatic
    {
        get => Prefs.Get("weather.mode", "automatic") == "automatic";
        set
        {
            Prefs.Set("weather.mode", value ? "automatic" : "city");
            if (value) RequestLocation(); else if (cityQuery.Length > 0) ApplyCity();
            ChangedAll();
        }
    }

    public bool ByCity { get => !Automatic; set => Automatic = !value; }
    public string CityQuery { get => cityQuery; set => Set(ref cityQuery, value); }

    public Snapshot? Data => snapshot;
    public bool HasData => snapshot != null;
    public bool NoData => snapshot == null;
    public string Status => status;
    public string PlaceName => placeName.Length > 0 ? placeName : "—";
    public string Temperature => snapshot == null ? "–" : Fmt.Degrees(snapshot.Temperature);
    public string Icon => snapshot == null ? "☁️" : WeatherCode.Icon(snapshot.Code, snapshot.IsDay);
    public string Description => snapshot == null ? (status.Length > 0 ? status : L("Loading weather…")) : WeatherCode.Description(snapshot.Code);
    public string HighLow => snapshot == null ? "" : LF("H %@  L %@  ·  Feels %@", Fmt.Degrees(snapshot.High), Fmt.Degrees(snapshot.Low), Fmt.Degrees(snapshot.FeelsLike));
    public string HighLowLong => snapshot == null ? "" : LF("H %@   L %@   ·   Feels like %@", Fmt.Degrees(snapshot.High), Fmt.Degrees(snapshot.Low), Fmt.Degrees(snapshot.FeelsLike));
    public string Humidity => snapshot == null ? "–" : $"{snapshot.Humidity:0} %";
    public string Wind => snapshot == null ? "–" : $"{snapshot.Wind:0} {snapshot.WindUnit}";
    public string Uv => snapshot == null ? "–" : $"{snapshot.UvMax:0}";
    public string Pressure => snapshot == null ? "–" : $"{snapshot.Pressure:0} hPa";
    public string Sunrise => snapshot?.Sunrise is { } t ? Fmt.Time(t) : "–";
    public string Sunset => snapshot?.Sunset is { } t ? Fmt.Time(t) : "–";
    public string Updated => snapshot == null ? "" : LF("Updated %@", Fmt.Time(snapshot.FetchedAt));
    public List<Hour> Hours => snapshot?.Hours ?? [];
    public List<Day> Days => snapshot?.Days ?? [];

    public void Start()
    {
        Refresh();
        if (Automatic) RequestLocation();
        timer = new DispatcherTimer(TimeSpan.FromMinutes(15), DispatcherPriority.Background, (_, _) => Refresh());
        timer.Start();
    }

    /// Refreshes if the data is older than 10 minutes (called when the panel opens).
    public void RefreshIfStale()
    {
        if (snapshot == null || (DateTime.Now - snapshot.FetchedAt).TotalMinutes > 10) Refresh();
    }

    public void Refresh()
    {
        if (lat is { } la && lon is { } lo) _ = Load(la, lo);
        else if (Automatic) RequestLocation();
    }

    public async void ApplyCity()
    {
        var query = cityQuery.Trim();
        if (query.Length == 0) return;
        Prefs.Set("weather.city", query);
        SetStatus(L("Loading weather…"));
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=1&language=en&format=json";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                SetStatus(LF("City “%@” not found", query));
                return;
            }
            var r = results[0];
            var name = r.GetProperty("name").GetString() ?? query;
            if (r.TryGetProperty("country", out var country)) name += ", " + country.GetString();
            SetLocation(r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), name);
            await Load(lat!.Value, lon!.Value);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            SetStatus(e.Message);
        }
    }

    public void OpenLocationSettings() => Shell.Open("ms-settings:privacy-location");

    void SetStatus(string text) { status = text; ChangedAll(); }

    void SetLocation(double latitude, double longitude, string name)
    {
        lat = latitude; lon = longitude;
        Prefs.Set("weather.lat", latitude);
        Prefs.Set("weather.lon", longitude);
        if (name.Length > 0) { placeName = name; Prefs.Set("weather.place", name); }
    }

    async void RequestLocation()
    {
#if WINDOWS
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
            {
                if (lat == null) SetStatus(L("Location access denied – choose a city in Settings"));
                return;
            }
            var position = await new Geolocator { DesiredAccuracy = PositionAccuracy.Default }.GetGeopositionAsync();
            if (!Automatic) return;
            // ponytail: no reverse geocoding (would send the position to another service) – shows "Current location".
            SetLocation(position.Coordinate.Point.Position.Latitude, position.Coordinate.Point.Position.Longitude, L("Current location"));
            await Load(lat!.Value, lon!.Value);
        }
        catch (Exception e)
        {
            if (lat == null) SetStatus(LF("Location unavailable: %@", e.Message));
        }
#else
        if (lat == null) SetStatus(L("Location access denied – choose a city in Settings"));
        await Task.CompletedTask;
#endif
    }

    async Task Load(double latitude, double longitude)
    {
        if (snapshot == null) SetStatus(L("Loading weather…"));
        bool celsius = Celsius;
        var inv = CultureInfo.InvariantCulture;
        // ~1 km precision is plenty for weather – don't send the exact position to a third party.
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={latitude.ToString("0.00", inv)}&longitude={longitude.ToString("0.00", inv)}" +
                  "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m,pressure_msl" +
                  "&hourly=temperature_2m,weather_code,is_day" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,uv_index_max" +
                  $"&temperature_unit={(celsius ? "celsius" : "fahrenheit")}&wind_speed_unit={(celsius ? "kmh" : "mph")}" +
                  "&timezone=auto&timeformat=unixtime&forecast_days=7";
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            snapshot = Parse(doc.RootElement, celsius ? "km/h" : "mph");
            status = "";
            ChangedAll();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Debug.WriteLine($"HaloNotch: weather failed – {e.Message}");
            SetStatus(e.Message);
        }
    }

    static Snapshot Parse(JsonElement r, string windUnit)
    {
        static double? D(JsonElement a, int i) => i < a.GetArrayLength() && a[i].ValueKind == JsonValueKind.Number ? a[i].GetDouble() : null;
        static int I(JsonElement a, int i, int fallback) => D(a, i) is { } v ? (int)v : fallback;
        static DateTime Unix(double s) => DateTimeOffset.FromUnixTimeSeconds((long)s).LocalDateTime;
        static double? C(JsonElement o, string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

        var now = DateTime.Now;
        var hourly = r.GetProperty("hourly");
        var hours = new List<Hour>();
        var ht = hourly.GetProperty("time");
        for (int i = 0; i < ht.GetArrayLength() && hours.Count < 12; i++)
        {
            var time = Unix(ht[i].GetDouble());
            if (time <= now.AddHours(-1) || D(hourly.GetProperty("temperature_2m"), i) is not { } temp) continue;
            hours.Add(new Hour(time, temp, I(hourly.GetProperty("weather_code"), i, 3), I(hourly.GetProperty("is_day"), i, 1) == 1));
        }

        var daily = r.GetProperty("daily");
        var raw = new List<(DateTime, int, double, double)>();
        var dt = daily.GetProperty("time");
        for (int i = 0; i < dt.GetArrayLength(); i++)
        {
            if (D(daily.GetProperty("temperature_2m_max"), i) is not { } hi || D(daily.GetProperty("temperature_2m_min"), i) is not { } lo) continue;
            raw.Add((Unix(dt[i].GetDouble()), I(daily.GetProperty("weather_code"), i, 3), hi, lo));
        }
        // Temperature bars share one scale across the week.
        double min = raw.Count > 0 ? raw.Min(d => d.Item4) : 0, max = raw.Count > 0 ? raw.Max(d => d.Item3) : 1, span = Math.Max(max - min, 1);
        var days = raw.Select(d => new Day(d.Item1, d.Item2, d.Item3, d.Item4, (d.Item4 - min) / span, (d.Item3 - d.Item4) / span)).ToList();

        var c = r.GetProperty("current");
        DateTime? First(string key) => D(daily.GetProperty(key), 0) is { } v ? Unix(v) : null;
        return new Snapshot(C(c, "temperature_2m") ?? hours.FirstOrDefault()?.Temperature ?? 0,
            C(c, "apparent_temperature") ?? C(c, "temperature_2m") ?? 0, C(c, "relative_humidity_2m") ?? 0,
            C(c, "wind_speed_10m") ?? 0, windUnit, C(c, "pressure_msl") ?? 0, (int)(C(c, "weather_code") ?? 3),
            (C(c, "is_day") ?? 1) == 1, days.FirstOrDefault()?.High ?? 0, days.FirstOrDefault()?.Low ?? 0,
            D(daily.GetProperty("uv_index_max"), 0) ?? 0, First("sunrise"), First("sunset"), hours, days, now);
    }
}

/// WMO weather interpretation codes (Open-Meteo).
public static class WeatherCode
{
    public static string Description(int code) => code switch
    {
        0 => L("Clear"), 1 => L("Mainly clear"), 2 => L("Partly cloudy"), 3 => L("Overcast"),
        45 or 48 => L("Fog"), 51 or 53 or 55 => L("Drizzle"), 56 or 57 => L("Freezing drizzle"),
        61 or 63 => L("Rain"), 65 => L("Heavy rain"), 66 or 67 => L("Freezing rain"),
        71 or 73 => L("Snow"), 75 => L("Heavy snow"), 77 => L("Snow grains"),
        80 or 81 => L("Showers"), 82 => L("Heavy showers"), 85 or 86 => L("Snow showers"),
        95 => L("Thunderstorm"), 96 or 99 => L("Thunderstorm, hail"), _ => "—",
    };

    /// Colour emoji (Segoe UI Emoji on Windows) instead of the Mac's SF Symbols.
    public static string Icon(int code, bool isDay) => code switch
    {
        0 => isDay ? "☀️" : "🌙",
        1 or 2 => isDay ? "⛅" : "☁️",
        3 => "☁️",
        45 or 48 => "🌫️",
        51 or 53 or 55 or 61 or 63 or 80 or 81 => "🌦️",
        56 or 57 or 66 or 67 or 65 or 82 => "🌧️",
        71 or 73 or 75 or 77 or 85 or 86 => "❄️",
        95 or 96 or 99 => "⛈️",
        _ => "☁️",
    };

    /// Background tint per weather category (sky gradient top colour).
    public static string Tint(int code, bool isDay) =>
        !isDay ? "#1B2340" : code switch
        {
            0 or 1 => "#2F6FB8", 2 => "#4A74A8", 45 or 48 => "#5F6670",
            >= 51 and <= 67 or >= 80 and <= 82 => "#3E4E66", >= 71 and <= 86 => "#7C8AA0", >= 95 => "#3A3550", _ => "#5B6578",
        };
}
