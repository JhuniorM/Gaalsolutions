namespace Gaalsolutions.Monitoring.Models
{
    public class NotificationHistoryModel
    {
        public int Id { get; set; }

        public string EventId { get; set; } = "";

        public string HostName { get; set; } = "";

        public string Severity { get; set; } = "";

        public string Problem { get; set; } = "";

        public string Recipient { get; set; } = "";

        public DateTime SentAt { get; set; }

        public string Status { get; set; } = "";
    }
}