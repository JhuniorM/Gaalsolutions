using Gaalsolutions.Monitoring.Models;

namespace Gaalsolutions.Monitoring.Services
{
    public class AlertService
    {
        private readonly List<AlertModel> _alerts = new List<AlertModel>
        {
            new AlertModel
            {
                Id = 1,
                HostName = "SW-CORE-01",
                Severity = "Advertencia",
                Message = "Uso de memoria superior al 80%",
                DateTime = DateTime.Now.AddMinutes(-18),
                Status = "Activa"
            },

            new AlertModel
            {
                Id = 2,
                HostName = "SW-ACCESS-01",
                Severity = "Alta",
                Message = "Uso de CPU superior al 85%",
                DateTime = DateTime.Now.AddMinutes(-42),
                Status = "Activa"
            },

            new AlertModel
            {
                Id = 3,
                HostName = "SRV-FILES01",
                Severity = "Media",
                Message = "Espacio disponible en disco por debajo del umbral",
                DateTime = DateTime.Now.AddHours(-2),
                Status = "Resuelta"
            }
        };

        public List<AlertModel> GetAll()
        {
            return _alerts;
        }

        public List<AlertModel> GetActive()
        {
            return _alerts
                .Where(a => a.Status == "Activa")
                .ToList();
        }

        public AlertModel? GetById(int id)
        {
            return _alerts.FirstOrDefault(a => a.Id == id);
        }
    }
}