namespace Gaalsolutions.Monitoring.Models
{
    // Resumen del tráfico de un equipo en un periodo (todo en bits por segundo, bps).
    public class TrafficStats
    {
        public bool HasData { get; set; }

        public double RxMin { get; set; }
        public double RxAvg { get; set; }
        public double RxMax { get; set; }

        public double TxMin { get; set; }
        public double TxAvg { get; set; }
        public double TxMax { get; set; }

        // Muestras usadas y rango de tiempo cubierto.
        public int Samples { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        // De dónde salen los datos (historial completo, promedios por hora, simulados...).
        public string DataKind { get; set; } = "";

        // Puntos para la gráfica (ya agrupados para que no sean demasiados).
        public List<TrafficPoint> Points { get; set; } = new List<TrafficPoint>();
    }
}