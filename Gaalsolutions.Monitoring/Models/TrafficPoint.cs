namespace Gaalsolutions.Monitoring.Models
{
    public class TrafficPoint
    {
        public DateTime Time { get; set; }
        public double Rx { get; set; }
        public double Tx { get; set; }
    }

    public static class TrafficLabel
    {
        // Hora para la gráfica: solo la hora si es de hoy; con fecha si es de otro día.
        public static string Format(DateTime time)
        {
            return time.Date == DateTime.Today
                ? time.ToString("HH:mm:ss")
                : time.ToString("dd/MM HH:mm");
        }
    }
}