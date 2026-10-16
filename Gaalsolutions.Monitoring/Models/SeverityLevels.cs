namespace Gaalsolutions.Monitoring.Models
{
    // Orden de las severidades, de menor a mayor (equivale a la escala de Zabbix 0 a 5).
    public static class SeverityLevels
    {
        private static readonly Dictionary<string, int> Ranks =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sin clasificar"] = 0,
                ["Información"] = 1,
                ["Advertencia"] = 2,
                ["Media"] = 3,
                ["Alta"] = 4,
                ["Crítica"] = 5
            };

        // Devuelve -1 si el nombre no es una severidad conocida.
        public static int Rank(string? severity)
        {
            return severity != null && Ranks.TryGetValue(severity.Trim(), out var rank) ? rank : -1;
        }
    }
}