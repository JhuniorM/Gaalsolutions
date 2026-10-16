using Microsoft.AspNetCore.Mvc;
using Gaalsolutions.Monitoring.Models;
using Gaalsolutions.Monitoring.Services;
using Microsoft.AspNetCore.Authorization;

namespace Gaalsolutions.Monitoring.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly IMonitoringSource _source;

        public HomeController(IMonitoringSource source)
        {
            _source = source;
        }

        // Carga inicial de la página completa.
        public async Task<IActionResult> Index(int? hostId, CancellationToken ct)
        {
            return View(await BuildDashboardAsync(hostId, ct));
        }

        // Lo consulta el JavaScript cada 5 segundos para el equipo elegido.
        // Devuelve solo el HTML del Dashboard, sin el layout.
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Live(int? hostId, CancellationToken ct)
        {
            return PartialView("_DashboardLive", await BuildDashboardAsync(hostId, ct));
        }

        private async Task<DashboardViewModel> BuildDashboardAsync(int? hostId, CancellationToken ct)
        {
            var snapshot = await _source.GetSnapshotAsync(hostId, ct);

            return new DashboardViewModel
            {
                SourceName = snapshot.SourceName,
                IsSimulated = snapshot.IsSimulated,
                SourceAvailable = snapshot.IsAvailable,
                SourceMessage = snapshot.Message,

                TotalHosts = snapshot.Hosts.Count,
                AvailableHosts = snapshot.Hosts.Count(h => h.Status == "Disponible"),
                ProblemHosts = snapshot.Hosts.Count(h => h.Status == "Problema"),
                ActiveAlerts = snapshot.Alerts.Count(a => a.Status == "Activa"),

                Hosts = snapshot.Hosts,
                Alerts = snapshot.Alerts,

                SelectedHostId = snapshot.Selected?.Id ?? 0,
                SelectedHostName = snapshot.Selected?.Name ?? "",
                SelectedInterface = snapshot.Selected?.InterfaceName ?? "",
                RxBps = snapshot.Selected?.RxBps,
                TxBps = snapshot.Selected?.TxBps,
                MaxRxBps = snapshot.MaxRxBps,
                MaxTxBps = snapshot.MaxTxBps,
                MaxPeriodLabel = snapshot.MaxPeriodLabel,
                HasTrafficData = snapshot.HasTrafficData,
                TrafficSampleTime = snapshot.TrafficSampleTime
            };
        }
    }
}