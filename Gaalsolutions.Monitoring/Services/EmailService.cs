using Gaalsolutions.Monitoring.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Gaalsolutions.Monitoring.Services
{
    public class EmailService
    {
        private readonly EmailSettings _settings;

        public EmailService(IOptions<EmailSettings> options)
        {
            _settings = options.Value;
        }

        public async Task SendAlertAsync(
            string hostName,
            string severity,
            string problem)
        {
            var message = new MimeMessage();

            message.From.Add(new MailboxAddress(
                _settings.SenderName,
                _settings.SenderEmail));

            message.To.Add(MailboxAddress.Parse(
                _settings.AdminEmail));

            message.Subject =
                $"[GAALSOLUTIONS] Alerta {severity}: {hostName}";

            message.Body = new TextPart("plain")
            {
                Text =
                    $"Equipo: {hostName}\n" +
                    $"Severidad: {severity}\n" +
                    $"Problema: {problem}\n" +
                    $"Fecha: {DateTime.Now:dd/MM/yyyy HH:mm:ss}"
            };

            using var client = new SmtpClient();

            await client.ConnectAsync(
                _settings.Host,
                _settings.Port,
                SecureSocketOptions.StartTls);

            await client.AuthenticateAsync(
                _settings.Username,
                _settings.Password);

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}