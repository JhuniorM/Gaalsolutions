using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gaalsolutions.Monitoring.Services;

namespace Gaalsolutions.Monitoring.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class HostsController : ControllerBase
    {
        private readonly IMonitoringSource _source;

        public HostsController(IMonitoringSource source)
        {
            _source = source;
        }

        [HttpGet]
        public async Task<IActionResult> GetHosts(CancellationToken ct)
        {
            var snapshot = await _source.GetSnapshotAsync(null, ct);

            return Ok(snapshot.Hosts);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetHost(int id, CancellationToken ct)
        {
            var snapshot = await _source.GetSnapshotAsync(null, ct);
            var host = snapshot.Hosts.FirstOrDefault(h => h.Id == id);

            if (host == null)
            {
                return NotFound();
            }

            return Ok(host);
        }
    }
}