namespace Gaalsolutions.Monitoring.Models
{
    public class AlertModel
    {
        public int Id { get; set; }

        // Identificador estable del evento (en Zabbix: "ZBX-" + id del evento).
        // Sirve para no enviar dos veces el correo del mismo problema.
        public string EventId { get; set; } = "";

        public string HostName { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime DateTime { get; set; }
        public string Status { get; set; } = "";
    }
}