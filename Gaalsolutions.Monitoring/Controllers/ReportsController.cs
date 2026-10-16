using Gaalsolutions.Monitoring.Data;
using Gaalsolutions.Monitoring.Models;
using Gaalsolutions.Monitoring.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gaalsolutions.Monitoring.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        // Periodos permitidos, en horas: 24 h, 7 días y 30 días.
        private static readonly int[] AllowedPeriods = { 24, 168, 720 };

        private readonly IMonitoringSource _source;
        private readonly ApplicationDbContext _context;

        public ReportsController(IMonitoringSource source, ApplicationDbContext context)
        {
            _source = source;
            _context = context;
        }

        public async Task<IActionResult> Index(int? hostId, int periodHours = 24, CancellationToken ct = default)
        {
            if (!AllowedPeriods.Contains(periodHours))
            {
                periodHours = 24;
            }

            var snapshot = await _source.GetSnapshotAsync(hostId, ct);
            var selected = snapshot.Selected;

            var model = new ReportViewModel
            {
                SourceName = snapshot.SourceName,
                SourceAvailable = snapshot.IsAvailable,
                SourceMessage = snapshot.Message,
                Hosts = snapshot.Hosts,
                SelectedHostId = selected?.Id ?? 0,
                SelectedHostName = selected?.Name ?? "",
                PeriodHours = periodHours,
                PeriodLabel = periodHours switch
                {
                    24 => "las últimas 24 horas",
                    168 => "los últimos 7 días",
                    _ => "los últimos 30 días"
                }
            };

            if (selected == null)
            {
                return View(model);
            }

            // Bloque 1: tráfico del equipo
            try
            {
                model.Traffic = await _source.GetTrafficStatsAsync(selected.Id, periodHours, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                model.TrafficError = ex is InvalidOperationException
                    ? ex.Message
                    : ZabbixService.ExplainError(ex);
            }

            // Bloque 2: notificaciones enviadas de ese equipo en el periodo
            var since = DateTime.Now.AddHours(-periodHours);

            model.Notifications = await _context.NotificationHistories
                .Where(n => n.HostName == selected.Name && n.SentAt >= since)
                .OrderByDescending(n => n.SentAt)
                .Take(200)
                .ToListAsync(ct);

            model.NotificationsSent = model.Notifications.Count(n => n.Status == "Enviado");
            model.NotificationsFailed = model.Notifications.Count(n => n.Status == "Fallido");
            model.NotificationsPending = model.Notifications.Count(n => n.Status == "Pendiente");

            return View(model);
        }
    }
}