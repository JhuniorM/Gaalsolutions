using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace Gaalsolutions.Monitoring.Services
{
    // Error devuelto por la propia API de Zabbix (por ejemplo, token inválido).
    public class ZabbixApiException : Exception
    {
        public string? ApiData { get; }

        public ZabbixApiException(string message, string? apiData)
            : base(message)
        {
            ApiData = apiData;
        }
    }

    public class DiagnosticStep
    {
        public string Name { get; set; } = "";
        public bool Ok { get; set; }
        public string Detail { get; set; } = "";
        public object? Data { get; set; }
    }

    public class ZabbixDiagnosticsResult
    {
        public List<DiagnosticStep> Steps { get; } = new List<DiagnosticStep>();

        public bool Ok => Steps.Count > 0 && Steps.All(s => s.Ok);
    }

    public class ZabbixService
    {
        private const int TimeoutSeconds = 10;

        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public ZabbixService(
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<string?> GetApiVersionAsync()
        {
            var apiUrl = _configuration["Zabbix:ApiUrl"];

            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                return null;
            }

            var result = await CallAsync("apiinfo.version", null, false, CancellationToken.None);

            return result.GetString();
        }

        // Llamada autenticada (con el token) a cualquier método de la API de Zabbix.
        public Task<JsonElement> CallApiAsync(string method, object? parameters, CancellationToken ct)
        {
            return CallAsync(method, parameters, true, ct);
        }

        // Llamada genérica JSON-RPC a la API de Zabbix.
        // apiinfo.version no lleva autenticación; el resto usa el token (encabezado Bearer).
        private async Task<JsonElement> CallAsync(
            string method,
            object? parameters,
            bool authenticated,
            CancellationToken ct)
        {
            var apiUrl = _configuration["Zabbix:ApiUrl"];

            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                throw new InvalidOperationException("Zabbix:ApiUrl no está configurada.");
            }

            var body = new
            {
                jsonrpc = "2.0",
                method = method,
                @params = parameters ?? new { },
                id = 1
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = JsonContent.Create(body, new MediaTypeHeaderValue("application/json-rpc"))
            };

            if (authenticated)
            {
                var token = _configuration["Zabbix:ApiToken"];

                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException("Zabbix:ApiToken no está configurado.");
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            // Tiempo máximo por llamada, para no esperar 100 s si no hay conexión.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            using var response = await _httpClient.SendAsync(request, cts.Token);

            response.EnsureSuccessStatusCode();

            var text = await response.Content.ReadAsStringAsync(cts.Token);

            JsonDocument doc;

            try
            {
                doc = JsonDocument.Parse(text);
            }
            catch (JsonException)
            {
                throw new InvalidDataException(
                    "La respuesta no es JSON-RPC (¿ruta incorrecta, página de login o redirección a https?).");
            }

            using (doc)
            {
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var m) ? m.ToString() : "Error";
                    var data = error.TryGetProperty("data", out var d) ? d.ToString() : null;

                    throw new ZabbixApiException(message, data);
                }

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result))
                {
                    return result.Clone();
                }

                throw new InvalidDataException("La respuesta de Zabbix no contiene 'result'.");
            }
        }

        // Prueba paso a paso la conexión con Zabbix y descubre los items de tráfico REALES del equipo.
        public async Task<ZabbixDiagnosticsResult> RunDiagnosticsAsync(string hostName, CancellationToken ct)
        {
            var result = new ZabbixDiagnosticsResult();

            var apiUrl = _configuration["Zabbix:ApiUrl"];
            var hasToken = !string.IsNullOrWhiteSpace(_configuration["Zabbix:ApiToken"]);

            void Add(string name, bool ok, string detail, object? data = null)
            {
                result.Steps.Add(new DiagnosticStep { Name = name, Ok = ok, Detail = detail, Data = data });
            }

            // Ejecuta una llamada y, si falla, agrega el paso con una explicación.
            async Task<JsonElement?> TryCall(string stepName, string method, object? parameters, bool authenticated)
            {
                try
                {
                    return await CallAsync(method, parameters, authenticated, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Add(stepName, false, ExplainError(ex));
                    return null;
                }
            }

            // 1. Configuración
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                Add("1. Configuración", false, "Falta Zabbix:ApiUrl en los secretos de usuario.");
                return result;
            }

            Add("1. Configuración", true,
                $"URL: {apiUrl} | Token: {(hasToken ? "configurado" : "NO configurado")}");

            // 2. Conexión (no requiere token)
            var version = await TryCall("2. Conexión (apiinfo.version)", "apiinfo.version", null, false);

            if (version == null)
            {
                return result;
            }

            Add("2. Conexión (apiinfo.version)", true, $"La API responde. Versión de Zabbix: {version.Value}");

            if (!hasToken)
            {
                Add("3. Autenticación", false,
                    "La conexión funciona, pero falta Zabbix:ApiToken en los secretos de usuario.");
                return result;
            }

            // 3. Autenticación y permisos: lista de equipos visibles
            var hosts = await TryCall(
                "3. Autenticación (host.get)",
                "host.get",
                new { output = new[] { "hostid", "host", "name" }, limit = 200 },
                true);

            if (hosts == null)
            {
                return result;
            }

            var hostList = ArrayItems(hosts.Value)
                .Select(h => new { id = Str(h, "hostid"), host = Str(h, "host"), name = Str(h, "name") })
                .ToList();

            if (hostList.Count == 0)
            {
                Add("3. Autenticación (host.get)", false,
                    "El token es válido, pero no ve ningún equipo. El usuario del token necesita permiso de lectura sobre los grupos de hosts.");
                return result;
            }

            Add("3. Autenticación (host.get)", true,
                $"Token válido. Equipos visibles: {hostList.Count}",
                hostList.Select(h => h.name).ToList());

            // 4. Equipo buscado
            var target = hostList.FirstOrDefault(h =>
                string.Equals(h.host, hostName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(h.name, hostName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                Add("4. Equipo buscado", false,
                    $"No se encontró el equipo '{hostName}'. Prueba con ?hostName=NOMBRE usando uno de los equipos visibles.");
                return result;
            }

            Add("4. Equipo buscado", true, $"Equipo '{hostName}' encontrado (hostid = {target.id}).");

            // 5. Items de tráfico: se buscan por nombre ("Bits") o por clave ("net.if").
            var items = await TryCall(
                "5. Items de tráfico (item.get)",
                "item.get",
                new
                {
                    output = new[] { "itemid", "name", "key_", "units", "lastvalue", "lastclock", "delay", "value_type", "state", "status" },
                    hostids = target.id,
                    search = new Dictionary<string, string> { ["name"] = "Bits", ["key_"] = "net.if" },
                    searchByAny = true,
                    sortfield = "name",
                    limit = 50
                },
                true);

            if (items == null)
            {
                return result;
            }

            var itemList = ArrayItems(items.Value)
                .Select(i => new
                {
                    id = Str(i, "itemid"),
                    name = Str(i, "name"),
                    key = Str(i, "key_"),
                    units = Str(i, "units"),
                    lastValue = Str(i, "lastvalue"),
                    lastTime = FormatClock(Str(i, "lastclock")),
                    interval = Str(i, "delay"),
                    valueType = Str(i, "value_type"),
                    state = Str(i, "state")
                })
                .ToList();

            if (itemList.Count == 0)
            {
                Add("5. Items de tráfico (item.get)", false,
                    "No se encontró ningún item con 'Bits' en el nombre ni 'net.if' en la clave. Revisa en Zabbix cómo se llaman los items de red de ese equipo.");
                return result;
            }

            Add("5. Items de tráfico (item.get)", true,
                $"{itemList.Count} items encontrados (copia estos nombres y claves: los usaremos para el Dashboard).",
                itemList);

            // 6. Historial del item de descarga (RX)
            var rxItem = itemList.FirstOrDefault(i =>
                                 i.name.Contains("received", StringComparison.OrdinalIgnoreCase) ||
                                 i.key.StartsWith("net.if.in", StringComparison.OrdinalIgnoreCase))
                             ?? itemList.First();

            var historyType = int.TryParse(rxItem.valueType, out var vt) ? vt : 0;

            var history = await TryCall(
                "6. Historial (history.get)",
                "history.get",
                new
                {
                    output = "extend",
                    history = historyType,
                    itemids = rxItem.id,
                    sortfield = "clock",
                    sortorder = "DESC",
                    limit = 5
                },
                true);

            if (history != null)
            {
                var points = ArrayItems(history.Value)
                    .Select(p => new { time = FormatClock(Str(p, "clock")), value = Str(p, "value") })
                    .ToList();

                Add("6. Historial (history.get)", points.Count > 0,
                    points.Count > 0
                        ? $"Últimos {points.Count} valores de '{rxItem.name}' (unidad del item: '{rxItem.units}')."
                        : $"Sin datos recientes en el historial de '{rxItem.name}'.",
                    points);
            }

            // 7. Problemas recientes
            var problems = await TryCall(
                "7. Problemas recientes (problem.get)",
                "problem.get",
                new
                {
                    output = new[] { "eventid", "name", "severity", "clock", "acknowledged" },
                    recent = true,
                    sortfield = new[] { "eventid" },
                    sortorder = "DESC",
                    limit = 5
                },
                true);

            if (problems != null)
            {
                var problemList = ArrayItems(problems.Value)
                    .Select(p => new
                    {
                        eventId = Str(p, "eventid"),
                        name = Str(p, "name"),
                        severity = Str(p, "severity"),
                        time = FormatClock(Str(p, "clock"))
                    })
                    .ToList();

                Add("7. Problemas recientes (problem.get)", true,
                    $"{problemList.Count} problemas recientes.",
                    problemList);
            }

            // 8. Otros items útiles para la tabla de dispositivos: CPU, memoria y disponibilidad
            var others = await TryCall(
                "8. Items de CPU, memoria y disponibilidad (item.get)",
                "item.get",
                new
                {
                    output = new[] { "itemid", "name", "key_", "units", "delay", "value_type" },
                    hostids = target.id,
                    search = new Dictionary<string, string[]>
                    {
                        ["key_"] = new[] { "system.cpu", "vm.memory", "agent.ping", "zabbix[host", "icmpping" }
                    },
                    searchByAny = true,
                    sortfield = "name",
                    limit = 50
                },
                true);

            if (others != null)
            {
                var otherList = ArrayItems(others.Value)
                    .Select(i => new
                    {
                        id = Str(i, "itemid"),
                        name = Str(i, "name"),
                        key = Str(i, "key_"),
                        units = Str(i, "units"),
                        interval = Str(i, "delay"),
                        valueType = Str(i, "value_type")
                    })
                    .ToList();

                Add("8. Items de CPU, memoria y disponibilidad (item.get)", true,
                    otherList.Count > 0
                        ? $"{otherList.Count} items encontrados."
                        : "No se encontró ningún item de CPU, memoria o disponibilidad (se buscó por clave).",
                    otherList);
            }

            // 9. Estado de las interfaces, plantillas y grupos del equipo
            var hostDetail = await TryCall(
                "9. Interfaces y plantillas del equipo (host.get)",
                "host.get",
                new
                {
                    output = new[] { "hostid", "host", "name", "status" },
                    hostids = target.id,
                    selectInterfaces = new[] { "type", "ip", "dns", "port", "available", "error" },
                    selectParentTemplates = new[] { "name" },
                    selectHostGroups = new[] { "name" }
                },
                true);

            if (hostDetail != null)
            {
                var detail = ArrayItems(hostDetail.Value)
                    .Select(h => new
                    {
                        status = Str(h, "status"),
                        interfaces = ArrayProp(h, "interfaces")
                            .Select(f => new
                            {
                                type = Str(f, "type"),
                                available = Str(f, "available"),
                                port = Str(f, "port"),
                                error = Str(f, "error")
                            })
                            .ToList(),
                        templates = ArrayProp(h, "parentTemplates").Select(t => Str(t, "name")).ToList(),
                        groups = ArrayProp(h, "hostgroups").Select(g => Str(g, "name")).ToList()
                    })
                    .ToList();

                Add("9. Interfaces y plantillas del equipo (host.get)", detail.Count > 0,
                    detail.Count > 0 ? "Datos del equipo obtenidos." : "No se obtuvieron datos del equipo.",
                    detail);
            }

            // 10. Eventos de problema con el equipo que los originó
            var events = await TryCall(
                "10. Problemas con su equipo (event.get)",
                "event.get",
                new
                {
                    output = "extend",
                    selectHosts = new[] { "hostid", "name" },
                    source = 0,
                    @object = 0,
                    value = 1,
                    sortfield = new[] { "clock", "eventid" },
                    sortorder = "DESC",
                    limit = 20
                },
                true);

            if (events != null)
            {
                var eventList = ArrayItems(events.Value)
                    .Select(e => new
                    {
                        eventId = Str(e, "eventid"),
                        name = Str(e, "name"),
                        severity = Str(e, "severity"),
                        time = FormatClock(Str(e, "clock")),
                        resolved = Str(e, "r_eventid") != "" && Str(e, "r_eventid") != "0",
                        hosts = ArrayProp(e, "hosts").Select(h => Str(h, "name")).ToList()
                    })
                    .ToList();

                Add("10. Problemas con su equipo (event.get)", true,
                    $"{eventList.Count} eventos de problema (los más recientes primero).",
                    eventList);
            }

            return result;
        }

        // Elementos de un resultado en forma de arreglo (o lista vacía si no lo es).
        public static List<JsonElement> ArrayItems(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Array
                ? element.EnumerateArray().ToList()
                : new List<JsonElement>();
        }

        // Elementos de una propiedad en forma de arreglo (o lista vacía si no existe).
        public static List<JsonElement> ArrayProp(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p)
                ? ArrayItems(p)
                : new List<JsonElement>();
        }

        public static string Str(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p)
                ? p.ToString()
                : "";
        }

        private static string FormatClock(string unixSeconds)
        {
            return long.TryParse(unixSeconds, out var seconds) && seconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
                : "";
        }

        // Traduce los errores técnicos a una pista concreta sobre qué revisar.
        public static string ExplainError(Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                return $"Sin respuesta en {TimeoutSeconds} s (timeout). Suele indicar VPN o túnel caído, IP incorrecta o un firewall que bloquea el puerto.";
            }

            if (ex is ZabbixApiException zex)
            {
                var text = $"Zabbix respondió con un error: {zex.Message}"
                           + (string.IsNullOrWhiteSpace(zex.ApiData) ? "" : $" ({zex.ApiData})");

                if (text.Contains("Not authorized", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Session terminated", StringComparison.OrdinalIgnoreCase))
                {
                    text += " -> El token es inválido, expiró o está desactivado. Si es correcto, puede que el servidor web no esté reenviando el encabezado Authorization.";
                }
                else if (text.Contains("No permissions", StringComparison.OrdinalIgnoreCase))
                {
                    text += " -> El usuario del token no tiene permisos sobre ese objeto.";
                }

                return text;
            }

            if (ex is HttpRequestException http)
            {
                if (http.InnerException is SocketException se)
                {
                    switch (se.SocketErrorCode)
                    {
                        case SocketError.ConnectionRefused:
                            return "El equipo respondió, pero el puerto está cerrado: el servidor web no escucha en ese puerto o el firewall lo rechaza.";
                        case SocketError.HostNotFound:
                        case SocketError.NoData:
                            return "No se pudo resolver el nombre del servidor. Revisa la URL.";
                        case SocketError.NetworkUnreachable:
                        case SocketError.HostUnreachable:
                            return "No hay ruta hacia ese equipo: la VPN no está conectada o la IP no es la correcta.";
                        case SocketError.TimedOut:
                            return $"Sin respuesta (timeout). Suele indicar VPN o túnel caído, IP incorrecta o firewall.";
                        default:
                            return $"Error de red ({se.SocketErrorCode}). Revisa la VPN y la URL.";
                    }
                }

                if (http.InnerException is System.Security.Authentication.AuthenticationException)
                {
                    return "Problema con el certificado HTTPS (¿autofirmado?). Si la conexión va por VPN, prueba con http://.";
                }

                if (http.StatusCode.HasValue)
                {
                    var code = (int)http.StatusCode.Value;

                    if (code == 404)
                    {
                        return "El servidor respondió 404: la ruta de la API no existe. Prueba con /zabbix/api_jsonrpc.php o /api_jsonrpc.php, según cómo abra el frontend.";
                    }

                    if (code == 401 || code == 403)
                    {
                        return $"El servidor web rechazó la petición ({code}).";
                    }

                    return $"El servidor respondió con el código HTTP {code}.";
                }

                return $"Error de red: {http.Message}";
            }

            if (ex is InvalidDataException || ex is InvalidOperationException)
            {
                return ex.Message;
            }

            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}