using Gaalsolutions.Monitoring.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gaalsolutions.Monitoring.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public class NotificationController : ControllerBase
    {
        private readonly NotificationService _notificationService;

        public NotificationController(NotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        // Prueba manual del flujo de notificaciones (solo POST y con sesión iniciada).
        // Útil para demostrar el correo aunque las notificaciones automáticas estén desactivadas.
        [HttpPost("test-critical")]
        public async Task<IActionResult> TestCriticalAlert()
        {
            var result = await _notificationService.NotifyAsync(
                eventId: "DEMO-CRITICAL-001",
                hostName: "SW-ACCESS-01",
                severity: "Alta",
                problem: "Uso de CPU superior al 90%."
            );

            var message = result switch
            {
                NotifyResult.Sent => "Alerta enviada y registrada correctamente.",
                NotifyResult.AlreadyNotified => "Este evento ya había sido notificado.",
                NotifyResult.RetryLater => "Hubo intentos fallidos: espera unos minutos antes de reintentar.",
                _ => "No se pudo enviar el correo. Revisa la configuración SMTP (Mailtrap)."
            };

            return Ok(new { message });
        }
    }
}