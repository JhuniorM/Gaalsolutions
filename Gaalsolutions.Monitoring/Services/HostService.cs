using Gaalsolutions.Monitoring.Models;

namespace Gaalsolutions.Monitoring.Services
{
    public class HostService
    {
        private readonly object _lock = new object();

        // Equipo que se muestra al abrir el Dashboard.
        public const string DefaultHostName = "PC-Cliente-Windows";

        // Valores base de tráfico: sobre ellos se simulan las variaciones.
        private readonly Dictionary<int, (double Rx, double Tx)> _baseTraffic;

        // Tasa máxima observada por equipo (en bps).
        // SIMULADO: máximo desde que inició la aplicación.
        private readonly Dictionary<int, (double Rx, double Tx)> _maxTraffic;

        public HostService()
        {
            _baseTraffic = _hosts.ToDictionary(h => h.Id, h => (h.RxBps ?? 0, h.TxBps ?? 0));
            _maxTraffic = _hosts.ToDictionary(h => h.Id, h => (h.RxBps ?? 0, h.TxBps ?? 0));
        }

        // SIMULADO: los nombres de interfaz y el tráfico son de ejemplo,
        // excepto PC-Cliente-Windows, basado en la captura de Zabbix.
        private readonly List<HostModel> _hosts = new List<HostModel>
        {
            new HostModel
            {
                Id = 1,
                Name = "SRV-DC01",
                Type = "Windows Server",
                IpAddress = "192.168.10.10",
                Status = "Disponible",
                CpuUsage = 34,
                MemoryUsage = 58,
                InterfaceName = "Ethernet0",
                RxBps = 2_400_000,
                TxBps = 650_000
            },

            new HostModel
            {
                Id = 2,
                Name = "SRV-ZABBIX",
                Type = "Ubuntu Server",
                IpAddress = "192.168.10.20",
                Status = "Disponible",
                CpuUsage = 27,
                MemoryUsage = 46,
                InterfaceName = "ens33",
                RxBps = 1_800_000,
                TxBps = 480_000
            },

            new HostModel
            {
                Id = 3,
                Name = "RTR-GAAL-01",
                Type = "Router",
                IpAddress = "192.168.1.1",
                Status = "Disponible",
                CpuUsage = 21,
                MemoryUsage = 39,
                InterfaceName = "GigabitEthernet0/0",
                RxBps = 9_870_000,
                TxBps = 899_000
            },

            new HostModel
            {
                Id = 4,
                Name = "SW-CORE-01",
                Type = "Switch",
                IpAddress = "192.168.1.2",
                Status = "Alerta",
                CpuUsage = 76,
                MemoryUsage = 81,
                InterfaceName = "GigabitEthernet1/0/1",
                RxBps = 14_500_000,
                TxBps = 1_200_000
            },

            new HostModel
            {
                Id = 5,
                Name = "SRV-FILES01",
                Type = "File Server",
                IpAddress = "192.168.10.30",
                Status = "Disponible",
                CpuUsage = 42,
                MemoryUsage = 67,
                InterfaceName = "Ethernet0",
                RxBps = 6_300_000,
                TxBps = 1_900_000
            },

            new HostModel
            {
                Id = 6,
                Name = "SW-ACCESS-01",
                Type = "Switch",
                IpAddress = "192.168.20.2",
                Status = "Problema",
                CpuUsage = 89,
                MemoryUsage = 74,
                InterfaceName = "GigabitEthernet0/1",
                RxBps = 3_900_000,
                TxBps = 410_000
            },

            new HostModel
            {
                Id = 7,
                Name = "PC-Cliente-Windows",
                Type = "Cliente Windows",
                IpAddress = "192.168.10.50",
                Status = "Disponible",
                CpuUsage = 18,
                MemoryUsage = 52,
                InterfaceName = "Ethernet0",
                RxBps = 664_520,
                TxBps = 16_230
            }
        };

        // SIMULADO: cada consulta mueve un poco los valores para que el
        // Dashboard se vea "en vivo". Con Zabbix, este método desaparece y
        // GetAll() devolverá los valores reales.
        private void SimulateChanges()
        {
            foreach (var host in _hosts)
            {
                host.CpuUsage = Vary(host.CpuUsage ?? 0, 4, 5, 98);
                host.MemoryUsage = Vary(host.MemoryUsage ?? 0, 2, 20, 95);

                // El tráfico varía +/- 30% alrededor de su valor base.
                host.RxBps = Math.Round(_baseTraffic[host.Id].Rx * Jitter(0.3));
                host.TxBps = Math.Round(_baseTraffic[host.Id].Tx * Jitter(0.3));

                var max = _maxTraffic[host.Id];
                _maxTraffic[host.Id] = (
                    Math.Max(max.Rx, host.RxBps ?? 0),
                    Math.Max(max.Tx, host.TxBps ?? 0));
            }
        }

        private static double Jitter(double amount)
        {
            var factor = 1 + (Random.Shared.NextDouble() * 2 - 1) * amount;

            // De vez en cuando simula un pico de tráfico (entre x2 y x3).
            if (Random.Shared.NextDouble() < 0.05)
            {
                factor *= 2 + Random.Shared.NextDouble();
            }

            return factor;
        }

        private static double Vary(double value, double step, double min, double max)
        {
            var next = value + (Random.Shared.NextDouble() * 2 - 1) * step;
            return Math.Round(Math.Clamp(next, min, max), 1);
        }

        public List<HostModel> GetAll()
        {
            lock (_lock)
            {
                SimulateChanges();
                return _hosts;
            }
        }

        // SIMULADO: serie de tráfico para los reportes (un punto por intervalo en el periodo).
        public List<(DateTime Time, double Rx, double Tx)> GetSimulatedSeries(int hostId, int periodHours, int buckets)
        {
            lock (_lock)
            {
                var result = new List<(DateTime Time, double Rx, double Tx)>();

                if (!_baseTraffic.TryGetValue(hostId, out var baseValues))
                {
                    return result;
                }

                var now = DateTime.Now;
                var step = TimeSpan.FromHours(periodHours) / buckets;

                for (int i = buckets - 1; i >= 0; i--)
                {
                    result.Add((
                        now - step * i,
                        Math.Round(baseValues.Rx * Jitter(0.3)),
                        Math.Round(baseValues.Tx * Jitter(0.3))));
                }

                return result;
            }
        }

        public HostModel? GetById(int id)
        {
            return _hosts.FirstOrDefault(h => h.Id == id);
        }

        public HostModel? GetDefaultHost()
        {
            return _hosts.FirstOrDefault(h => h.Name == DefaultHostName);
        }

        // Tasa máxima observada del equipo (bps).
        public (double Rx, double Tx) GetMaxTraffic(int hostId)
        {
            lock (_lock)
            {
                return _maxTraffic.TryGetValue(hostId, out var max) ? max : (0, 0);
            }
        }

        // SIMULADO: últimos puntos de tráfico del equipo, uno cada 5 segundos.
        // Con Zabbix se reemplazará por los datos históricos reales recientes.
        public List<(DateTime Time, double Rx, double Tx)> GetTrafficHistory(int hostId, int points)
        {
            lock (_lock)
            {
                var result = new List<(DateTime Time, double Rx, double Tx)>();

                var host = _hosts.FirstOrDefault(h => h.Id == hostId);
                if (host == null)
                {
                    return result;
                }

                var now = DateTime.Now;
                var baseValues = _baseTraffic[hostId];

                for (int i = points - 1; i >= 1; i--)
                {
                    result.Add((
                        now.AddSeconds(-5 * i),
                        Math.Round(baseValues.Rx * Jitter(0.3)),
                        Math.Round(baseValues.Tx * Jitter(0.3))));
                }

                // El último punto es el valor actual del equipo.
                result.Add((now, host.RxBps ?? 0, host.TxBps ?? 0));

                return result;
            }
        }
    }
}