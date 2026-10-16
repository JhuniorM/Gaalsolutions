namespace Gaalsolutions.Monitoring.Models
{
    public class HostModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string Status { get; set; } = "";

        // Los valores son null cuando no hay dato (por ejemplo, Zabbix apagado
        // o el equipo no tiene ese item). La vista muestra "—".
        public double? CpuUsage { get; set; }
        public double? MemoryUsage { get; set; }

        // Interfaz de red cuyo tráfico se muestra.
        public string InterfaceName { get; set; } = "";

        // Tráfico actual en bits por segundo (bps).
        // RX = entrada / descarga, TX = salida / subida.
        public double? RxBps { get; set; }
        public double? TxBps { get; set; }
    }
}