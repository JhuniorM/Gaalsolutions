using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gaalsolutions.Monitoring.Services;

namespace Gaalsolutions.Monitoring.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AlertsController : ControllerBase
    {
        private readonly IMonitoringSource _source;

        public AlertsController(IMonitoringSource source)
        {
            _source = source;
        }

        [HttpGet]
        public async Task<IActionResult> GetAlerts(CancellationToken ct)
        {
            var snapshot = await _source.GetSnapshotAsync(null, ct);

            return Ok(snapshot.Alerts);
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActiveAlerts(CancellationToken ct)
        {
            var snapshot = await _source.GetSnapshotAsync(null, ct);

            return Ok(snapshot.Alerts.Where(a => a.Status == "Activa").ToList());
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAlert(int id, CancellationToken ct)
        {
            var snapshot = await _source.GetSnapshotAsync(null, ct);
            var alert = snapshot.Alerts.FirstOrDefault(a => a.Id == id);

            if (alert == null)
            {
                return NotFound();
            }

            return Ok(alert);
        }
    }
}