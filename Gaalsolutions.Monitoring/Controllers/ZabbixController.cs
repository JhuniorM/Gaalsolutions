using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Gaalsolutions.Monitoring.Services;

namespace Gaalsolutions.Monitoring.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ZabbixController : ControllerBase
    {
        private readonly ZabbixService _zabbixService;

        public ZabbixController(ZabbixService zabbixService)
        {
            _zabbixService = zabbixService;
        }

        [HttpGet("version")]
        public async Task<IActionResult> GetVersion()
        {
            try
            {
                var version = await _zabbixService.GetApiVersionAsync();

                if (version == null)
                {
                    return BadRequest(new
                    {
                        message = "La URL de la API de Zabbix no está configurada."
                    });
                }

                return Ok(new
                {
                    version = version
                });
            }
            catch (Exception ex)
            {
                return StatusCode(502, new
                {
                    message = "No se pudo conectar con Zabbix. Abre /api/zabbix/diagnostics para ver qué falla.",
                    detail = ex.Message
                });
            }
        }

        // Diagnóstico paso a paso de la conexión con Zabbix.
        // Uso: /api/zabbix/diagnostics  o  /api/zabbix/diagnostics?hostName=OTRO-EQUIPO
        // Añadir &json=true para ver el resultado en JSON.
        [HttpGet("diagnostics")]
        public async Task<IActionResult> Diagnostics(
            [FromQuery] string hostName = "PC-Cliente-Windows",
            [FromQuery] bool json = false,
            CancellationToken ct = default)
        {
            var result = await _zabbixService.RunDiagnosticsAsync(hostName, ct);

            if (json)
            {
                return Ok(result);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var sb = new StringBuilder();

            sb.AppendLine($"DIAGNÓSTICO DE ZABBIX  ({DateTime.Now:dd/MM/yyyy HH:mm:ss})");
            sb.AppendLine($"Resultado general: {(result.Ok ? "TODO OK" : "HAY PROBLEMAS")}");
            sb.AppendLine();

            foreach (var step in result.Steps)
            {
                sb.AppendLine($"[{(step.Ok ? "OK" : "FALLO")}] {step.Name}");
                sb.AppendLine($"      {step.Detail}");

                if (step.Data != null)
                {
                    var dataJson = JsonSerializer.Serialize(step.Data, options);

                    foreach (var line in dataJson.Split('\n'))
                    {
                        sb.AppendLine("      " + line.TrimEnd('\r'));
                    }
                }

                sb.AppendLine();
            }

            return Content(sb.ToString(), "text/plain; charset=utf-8");
        }
    }
}