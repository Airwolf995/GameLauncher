using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace GameLauncher.Services
{
    /// <summary>
    /// Liest die Sensorwerte aus einer laufenden LibreHardwareMonitor-Anwendung.
    ///
    /// Die Anwendung stellt ihre Messwerte ueber einen eingebauten Webserver als
    /// <c>data.json</c> bereit; einen WMI-Anbieter besitzt sie im Gegensatz zum
    /// Vorgaenger OpenHardwareMonitor nicht. Sie laeuft mit erhoehten Rechten
    /// und bringt den noetigen Sensortreiber mit, weshalb der Launcher selbst
    /// weder Administratorrechte noch eine eigene Sensorbibliothek braucht.
    ///
    /// Der Webserver ist in den Optionen der Anwendung abschaltbar und
    /// standardmaessig aus. Ist er nicht erreichbar, liefert die Quelle
    /// <c>null</c> - die Anzeige bleibt dann leer, statt falsche Werte zu zeigen.
    /// </summary>
    internal sealed class LibreHardwareMonitorWebSource : IHardwareTelemetrySource
    {
        public const int DefaultPort = 8085;

        /// <summary>
        /// Die Version, gegen die diese Quelle geprueft ist.
        ///
        /// Bewusst festgelegt statt auf die neueste Fassung zu verweisen: die
        /// Anwendung hat ihre Schnittstellen schon einmal ohne Vorwarnung
        /// ausgetauscht - bis 0.9.4 gab es einen WMI-Anbieter, danach nur noch
        /// den Webserver. Ein fester Verweis sorgt dafuer, dass Benutzer eine
        /// nachweislich passende Fassung erhalten und eine neue Version erst
        /// geprueft wird, bevor sie empfohlen wird.
        /// </summary>
        public const string SupportedVersion = "v0.9.6";

        public const string DownloadUrl =
            "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/" + SupportedVersion;

        private static readonly TimeSpan CacheInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Laeuft die Anwendung nicht, schlaegt jede Abfrage fehl. Ohne diese
        /// Pause versuchte es der Launcher bei jedem Overlay-Durchlauf erneut.
        /// </summary>
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

        private const string TemperatureType = "Temperature";
        private const string LoadType = "Load";
        private const string SmallDataType = "SmallData";

        private readonly HttpClient _httpClient;
        private readonly SensorUpdateThrottle _cacheThrottle;
        private readonly Func<DateTime> _utcNow;
        private readonly string _requestUri;
        private readonly object _sync = new();

        private float? _cpuTemperature;
        private float? _gpuTemperature;
        private float? _gpuLoad;
        private float? _gpuMemoryTotalGb;
        private DateTime? _skipUntilUtc;
        private bool _loggedUnavailable;
        private bool _disposed;

        public LibreHardwareMonitorWebSource()
            : this(DefaultPort, null)
        {
        }

        internal LibreHardwareMonitorWebSource(int port, Func<DateTime>? utcNow)
        {
            _requestUri = BuildRequestUri(port);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _cacheThrottle = new SensorUpdateThrottle(CacheInterval, _utcNow);
            _httpClient = new HttpClient { Timeout = RequestTimeout };
        }

        private static string BuildRequestUri(int port) =>
            $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}/data.json";

        /// <summary>
        /// Meldet, ob die Anwendung erreichbar ist. Dient dem Hinweis in den
        /// Einstellungen.
        /// </summary>
        public static bool IsApplicationAvailable()
        {
            try
            {
                using var probe = new HttpClient { Timeout = RequestTimeout };
                using HttpResponseMessage response = probe
                    .GetAsync(BuildRequestUri(DefaultPort), HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public float? TryReadCpuTemperature() => ReadCached(() => _cpuTemperature);

        public float? TryReadGpuTemperature() => ReadCached(() => _gpuTemperature);

        public float? TryReadGpuLoad() => ReadCached(() => _gpuLoad);

        public float? TryReadGpuMemoryTotalGb() => ReadCached(() => _gpuMemoryTotalGb);

        private float? ReadCached(Func<float?> selector)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return null;
                }

                RefreshIfDue();
                return selector();
            }
        }

        private void RefreshIfDue()
        {
            if (_skipUntilUtc.HasValue && _utcNow() < _skipUntilUtc.Value)
            {
                return;
            }

            if (!_cacheThrottle.ShouldUpdate())
            {
                return;
            }

            try
            {
                Refresh();
                _skipUntilUtc = null;

                // Nach einer erfolgreichen Verbindung darf ein spaeterer Ausfall
                // wieder gemeldet werden. Ohne dieses Zuruecksetzen bliebe jeder
                // weitere Ausfall stumm, weil die Meldung als bereits abgesetzt
                // gilt - protokolliert wird so jeder Wechsel von erreichbar zu
                // nicht erreichbar, nicht jeder einzelne Fehlversuch.
                _loggedUnavailable = false;
            }
            catch (Exception ex)
            {
                Clear();
                _skipUntilUtc = _utcNow().Add(RetryInterval);
                LogUnavailableOnce(ex);
            }
        }

        private void Clear()
        {
            _cpuTemperature = null;
            _gpuTemperature = null;
            _gpuLoad = null;
            _gpuMemoryTotalGb = null;
        }

        private void Refresh()
        {
            string payload = _httpClient
                .GetStringAsync(_requestUri)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            var readings = new List<Reading>();
            using (JsonDocument document = JsonDocument.Parse(payload))
            {
                CollectReadings(document.RootElement, readings);
            }

            _cpuTemperature = SelectCpuTemperature(readings);
            _gpuTemperature = SelectGpuTemperature(readings);
            _gpuLoad = SelectGpuLoad(readings);
            _gpuMemoryTotalGb = SelectGpuMemoryTotalGb(readings);
        }

        /// <summary>
        /// Die Antwort ist ein Baum aus Rechner, Geraeten und Sensorgruppen.
        /// Nur die Blaetter tragen eine <c>SensorId</c>.
        /// </summary>
        private static void CollectReadings(JsonElement element, List<Reading> readings)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (element.TryGetProperty("SensorId", out JsonElement sensorId)
                && sensorId.ValueKind == JsonValueKind.String)
            {
                string identifier = sensorId.GetString() ?? string.Empty;
                string name = element.TryGetProperty("Text", out JsonElement text)
                    ? text.GetString() ?? string.Empty
                    : string.Empty;
                string type = element.TryGetProperty("Type", out JsonElement typeElement)
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;
                string? rawValue = element.TryGetProperty("Value", out JsonElement value)
                    ? value.GetString()
                    : null;

                if (TryParseValue(rawValue, out float parsedValue))
                {
                    readings.Add(new Reading(name, identifier, type, parsedValue));
                }
            }

            if (element.TryGetProperty("Children", out JsonElement children)
                && children.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement child in children.EnumerateArray())
                {
                    CollectReadings(child, readings);
                }
            }
        }

        /// <summary>
        /// Die Werte kommen als Text mit Einheit und in der Zahlenschreibweise
        /// der Anwendung, etwa "59,0 °C" oder "16376.0 MB". Gelesen wird
        /// deshalb nur der fuehrende Zahlenteil; das Trennzeichen kann Komma
        /// oder Punkt sein.
        /// </summary>
        internal static bool TryParseValue(string? rawValue, out float value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            var number = new StringBuilder();
            foreach (char character in rawValue.Trim())
            {
                if (char.IsDigit(character) || character == '-' || character == '+')
                {
                    number.Append(character);
                    continue;
                }

                if (character == ',' || character == '.')
                {
                    number.Append('.');
                    continue;
                }

                break;
            }

            return float.TryParse(
                number.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        /// <summary>
        /// Der Prozessor wird ueber die Kennung ausgewaehlt, nicht ueber den
        /// Namen: der Baum enthaelt auch Mainboard-, Speicher- und
        /// Laufwerkssensoren, deren Bezeichnungen sich ueberschneiden.
        /// </summary>
        private static float? SelectCpuTemperature(IReadOnlyList<Reading> readings)
        {
            float? packageTemperature = null;
            float? maximumTemperature = null;

            foreach (Reading reading in readings)
            {
                if (!reading.IsType(TemperatureType)
                    || !TemperatureSensorSelection.IsPlausibleTemperature(reading.Value)
                    || !TemperatureSensorSelection.IsCpuIdentifier(reading.Identifier))
                {
                    continue;
                }

                if (!packageTemperature.HasValue
                    && reading.HasName(TemperatureSensorSelection.CpuPackageSensorName))
                {
                    packageTemperature = reading.Value;
                }

                if (!maximumTemperature.HasValue || reading.Value > maximumTemperature.Value)
                {
                    maximumTemperature = reading.Value;
                }
            }

            return packageTemperature ?? maximumTemperature;
        }

        private static float? SelectGpuTemperature(IReadOnlyList<Reading> readings)
        {
            float? coreTemperature = null;
            float? anyTemperature = null;

            foreach (Reading reading in readings)
            {
                if (!reading.IsType(TemperatureType)
                    || !TemperatureSensorSelection.IsPlausibleTemperature(reading.Value)
                    || !TemperatureSensorSelection.IsGpuIdentifier(reading.Identifier))
                {
                    continue;
                }

                if (reading.HasName(TemperatureSensorSelection.GpuCoreSensorName))
                {
                    return reading.Value;
                }

                if (TemperatureSensorSelection.IsGpuCoreSensor(reading.Name)
                    && (!coreTemperature.HasValue || reading.Value > coreTemperature.Value))
                {
                    coreTemperature = reading.Value;
                }

                if (!anyTemperature.HasValue || reading.Value > anyTemperature.Value)
                {
                    anyTemperature = reading.Value;
                }
            }

            return coreTemperature ?? anyTemperature;
        }

        /// <summary>
        /// Der Wert 0 ist hier gueltig: im Leerlauf ist null Prozent die
        /// richtige Antwort und darf nicht als "kein Messwert" gelten.
        /// </summary>
        private static float? SelectGpuLoad(IReadOnlyList<Reading> readings)
        {
            foreach (Reading reading in readings)
            {
                if (reading.IsType(LoadType)
                    && reading.Value >= 0
                    && TemperatureSensorSelection.IsGpuIdentifier(reading.Identifier)
                    && reading.HasName(TemperatureSensorSelection.GpuCoreSensorName))
                {
                    return reading.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Der Gesamtspeicher der Grafikkarte wird in Megabyte gemeldet.
        /// </summary>
        private static float? SelectGpuMemoryTotalGb(IReadOnlyList<Reading> readings)
        {
            foreach (Reading reading in readings)
            {
                if (reading.IsType(SmallDataType)
                    && reading.Value > 0
                    && TemperatureSensorSelection.IsGpuIdentifier(reading.Identifier)
                    && reading.HasName("GPU Memory Total"))
                {
                    return reading.Value / 1024f;
                }
            }

            return null;
        }

        private void LogUnavailableOnce(Exception ex)
        {
            if (_loggedUnavailable)
            {
                return;
            }

            _loggedUnavailable = true;
            Models.Logger.Warning(
                "LibreHardwareMonitor ist als Datenquelle nicht erreichbar "
                + $"({ex.GetType().Name}). Die Anwendung muss laufen und ihren Webserver "
                + $"auf Port {DefaultPort.ToString(CultureInfo.InvariantCulture)} aktiviert haben.");
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _httpClient.Dispose();
            }
        }

        private readonly record struct Reading(string Name, string Identifier, string SensorType, float Value)
        {
            public bool IsType(string sensorType) =>
                string.Equals(SensorType, sensorType, StringComparison.OrdinalIgnoreCase);

            public bool HasName(string sensorName) =>
                string.Equals(Name, sensorName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
