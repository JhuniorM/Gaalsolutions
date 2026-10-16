using Gaalsolutions.Monitoring.Models;

namespace Gaalsolutions.Monitoring.Services
{
    // Revisa las alertas cada cierto tiempo y envía un correo por cada problema grave nuevo.
    // Se controla con estos ajustes (User Secrets):
    //   Notifications:Enabled        true/false (por defecto false)
    //   Notifications:MinSeverity    severidad mínima que genera correo (por defecto "Alta")
    //   Notifications:CheckSeconds   cada cuántos segundos se revisa (por defecto 60)
    //   Notifications:MaxAgeHours    ignora problemas más antiguos que esto (por defecto 24)
    //   Notifications:MaxPerCycle    máximo de correos por revisión (por defecto 5)
    public class AlertNotificationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<AlertNotificationWorker> _logger;

        public AlertNotificationWorker(
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            ILogger<AlertNotificationWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue<bool>("Notifications:Enabled"))
            {
                _logger.LogInformation("Notificaciones automáticas desactivadas (Notifications:Enabled).");
                return;
            }

            var checkSeconds = Math.Max(10, _config.GetValue("Notifications:CheckSeconds", 60));

            _logger.LogInformation(
                "Notificaciones automáticas activas: severidad mínima {Severity}, revisión cada {Seconds} s.",
                _config["Notifications:MinSeverity"] ?? "Alta",
                checkSeconds);

            try
            {
                // Pequeña espera para que la aplicación termine de arrancar.
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await RunOnceAsync(stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Error al revisar las alertas para notificar.");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(checkSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // La aplicación se está cerrando.
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            // Cada revisión usa su propio ámbito (la base de datos y la fuente son "scoped").
            using var scope = _scopeFactory.CreateScope();

            var source = scope.ServiceProvider.GetRequiredService<IMonitoringSource>();
            var notifier = scope.ServiceProvider.GetRequiredService<NotificationService>();

            var snapshot = await source.GetSnapshotAsync(null, ct);

            // Si la fuente no respondió, los datos son los últimos conocidos: no se notifica.
            if (!snapshot.IsAvailable)
            {
                _logger.LogDebug("Fuente no disponible; se omite la revisión de alertas.");
                return;
            }

            var minRank = SeverityLevels.Rank(_config["Notifications:MinSeverity"]);

            if (minRank < 0)
            {
                minRank = SeverityLevels.Rank("Alta");
            }

            var maxAgeHours = Math.Max(1, _config.GetValue("Notifications:MaxAgeHours", 24));
            var maxPerCycle = Math.Max(1, _config.GetValue("Notifications:MaxPerCycle", 5));
            var cutoff = DateTime.Now.AddHours(-maxAgeHours);

            var candidates = snapshot.Alerts
                .Where(a => a.Status == "Activa"
                            && SeverityLevels.Rank(a.Severity) >= minRank
                            && a.DateTime >= cutoff)
                .OrderByDescending(a => a.DateTime)
                .ToList();

            var sent = 0;
            var failed = 0;

            foreach (var alert in candidates)
            {
                var eventId = string.IsNullOrWhiteSpace(alert.EventId)
                    ? $"SIM-{alert.Id}"
                    : alert.EventId;

                var result = await notifier.NotifyAsync(eventId, alert.HostName, alert.Severity, alert.Message);

                if (result == NotifyResult.Sent)
                {
                    sent++;
                }
                else if (result == NotifyResult.Failed)
                {
                    failed++;
                }

                if (sent + failed >= maxPerCycle)
                {
                    break;
                }
            }

            if (sent > 0 || failed > 0)
            {
                _logger.LogInformation("Notificaciones: {Sent} enviadas, {Failed} fallidas.", sent, failed);
            }
        }
    }
}