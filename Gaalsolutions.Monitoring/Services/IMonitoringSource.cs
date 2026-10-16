using Gaalsolutions.Monitoring.Models;

namespace Gaalsolutions.Monitoring.Services
{
    // Origen de los datos de monitoreo. Hay dos implementaciones:
    // datos simulados y Zabbix. Se elige con "Monitoring:Source" en los secretos de usuario.
    public interface IMonitoringSource
    {
        string SourceName { get; }

        Task<MonitoringSnapshot> GetSnapshotAsync(int? hostId, CancellationToken ct);

        Task<List<TrafficPoint>> GetTrafficHistoryAsync(int hostId, int points, CancellationToken ct);

        // Mínimo, media y máximo de tráfico de un equipo en un periodo (para los reportes).
        Task<TrafficStats> GetTrafficStatsAsync(int hostId, int periodHours, CancellationToken ct);
    }
}