using Gaalsolutions.Monitoring.Models;
using Gaalsolutions.Monitoring.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gaalsolutions.Monitoring.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/metrics")]
    public class MetricsController : ControllerBase
    {
        private const int MaxPoints = 30;

        private readonly IMonitoringSource _source;

        public MetricsController(IMonitoringSource source)
        {
            _source = source;
        }

        // Historial reciente de tráfico (bps) de un equipo, para rellenar la gráfica.
        // GET /api/metrics/traffic/{hostId}/history
        [HttpGet("traffic/{hostId:int}/history")]
        public async Task<IActionResult> GetTrafficHistory(int hostId, CancellationToken ct)
        {
            var history = await _source.GetTrafficHistoryAsync(hostId, MaxPoints, ct);

            var points = history
                .Select(p => new
                {
                    time = TrafficLabel.Format(p.Time),
                    rx = p.Rx,
                    tx = p.Tx
                })
                .ToList();

            return Ok(new { points });
        }
    }
}