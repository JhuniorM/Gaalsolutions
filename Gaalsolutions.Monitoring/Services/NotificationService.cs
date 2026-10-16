using Gaalsolutions.Monitoring.Data;
using Gaalsolutions.Monitoring.Models;
using Microsoft.EntityFrameworkCore;

namespace Gaalsolutions.Monitoring.Services
{
    public enum NotifyResult
    {
        Sent,              // Correo enviado y registrado
        AlreadyNotified,   // Este evento ya se había notificado
        RetryLater,        // Hubo intentos fallidos: se espera antes de reintentar o se superó el máximo
        Failed             // El envío falló en este intento
    }

    public class NotificationService
    {
        // Intentos máximos por evento y espera entre intentos fallidos.
        private const int MaxAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

        private readonly ApplicationDbContext _context;
        private readonly EmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            ApplicationDbContext context,
            EmailService emailService,
            IConfiguration configuration,
            ILogger<NotificationService> logger)
        {
            _context = context;
            _emailService = emailService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<NotifyResult> NotifyAsync(
            string eventId,
            string hostName,
            string severity,
            string problem)
        {
            // Evitar repetir una notificación enviada.
            bool alreadySent = await _context.NotificationHistories
                .AnyAsync(n =>
                    n.EventId == eventId &&
                    n.Status == "Enviado");

            if (alreadySent)
            {
                return NotifyResult.AlreadyNotified;
            }

            // Intentos anteriores que no llegaron a enviarse (fallidos o pendientes).
            var previousAttempts = await _context.NotificationHistories
                .Where(n => n.EventId == eventId)
                .Select(n => n.SentAt)
                .ToListAsync();

            if (previousAttempts.Count >= MaxAttempts)
            {
                return NotifyResult.RetryLater;
            }

            if (previousAttempts.Count > 0 &&
                DateTime.Now - previousAttempts.Max() < RetryDelay)
            {
                return NotifyResult.RetryLater;
            }

            string recipient =
                _configuration["EmailSettings:AdminEmail"] ?? "";

            var notification = new NotificationHistoryModel
            {
                EventId = eventId,
                HostName = hostName,
                Severity = severity,
                Problem = problem,
                Recipient = recipient,
                SentAt = DateTime.Now,
                Status = "Pendiente"
            };

            _context.NotificationHistories.Add(notification);
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendAlertAsync(
                    hostName,
                    severity,
                    problem);

                notification.Status = "Enviado";
                notification.SentAt = DateTime.Now;

                await _context.SaveChangesAsync();

                return NotifyResult.Sent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo enviar el correo del evento {EventId}.", eventId);

                notification.Status = "Fallido";

                await _context.SaveChangesAsync();

                return NotifyResult.Failed;
            }
        }
    }
}