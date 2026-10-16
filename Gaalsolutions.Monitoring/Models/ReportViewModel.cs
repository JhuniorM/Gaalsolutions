namespace Gaalsolutions.Monitoring.Models
{
    public class ReportViewModel
    {
        public string SourceName { get; set; } = "";
        public bool SourceAvailable { get; set; } = true;
        public string SourceMessage { get; set; } = "";

        // Filtros
        public List<HostModel> Hosts { get; set; } = new List<HostModel>();
        public int SelectedHostId { get; set; }
        public string SelectedHostName { get; set; } = "";
        public int PeriodHours { get; set; } = 24;
        public string PeriodLabel { get; set; } = "";

        // Bloque 1: tráfico del equipo en el periodo
        public TrafficStats? Traffic { get; set; }
        public string TrafficError { get; set; } = "";

        // Bloque 2: notificaciones enviadas (datos propios de la aplicación)
        public List<NotificationHistoryModel> Notifications { get; set; } = new List<NotificationHistoryModel>();
        public int NotificationsSent { get; set; }
        public int NotificationsFailed { get; set; }
        public int NotificationsPending { get; set; }
    }
}