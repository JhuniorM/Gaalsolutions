using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gaalsolutions.Monitoring.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Gaalsolutions.Monitoring.Services
{
    // Fuente de datos reales: consulta la API de Zabbix.
    // Si Zabbix no responde, devuelve el último dato conocido con un aviso, sin romper el Dashboard.
    public class ZabbixMonitoringSource : IMonitoringSource
    {
        private const string FailureKey = "zabbix:failure";
        private const string LastGoodKey = "zabbix:lastgood";

        // Claves de tráfico sin parámetros extra: net.if.in["..."] y net.if.out["..."].
        // Se excluyen las variantes con coma (errors, dropped, etc.).
        private static readonly Regex TrafficKeyRegex =
            new Regex(@"^net\.if\.(in|out)\[[^,\]]*\]$", RegexOptions.Compiled);

        private static readonly Regex TrailingParenthesis =
            new Regex(@"\(([^()]*)\)\s*$", RegexOptions.Compiled);

        private static readonly HashSet<string> CpuKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system.cpu.util", "system.cpu.util[]"
        };

        private static readonly HashSet<string> MemoryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "vm.memory.utilization", "vm.memory.util", "vm.memory.utilization[]", "vm.memory.util[]"
        };

        private readonly ZabbixService _zabbix;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;

        public string SourceName => "Zabbix";

        public ZabbixMonitoringSource(ZabbixService zabbix, IMemoryCache cache, IConfiguration config)
        {
            _zabbix = zabbix;
            _cache = cache;
            _config = config;
        }

        // Un dato se considera "reciente" si es de los últimos N minutos.
        private int FreshnessMinutes => ReadInt("Monitoring:FreshnessMinutes", 15);

        // Ventana de la gráfica y de la tasa máxima.
        private int WindowMinutes => ReadInt("Monitoring:TrafficWindowMinutes", 60);

        private string DefaultHostName
        {
            get
            {
                var name = _config["Monitoring:DefaultHost"];
                return string.IsNullOrWhiteSpace(name) ? "PC-Cliente-Windows" : name;
            }
        }

        private int ReadInt(string key, int fallback)
        {
            return int.TryParse(_config[key], out var value) && value > 0 ? value : fallback;
        }

        // ---------- Clases internas ----------

        private class InterfaceItems
        {
            public string Label { get; set; } = "";
            public string? RxId { get; set; }
            public string RxType { get; set; } = "3";
            public string? TxId { get; set; }
            public string TxType { get; set; } = "3";
        }

        private class HostItems
        {
            public string Interface { get; set; } = "";
            public string? RxId { get; set; }
            public string RxType { get; set; } = "3";
            public string? TxId { get; set; }
            public string TxType { get; set; } = "3";
            public string? CpuId { get; set; }
            public string CpuType { get; set; } = "0";
            public string? MemId { get; set; }
            public string MemType { get; set; } = "0";
        }

        // ---------- Atajos a los ayudantes de ZabbixService ----------

        private static string Str(JsonElement element, string name) => ZabbixService.Str(element, name);

        private static List<JsonElement> ArrayItems(JsonElement element) => ZabbixService.ArrayItems(element);

        private static List<JsonElement> ArrayProp(JsonElement element, string name) => ZabbixService.ArrayProp(element, name);

        private static DateTime FromUnix(string seconds)
        {
            return long.TryParse(seconds, out var value) && value > 0
                ? DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime
                : DateTime.MinValue;
        }

        private static string SeverityName(int severity)
        {
            switch (severity)
            {
                case 1: return "Información";
                case 2: return "Advertencia";
                case 3: return "Media";
                case 4: return "Alta";
                case 5: return "Crítica";
                default: return "Sin clasificar";
            }
        }

        // ---------- API pública ----------

        public async Task<MonitoringSnapshot> GetSnapshotAsync(int? hostId, CancellationToken ct)
        {
            // Si Zabbix falló hace poco, no se vuelve a intentar durante 30 s
            // (evita que cada actualización espere el tiempo de espera completo).
            if (_cache.TryGetValue(FailureKey, out string? failure) && failure != null)
            {
                return Fallback(failure);
            }

            try
            {
                var cacheKey = $"zabbix:snapshot:{(hostId.HasValue ? hostId.Value.ToString() : "default")}";

                if (!_cache.TryGetValue(cacheKey, out MonitoringSnapshot? snapshot) || snapshot == null)
                {
                    snapshot = await BuildSnapshotAsync(hostId, ct);
                    _cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(10));
                }

                _cache.Set(LastGoodKey, snapshot);

                return snapshot;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var message = ZabbixService.ExplainError(ex);
                _cache.Set(FailureKey, message, TimeSpan.FromSeconds(30));

                return Fallback(message);
            }
        }

        public async Task<List<TrafficPoint>> GetTrafficHistoryAsync(int hostId, int points, CancellationToken ct)
        {
            if (_cache.TryGetValue(FailureKey, out string? failure) && failure != null)
            {
                return new List<TrafficPoint>();
            }

            try
            {
                var zabbixHostId = hostId.ToString();
                var map = await GetItemMapAsync(new List<string> { zabbixHostId }, ct);
                var series = await GetTrafficSeriesAsync(zabbixHostId, map, ct);

                return series.TakeLast(points).ToList();
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Zabbix apagado o sin respuesta: la gráfica queda vacía.
                return new List<TrafficPoint>();
            }
        }

        // Mínimo, media y máximo de tráfico en un periodo (para los reportes).
        // Hasta 24 h se usa el historial completo; para periodos largos, las tendencias
        // de Zabbix (promedios por hora con mínimo y máximo).
        public async Task<TrafficStats> GetTrafficStatsAsync(int hostId, int periodHours, CancellationToken ct)
        {
            if (_cache.TryGetValue(FailureKey, out string? failure) && failure != null)
            {
                throw new InvalidOperationException("No se pudo consultar Zabbix: " + failure);
            }

            var zabbixHostId = hostId.ToString();
            var map = await GetItemMapAsync(new List<string> { zabbixHostId }, ct);

            if (!map.TryGetValue(zabbixHostId, out var items) || items.RxId == null || items.TxId == null)
            {
                return new TrafficStats { DataKind = "Zabbix" };
            }

            var from = DateTimeOffset.UtcNow.AddHours(-periodHours).ToUnixTimeSeconds();

            if (periodHours <= 24)
            {
                var rx = await ReadHistoryAsync(items.RxId, items.RxType, from, ct);
                var tx = await ReadHistoryAsync(items.TxId, items.TxType, from, ct);

                if (rx.Count == 0 || tx.Count == 0)
                {
                    return new TrafficStats { DataKind = "Historial de Zabbix" };
                }

                return new TrafficStats
                {
                    HasData = true,
                    RxMin = rx.Min(v => v.Value),
                    RxAvg = rx.Average(v => v.Value),
                    RxMax = rx.Max(v => v.Value),
                    TxMin = tx.Min(v => v.Value),
                    TxAvg = tx.Average(v => v.Value),
                    TxMax = tx.Max(v => v.Value),
                    Samples = rx.Count,
                    From = rx.First().Time,
                    To = rx.Last().Time,
                    DataKind = "Historial completo de Zabbix",
                    Points = Downsample(PairByTime(rx, tx, 90), 96)
                };
            }

            var rxTrends = await ReadTrendsAsync(items.RxId, from, ct);
            var txTrends = await ReadTrendsAsync(items.TxId, from, ct);

            if (rxTrends.Count == 0 || txTrends.Count == 0)
            {
                return new TrafficStats { DataKind = "Tendencias de Zabbix (promedios por hora)" };
            }

            var rxSamples = rxTrends.Sum(t => t.Num);
            var txSamples = txTrends.Sum(t => t.Num);

            return new TrafficStats
            {
                HasData = true,
                RxMin = rxTrends.Min(t => t.Min),
                RxAvg = rxSamples > 0 ? rxTrends.Sum(t => t.Avg * t.Num) / rxSamples : rxTrends.Average(t => t.Avg),
                RxMax = rxTrends.Max(t => t.Max),
                TxMin = txTrends.Min(t => t.Min),
                TxAvg = txSamples > 0 ? txTrends.Sum(t => t.Avg * t.Num) / txSamples : txTrends.Average(t => t.Avg),
                TxMax = txTrends.Max(t => t.Max),
                Samples = rxSamples,
                From = rxTrends.First().Time,
                To = rxTrends.Last().Time,
                DataKind = "Tendencias de Zabbix (promedios por hora)",
                Points = Downsample(
                    PairByTime(
                        rxTrends.Select(t => (Time: t.Time, Value: t.Avg)).ToList(),
                        txTrends.Select(t => (Time: t.Time, Value: t.Avg)).ToList(),
                        90),
                    120)
            };
        }

        // Muestras de un item desde una fecha, ordenadas por hora.
        private async Task<List<(DateTime Time, double Value)>> ReadHistoryAsync(
            string itemId,
            string valueType,
            long fromUnix,
            CancellationToken ct)
        {
            var list = new List<(DateTime Time, double Value)>();

            if (!int.TryParse(valueType, out var historyType))
            {
                return list;
            }

            var rows = await _zabbix.CallApiAsync(
                "history.get",
                new
                {
                    output = "extend",
                    history = historyType,
                    itemids = itemId,
                    time_from = fromUnix,
                    sortfield = "clock",
                    sortorder = "DESC",
                    limit = 5000
                },
                ct);

            foreach (var row in ArrayItems(rows))
            {
                if (double.TryParse(Str(row, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    var time = FromUnix(Str(row, "clock"));

                    if (time != DateTime.MinValue)
                    {
                        list.Add((time, value));
                    }
                }
            }

            list.Sort((a, b) => a.Time.CompareTo(b.Time));

            return list;
        }

        // Tendencias por hora de un item: mínimo, promedio, máximo y número de muestras.
        private async Task<List<(DateTime Time, double Min, double Avg, double Max, int Num)>> ReadTrendsAsync(
            string itemId,
            long fromUnix,
            CancellationToken ct)
        {
            var list = new List<(DateTime Time, double Min, double Avg, double Max, int Num)>();

            var rows = await _zabbix.CallApiAsync(
                "trend.get",
                new
                {
                    output = new[] { "itemid", "clock", "num", "value_min", "value_avg", "value_max" },
                    itemids = itemId,
                    time_from = fromUnix,
                    limit = 2000
                },
                ct);

            foreach (var row in ArrayItems(rows))
            {
                var time = FromUnix(Str(row, "clock"));

                if (time == DateTime.MinValue
                    || !double.TryParse(Str(row, "value_min"), NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
                    || !double.TryParse(Str(row, "value_avg"), NumberStyles.Float, CultureInfo.InvariantCulture, out var avg)
                    || !double.TryParse(Str(row, "value_max"), NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
                {
                    continue;
                }

                int.TryParse(Str(row, "num"), out var num);

                list.Add((time, min, avg, max, num));
            }

            list.Sort((a, b) => a.Time.CompareTo(b.Time));

            return list;
        }

        // Empareja cada valor de entrada con el de salida más cercano en el tiempo.
        // Ambas listas deben venir ordenadas por hora.
        private static List<TrafficPoint> PairByTime(
            List<(DateTime Time, double Value)> rx,
            List<(DateTime Time, double Value)> tx,
            double toleranceSeconds)
        {
            var result = new List<TrafficPoint>();

            if (tx.Count == 0)
            {
                return result;
            }

            var index = 0;

            foreach (var r in rx)
            {
                while (index < tx.Count - 1
                       && Math.Abs((tx[index + 1].Time - r.Time).TotalSeconds)
                          <= Math.Abs((tx[index].Time - r.Time).TotalSeconds))
                {
                    index++;
                }

                if (Math.Abs((tx[index].Time - r.Time).TotalSeconds) <= toleranceSeconds)
                {
                    result.Add(new TrafficPoint { Time = r.Time, Rx = r.Value, Tx = tx[index].Value });
                }
            }

            return result;
        }

        // Agrupa los puntos para que la gráfica no tenga demasiados (promedio por grupo).
        private static List<TrafficPoint> Downsample(List<TrafficPoint> points, int maxPoints)
        {
            if (points.Count <= maxPoints)
            {
                return points;
            }

            var size = (int)Math.Ceiling(points.Count / (double)maxPoints);
            var result = new List<TrafficPoint>();

            for (var i = 0; i < points.Count; i += size)
            {
                var chunk = points.Skip(i).Take(size).ToList();

                result.Add(new TrafficPoint
                {
                    Time = chunk[chunk.Count / 2].Time,
                    Rx = chunk.Average(p => p.Rx),
                    Tx = chunk.Average(p => p.Tx)
                });
            }

            return result;
        }

        // ---------- Construcción de la consulta ----------

        private async Task<MonitoringSnapshot> BuildSnapshotAsync(int? hostId, CancellationToken ct)
        {
            var snapshot = new MonitoringSnapshot
            {
                SourceName = SourceName,
                IsSimulated = false,
                MaxPeriodLabel = $"últimos {WindowMinutes} min"
            };

            // 1. Equipos monitoreados
            var hostsResult = await _zabbix.CallApiAsync(
                "host.get",
                new
                {
                    output = new[] { "hostid", "host", "name", "status" },
                    filter = new { status = 0 },
                    selectInterfaces = new[] { "ip", "dns", "useip", "type", "available" },
                    selectParentTemplates = new[] { "name" },
                    limit = 200
                },
                ct);

            var rawHosts = ArrayItems(hostsResult);

            if (rawHosts.Count == 0)
            {
                snapshot.HasTrafficData = false;
                snapshot.Message = "Zabbix no devolvió ningún equipo. Revisa los permisos del usuario del token.";
                return snapshot;
            }

            var hostIds = rawHosts.Select(h => Str(h, "hostid")).ToList();

            // 2. Items de cada equipo y sus últimos valores
            var itemMap = await GetItemMapAsync(hostIds, ct);
            var latest = await GetLatestValuesAsync(itemMap, ct);

            // 3. Problemas (si falla, el resto del Dashboard sigue funcionando)
            var unresolvedSeverity = new Dictionary<string, int>();

            try
            {
                var eventsResult = await _zabbix.CallApiAsync(
                    "event.get",
                    new
                    {
                        output = "extend",
                        selectHosts = new[] { "hostid", "name" },
                        source = 0,
                        @object = 0,
                        value = 1,
                        sortfield = new[] { "clock", "eventid" },
                        sortorder = "DESC",
                        limit = 50
                    },
                    ct);

                foreach (var e in ArrayItems(eventsResult))
                {
                    var severity = int.TryParse(Str(e, "severity"), out var parsed) ? parsed : 0;
                    var recoveryId = Str(e, "r_eventid");
                    var resolved = recoveryId != "" && recoveryId != "0";
                    var eventHosts = ArrayProp(e, "hosts");

                    long.TryParse(Str(e, "eventid"), out var eventId);

                    snapshot.Alerts.Add(new AlertModel
                    {
                        Id = (int)(eventId % int.MaxValue),
                        EventId = "ZBX-" + Str(e, "eventid"),
                        HostName = eventHosts.Count > 0 ? Str(eventHosts[0], "name") : "—",
                        Severity = SeverityName(severity),
                        Message = Str(e, "name"),
                        DateTime = FromUnix(Str(e, "clock")),
                        Status = resolved ? "Resuelta" : "Activa"
                    });

                    if (!resolved)
                    {
                        foreach (var eventHost in eventHosts)
                        {
                            var zabbixId = Str(eventHost, "hostid");
                            unresolvedSeverity[zabbixId] = Math.Max(
                                unresolvedSeverity.TryGetValue(zabbixId, out var current) ? current : 0,
                                severity);
                        }
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                snapshot.Message = "No se pudieron leer las alertas de Zabbix: " + ZabbixService.ExplainError(ex);
            }

            // 4. Modelo de cada equipo
            double? ValueOf(string? itemId)
            {
                return itemId != null && latest.TryGetValue(itemId, out var found)
                    ? (double?)found.Value
                    : null;
            }

            foreach (var h in rawHosts)
            {
                var zabbixId = Str(h, "hostid");

                if (!int.TryParse(zabbixId, out var id))
                {
                    continue;
                }

                var interfaces = ArrayProp(h, "interfaces");

                var ip = interfaces
                    .Select(i => Str(i, "useip") == "0" ? Str(i, "dns") : Str(i, "ip"))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "—";

                var interfaceUnavailable = interfaces.Any(i => Str(i, "available") == "2");

                var template = ArrayProp(h, "parentTemplates")
                    .Select(t => Str(t, "name"))
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "—";

                itemMap.TryGetValue(zabbixId, out var items);

                var name = Str(h, "name");

                var model = new HostModel
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? Str(h, "host") : name,
                    Type = template,
                    IpAddress = ip,
                    InterfaceName = items?.Interface ?? "",
                    CpuUsage = ValueOf(items?.CpuId),
                    MemoryUsage = ValueOf(items?.MemId),
                    RxBps = ValueOf(items?.RxId),
                    TxBps = ValueOf(items?.TxId)
                };

                var hasProblem = unresolvedSeverity.TryGetValue(zabbixId, out var worst);

                model.Status = interfaceUnavailable || worst >= 4
                    ? "Problema"
                    : hasProblem ? "Alerta" : "Disponible";

                snapshot.Hosts.Add(model);
            }

            // 5. Equipo elegido, su serie de tráfico y su tasa máxima
            var selected =
                (hostId.HasValue ? snapshot.Hosts.FirstOrDefault(x => x.Id == hostId.Value) : null)
                ?? snapshot.Hosts.FirstOrDefault(x => x.Name.Equals(DefaultHostName, StringComparison.OrdinalIgnoreCase))
                ?? snapshot.Hosts.FirstOrDefault();

            snapshot.Selected = selected;
            snapshot.HasTrafficData = false;

            if (selected != null)
            {
                var selectedZabbixId = selected.Id.ToString();

                var series = await GetTrafficSeriesAsync(selectedZabbixId, itemMap, ct);

                snapshot.MaxRxBps = series.Count > 0 ? series.Max(p => p.Rx) : 0;
                snapshot.MaxTxBps = series.Count > 0 ? series.Max(p => p.Tx) : 0;

                if (itemMap.TryGetValue(selectedZabbixId, out var selectedItems)
                    && selectedItems.RxId != null
                    && latest.TryGetValue(selectedItems.RxId, out var lastRx))
                {
                    snapshot.TrafficSampleTime = lastRx.Time;
                    snapshot.HasTrafficData = true;
                }
            }

            return snapshot;
        }

        // Descubre qué items usar para cada equipo (se guarda 10 minutos).
        private async Task<Dictionary<string, HostItems>> GetItemMapAsync(List<string> hostIds, CancellationToken ct)
        {
            var cacheKey = "zabbix:items:" + string.Join(",", hostIds);

            if (_cache.TryGetValue(cacheKey, out Dictionary<string, HostItems>? cached) && cached != null)
            {
                return cached;
            }

            var result = await _zabbix.CallApiAsync(
                "item.get",
                new
                {
                    output = new[] { "itemid", "hostid", "name", "key_", "value_type" },
                    hostids = hostIds,
                    search = new Dictionary<string, string[]>
                    {
                        ["key_"] = new[] { "net.if.in[", "net.if.out[", "system.cpu.util", "vm.memory.util" }
                    },
                    searchByAny = true,
                    sortfield = "name",
                    limit = 1000
                },
                ct);

            var map = new Dictionary<string, HostItems>();
            var interfacesByHost = new Dictionary<string, List<InterfaceItems>>();

            foreach (var item in ArrayItems(result))
            {
                var hostId = Str(item, "hostid");
                var id = Str(item, "itemid");
                var key = Str(item, "key_");
                var name = Str(item, "name");
                var valueType = Str(item, "value_type");

                if (!map.TryGetValue(hostId, out var hostItems))
                {
                    hostItems = new HostItems();
                    map[hostId] = hostItems;
                    interfacesByHost[hostId] = new List<InterfaceItems>();
                }

                var trafficMatch = TrafficKeyRegex.Match(key);

                if (trafficMatch.Success)
                {
                    // Se omite la interfaz de loopback
                    if (name.Contains("loopback", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("Interface lo:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var label = InterfaceLabel(name);
                    var entry = interfacesByHost[hostId].FirstOrDefault(x => x.Label == label);

                    if (entry == null)
                    {
                        entry = new InterfaceItems { Label = label };
                        interfacesByHost[hostId].Add(entry);
                    }

                    if (trafficMatch.Groups[1].Value == "in")
                    {
                        entry.RxId = id;
                        entry.RxType = valueType;
                    }
                    else
                    {
                        entry.TxId = id;
                        entry.TxType = valueType;
                    }
                }
                else if (CpuKeys.Contains(key) && hostItems.CpuId == null)
                {
                    hostItems.CpuId = id;
                    hostItems.CpuType = valueType;
                }
                else if (MemoryKeys.Contains(key) && hostItems.MemId == null)
                {
                    hostItems.MemId = id;
                    hostItems.MemType = valueType;
                }
            }

            // Por equipo se elige la primera interfaz que tenga entrada y salida.
            foreach (var pair in interfacesByHost)
            {
                var complete = pair.Value
                    .OrderBy(x => x.Label)
                    .FirstOrDefault(x => x.RxId != null && x.TxId != null);

                if (complete != null)
                {
                    var hostItems = map[pair.Key];
                    hostItems.RxId = complete.RxId;
                    hostItems.RxType = complete.RxType;
                    hostItems.TxId = complete.TxId;
                    hostItems.TxType = complete.TxType;
                    hostItems.Interface = ShortLabel(complete.Label);
                }
            }

            _cache.Set(cacheKey, map, TimeSpan.FromMinutes(10));

            return map;
        }

        // "Interface Intel(R) ... (Ethernet0): Bits received" -> "Interface Intel(R) ... (Ethernet0)"
        private static string InterfaceLabel(string itemName)
        {
            var index = itemName.LastIndexOf(": ", StringComparison.Ordinal);
            return index > 0 ? itemName.Substring(0, index) : itemName;
        }

        // "Interface Intel(R) ... (Ethernet0)" -> "Ethernet0";  "Interface eth0" -> "eth0"
        private static string ShortLabel(string label)
        {
            var match = TrailingParenthesis.Match(label);

            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                return match.Groups[1].Value;
            }

            return label.StartsWith("Interface ", StringComparison.OrdinalIgnoreCase)
                ? label.Substring("Interface ".Length)
                : label;
        }

        // Último valor reciente de cada item. Se usa history.get porque item.get
        // no entrega el último valor de forma confiable en esta instalación.
        private async Task<Dictionary<string, (double Value, DateTime Time)>> GetLatestValuesAsync(
            Dictionary<string, HostItems> map,
            CancellationToken ct)
        {
            var idsByType = new Dictionary<string, List<string>>();

            void AddId(string? itemId, string valueType)
            {
                if (itemId == null)
                {
                    return;
                }

                if (!idsByType.TryGetValue(valueType, out var list))
                {
                    list = new List<string>();
                    idsByType[valueType] = list;
                }

                list.Add(itemId);
            }

            foreach (var items in map.Values)
            {
                AddId(items.RxId, items.RxType);
                AddId(items.TxId, items.TxType);
                AddId(items.CpuId, items.CpuType);
                AddId(items.MemId, items.MemType);
            }

            var latest = new Dictionary<string, (double Value, DateTime Time)>();
            var from = DateTimeOffset.UtcNow.AddMinutes(-FreshnessMinutes).ToUnixTimeSeconds();

            foreach (var pair in idsByType)
            {
                if (!int.TryParse(pair.Key, out var historyType))
                {
                    continue;
                }

                var rows = await _zabbix.CallApiAsync(
                    "history.get",
                    new
                    {
                        output = "extend",
                        history = historyType,
                        itemids = pair.Value,
                        time_from = from,
                        sortfield = "clock",
                        sortorder = "DESC",
                        limit = Math.Min(2000, pair.Value.Count * 20)
                    },
                    ct);

                foreach (var row in ArrayItems(rows))
                {
                    var itemId = Str(row, "itemid");

                    if (latest.ContainsKey(itemId))
                    {
                        continue;
                    }

                    if (!double.TryParse(Str(row, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        continue;
                    }

                    var time = FromUnix(Str(row, "clock"));

                    if (time == DateTime.MinValue)
                    {
                        continue;
                    }

                    latest[itemId] = (value, time);
                }
            }

            return latest;
        }

        // Serie de tráfico (entrada y salida emparejadas por hora) de la última ventana. Se guarda 30 s.
        private async Task<List<TrafficPoint>> GetTrafficSeriesAsync(
            string zabbixHostId,
            Dictionary<string, HostItems> map,
            CancellationToken ct)
        {
            if (!map.TryGetValue(zabbixHostId, out var items) || items.RxId == null || items.TxId == null)
            {
                return new List<TrafficPoint>();
            }

            var cacheKey = $"zabbix:series:{zabbixHostId}";

            if (_cache.TryGetValue(cacheKey, out List<TrafficPoint>? cached) && cached != null)
            {
                return cached;
            }

            var from = DateTimeOffset.UtcNow.AddMinutes(-WindowMinutes).ToUnixTimeSeconds();

            async Task<List<(DateTime Time, double Value)>> ReadAsync(string itemId, string valueType)
            {
                var list = new List<(DateTime Time, double Value)>();

                if (!int.TryParse(valueType, out var historyType))
                {
                    return list;
                }

                var rows = await _zabbix.CallApiAsync(
                    "history.get",
                    new
                    {
                        output = "extend",
                        history = historyType,
                        itemids = itemId,
                        time_from = from,
                        sortfield = "clock",
                        sortorder = "ASC",
                        limit = 1000
                    },
                    ct);

                foreach (var row in ArrayItems(rows))
                {
                    if (double.TryParse(Str(row, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        var time = FromUnix(Str(row, "clock"));

                        if (time != DateTime.MinValue)
                        {
                            list.Add((time, value));
                        }
                    }
                }

                return list;
            }

            var rx = await ReadAsync(items.RxId, items.RxType);
            var tx = await ReadAsync(items.TxId, items.TxType);

            var series = new List<TrafficPoint>();

            foreach (var r in rx)
            {
                // Valor de salida más cercano en el tiempo (hasta 90 s de diferencia)
                var txValue = 0.0;
                var bestDiff = double.MaxValue;

                foreach (var t in tx)
                {
                    var diff = Math.Abs((t.Time - r.Time).TotalSeconds);

                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        txValue = t.Value;
                    }
                }

                if (bestDiff <= 90)
                {
                    series.Add(new TrafficPoint { Time = r.Time, Rx = r.Value, Tx = txValue });
                }
            }

            _cache.Set(cacheKey, series, TimeSpan.FromSeconds(30));

            return series;
        }

        // Cuando Zabbix no responde: último dato conocido (si existe) con un aviso.
        private MonitoringSnapshot Fallback(string reason)
        {
            var message = $"No se pudo consultar Zabbix: {reason}";

            if (_cache.TryGetValue(LastGoodKey, out MonitoringSnapshot? last) && last != null)
            {
                return new MonitoringSnapshot
                {
                    SourceName = SourceName,
                    IsSimulated = false,
                    IsAvailable = false,
                    Message = message + " Se muestra el último dato conocido.",
                    Hosts = last.Hosts,
                    Alerts = last.Alerts,
                    Selected = last.Selected,
                    MaxRxBps = last.MaxRxBps,
                    MaxTxBps = last.MaxTxBps,
                    MaxPeriodLabel = last.MaxPeriodLabel,
                    TrafficSampleTime = last.TrafficSampleTime,
                    HasTrafficData = last.HasTrafficData
                };
            }

            return new MonitoringSnapshot
            {
                SourceName = SourceName,
                IsSimulated = false,
                IsAvailable = false,
                Message = message,
                HasTrafficData = false,
                MaxPeriodLabel = $"últimos {WindowMinutes} min"
            };
        }
    }
}