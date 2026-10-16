using Gaalsolutions.Monitoring.Models;

namespace Gaalsolutions.Monitoring.Services
{
    // Fuente de datos simulados.
    public class SimulatedMonitoringSource : IMonitoringSource
    {
        private readonly HostService _hostService;
        private readonly AlertService _alertService;

        public string SourceName => "Datos simulados";

        public SimulatedMonitoringSource(HostService hostService, AlertService alertService)
        {
            _hostService = hostService;
            _alertService = alertService;
        }

        public Task<MonitoringSnapshot> GetSnapshotAsync(int? hostId, CancellationToken ct)
        {
            // GetAll() primero: actualiza (simula) los valores de todos los equipos.
            var hosts = _hostService.GetAll();

            var selected =
                (hostId.HasValue ? _hostService.GetById(hostId.Value) : null)
                ?? _hostService.GetDefaultHost()
                ?? hosts.First();

            var max = _hostService.GetMaxTraffic(selected.Id);

            var snapshot = new MonitoringSnapshot
            {
                SourceName = SourceName,
                IsSimulated = true,
                Hosts = hosts,
                Alerts = _alertService.GetAll(),
                Selected = selected,
                MaxRxBps = max.Rx,
                MaxTxBps = max.Tx,
                MaxPeriodLabel = "desde que inició la aplicación",
                TrafficSampleTime = DateTime.Now,
                HasTrafficData = true
            };

            return Task.FromResult(snapshot);
        }

        public Task<List<TrafficPoint>> GetTrafficHistoryAsync(int hostId, int points, CancellationToken ct)
        {
            var history = _hostService
                .GetTrafficHistory(hostId, points)
                .Select(p => new TrafficPoint { Time = p.Time, Rx = p.Rx, Tx = p.Tx })
                .ToList();

            return Task.FromResult(history);
        }

        public Task<TrafficStats> GetTrafficStatsAsync(int hostId, int periodHours, CancellationToken ct)
        {
            var buckets = periodHours <= 24 ? 48 : periodHours <= 168 ? 56 : 60;
            var series = _hostService.GetSimulatedSeries(hostId, periodHours, buckets);

            if (series.Count == 0)
            {
                return Task.FromResult(new TrafficStats { DataKind = SourceName });
            }

            var stats = new TrafficStats
            {
                HasData = true,
                RxMin = series.Min(p => p.Rx),
                RxAvg = series.Average(p => p.Rx),
                RxMax = series.Max(p => p.Rx),
                TxMin = series.Min(p => p.Tx),
                TxAvg = series.Average(p => p.Tx),
                TxMax = series.Max(p => p.Tx),
                Samples = series.Count,
                From = series.First().Time,
                To = series.Last().Time,
                DataKind = SourceName,
                Points = series
                    .Select(p => new TrafficPoint { Time = p.Time, Rx = p.Rx, Tx = p.Tx })
                    .ToList()
            };

            return Task.FromResult(stats);
        }
    }
}