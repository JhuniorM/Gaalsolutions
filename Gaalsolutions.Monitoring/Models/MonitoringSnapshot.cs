namespace Gaalsolutions.Monitoring.Models
{
    // Todo lo que necesita el Dashboard en una sola consulta, venga de donde venga.
    public class MonitoringSnapshot
    {
        public string SourceName { get; set; } = "";
        public bool IsSimulated { get; set; }

        // false cuando no se pudo consultar la fuente (por ejemplo, Zabbix apagado).
        public bool IsAvailable { get; set; } = true;
        public string Message { get; set; } = "";

        public List<HostModel> Hosts { get; set; } = new List<HostModel>();
        public List<AlertModel> Alerts { get; set; } = new List<AlertModel>();

        // Equipo elegido para el panel de tráfico y su tasa máxima.
        public HostModel? Selected { get; set; }
        public double MaxRxBps { get; set; }
        public double MaxTxBps { get; set; }
        public string MaxPeriodLabel { get; set; } = "";

        // Hora del último dato de tráfico y si es reciente.
        public DateTime? TrafficSampleTime { get; set; }
        public bool HasTrafficData { get; set; } = true;
    }
}