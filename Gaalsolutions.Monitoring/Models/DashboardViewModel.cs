namespace Gaalsolutions.Monitoring.Models
{
    public class DashboardViewModel
    {
        // Fuente de los datos y su estado
        public string SourceName { get; set; } = "";
        public bool IsSimulated { get; set; }
        public bool SourceAvailable { get; set; } = true;
        public string SourceMessage { get; set; } = "";

        public int TotalHosts { get; set; }
        public int AvailableHosts { get; set; }
        public int ProblemHosts { get; set; }
        public int ActiveAlerts { get; set; }

        public List<HostModel> Hosts { get; set; } = new List<HostModel>();
        public List<AlertModel> Alerts { get; set; } = new List<AlertModel>();

        // Dispositivo elegido para el panel de tráfico (todo en bits por segundo, bps)
        public int SelectedHostId { get; set; }
        public string SelectedHostName { get; set; } = "";
        public string SelectedInterface { get; set; } = "";
        public double? RxBps { get; set; }
        public double? TxBps { get; set; }
        public double MaxRxBps { get; set; }
        public double MaxTxBps { get; set; }
        public string MaxPeriodLabel { get; set; } = "";
        public bool HasTrafficData { get; set; } = true;
        public DateTime? TrafficSampleTime { get; set; }
    }
}