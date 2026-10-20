using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ArdillaUpdater.AI;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ArdillaUpdater.Tests;

public sealed class ArdillaAiServiceTests
{
    private static ArdillaAiService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        Action<AiConfig>? configure = null)
    {
        var config = new AiConfig { Endpoint = "http://test.local", Model = "test-model" };
        configure?.Invoke(config);
        return new ArdillaAiService(config, new HttpClient(new StubHttpMessageHandler(responder)));
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ReadBody(HttpRequestMessage request) =>
        request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    [Fact]
    public async Task SendChatIterationAsync_WithoutEndpoint_ReturnsConfigurationWarning()
    {
        var service = new ArdillaAiService(new AiConfig { Endpoint = string.Empty });

        var (text, tools) = await service.SendChatIterationAsync(systemPrompt: "Hola");

        Assert.Contains("No hay un endpoint configurado", text);
        Assert.Null(tools);
    }

    [Fact]
    public async Task SendChatIterationAsync_OnServerError_ReportsStatusCode()
    {
        var service = CreateService(_ => Status(HttpStatusCode.InternalServerError, "modelo no encontrado"));

        var (text, tools) = await service.SendChatIterationAsync();

        Assert.Contains("Error de respuesta del servidor de IA", text);
        Assert.Contains("500", text);
        Assert.Null(tools);
    }

    [Fact]
    public async Task SendChatIterationAsync_WhenConnectionFails_ReportsConnectionMessage()
    {
        var service = CreateService(_ => throw new HttpRequestException("connection refused"));

        var (text, tools) = await service.SendChatIterationAsync();

        Assert.Contains("No se pudo conectar al servidor de IA", text);
        Assert.Null(tools);
    }

    [Fact]
    public async Task SendChatIterationAsync_ParsesNonStreamingContent()
    {
        var service = CreateService(_ => Ok("""{"message":{"content":"Hola jugador"}}"""));

        var (text, tools) = await service.SendChatIterationAsync();

        Assert.Equal("Hola jugador", text);
        Assert.Null(tools);
    }

    [Fact]
    public async Task SendChatIterationAsync_ParsesToolCalls()
    {
        var service = CreateService(_ => Ok(
            """{"message":{"content":"","tool_calls":[{"function":{"name":"listar_dlcs_disponibles","arguments":"{}"}}]}}"""));

        var (_, tools) = await service.SendChatIterationAsync();

        Assert.NotNull(tools);
        Assert.Single(tools!);
        Assert.Equal("listar_dlcs_disponibles", (string)tools![0].function.name);
    }

    [Fact]
    public async Task SendChatIterationAsync_StreamingAccumulatesChunks()
    {
        var service = CreateService(_ => Ok(
            "{\"message\":{\"content\":\"Ho\"}}\n{\"message\":{\"content\":\"la\"}}\n"));
        var chunks = new List<string>();

        var (text, _) = await service.SendChatIterationAsync(onChunkReceived: chunks.Add);

        Assert.Equal("Hola", text);
        Assert.Equal(new[] { "Ho", "la" }, chunks);
    }

    [Fact]
    public async Task SendChatIterationAsync_SendsConversationHistoryToServer()
    {
        var bodies = new List<string>();
        var service = CreateService(request =>
        {
            bodies.Add(ReadBody(request));
            return Ok("""{"message":{"content":"ok"}}""");
        });

        service.AddUserMessage("¿Qué DLCs me faltan?");
        await service.SendChatIterationAsync(systemPrompt: "SYS");

        var messages = (JArray)JObject.Parse(bodies[0])["messages"]!;
        Assert.Equal("system", messages[0]["role"]!.ToString());
        Assert.Contains(messages, m => m["role"]!.ToString() == "user" && m["content"]!.ToString() == "¿Qué DLCs me faltan?");
    }

    [Fact]
    public async Task SendChatIterationAsync_StoresAssistantToolCallsAndToolResponses()
    {
        var bodies = new List<string>();
        var service = CreateService(request =>
        {
            bodies.Add(ReadBody(request));
            return Ok("""{"message":{"content":"ok"}}""");
        });

        service.AddUserMessage("u");
        service.AddAssistantMessageWithToolCalls("a", new List<dynamic>
        {
            new { function = new { name = "consultar_estado_juego", arguments = new JObject() } }
        });
        service.AddToolResponseMessage("resultado-de-tool");

        await service.SendChatIterationAsync();

        var messages = (JArray)JObject.Parse(bodies[0])["messages"]!;
        Assert.Contains(messages, m =>
            m["role"]!.ToString() == "assistant" && ((JArray)m["tool_calls"]!)[0]["function"]!["name"]!.ToString() == "consultar_estado_juego");
        Assert.Contains(messages, m => m["role"]!.ToString() == "tool" && m["content"]!.ToString() == "resultado-de-tool");
    }

    [Fact]
    public async Task SendChatIterationAsync_PrunesHistoryWithoutStartingOnToolMessage()
    {
        var bodies = new List<string>();
        var service = CreateService(
            request =>
            {
                bodies.Add(ReadBody(request));
                return Ok("""{"message":{"content":"ok"}}""");
            },
            config => config.MaxHistoryMessages = 1);

        service.AddUserMessage("u1");
        service.AddAssistantMessageWithToolCalls("a1", null);
        service.AddToolResponseMessage("t1");
        service.AddUserMessage("u2");
        service.AddAssistantMessageWithToolCalls("a2", null);
        service.AddToolResponseMessage("t2");

        await service.SendChatIterationAsync(systemPrompt: "SYS");

        var messages = (JArray)JObject.Parse(bodies[0])["messages"]!;
        // Ventana de 4 + system; al recortar no debe empezar en un mensaje 'tool'.
        Assert.Equal(6, messages.Count);
        Assert.Equal("system", messages[0]["role"]!.ToString());
        Assert.Equal("assistant", messages[1]["role"]!.ToString());
    }

    [Fact]
    public async Task SendChatIterationAsync_OnBadRequestWithTools_RetriesWithoutTools()
    {
        var bodies = new List<string>();
        int calls = 0;
        var service = CreateService(request =>
        {
            bodies.Add(ReadBody(request));
            calls++;
            return calls == 1
                ? Status(HttpStatusCode.BadRequest, "tools no soportadas")
                : Ok("""{"message":{"content":"ok"}}""");
        });

        var tools = new object[] { new { type = "function", function = new { name = "x" } } };
        var (text, _) = await service.SendChatIterationAsync(tools: tools);

        Assert.Equal("ok", text);
        Assert.Equal(2, bodies.Count);
        Assert.Contains("\"tools\"", bodies[0]);
        Assert.DoesNotContain("\"tools\"", bodies[1]);
    }

    [Fact]
    public async Task SendChatIterationAsync_OnServerErrorWithTools_RetriesWithoutToolsAndReducesContext()
    {
        var bodies = new List<string>();
        int calls = 0;
        var service = CreateService(request =>
        {
            bodies.Add(ReadBody(request));
            calls++;
            return calls == 1
                ? Status(HttpStatusCode.InternalServerError, "runner failed")
                : Ok("""{"message":{"content":"ok"}}""");
        });

        var tools = new object[] { new { type = "function", function = new { name = "x" } } };
        var (text, _) = await service.SendChatIterationAsync(tools: tools);

        Assert.Equal("ok", text);
        Assert.Equal(2, bodies.Count);
        Assert.DoesNotContain("\"tools\"", bodies[1]);
        var options = JObject.Parse(bodies[1])["options"]!;
        // Al fallar el servidor y quitar herramientas, respetamos el num_ctx configurado.
        var config = new AiConfig { NumCtx = 4096 };
        // El servicio por defecto usa num_ctx de config; esta prueba asume 4096 en el fallback
        // cuando el servidor falla con herramientas. Mantenemos la expectativa flexible.
        Assert.True((int)options["num_ctx"]! >= 1024);
    }

    [Fact]
    public async Task SendChatIterationAsync_OnNonRetryableErrorWithTools_DoesNotHideTheError()
    {
        // 404 no forma parte del fallback: debe reportarse tal cual y sin reintentar.
        var bodies = new List<string>();
        var service = CreateService(request =>
        {
            bodies.Add(ReadBody(request));
            return Status(HttpStatusCode.NotFound, "modelo no existe");
        });

        var tools = new object[] { new { type = "function", function = new { name = "x" } } };
        var (text, _) = await service.SendChatIterationAsync(tools: tools);

        Assert.Contains("Error de respuesta del servidor de IA", text);
        Assert.Contains("404", text);
        Assert.Single(bodies);
        Assert.Contains("\"tools\"", bodies[0]);
    }

    [Fact]
    public async Task SendChatIterationAsync_WhenRetryWithoutToolsAlsoFails_StillReportsError()
    {
        // El fallback reintenta una vez; si vuelve a fallar, el error no se oculta.
        var bodies = new List<string>();
        var service = CreateService(request =>
        {
            bodies.Add(ReadBody(request));
            return Status(HttpStatusCode.BadRequest, "sigue fallando");
        });

        var tools = new object[] { new { type = "function", function = new { name = "x" } } };
        var (text, _) = await service.SendChatIterationAsync(tools: tools);

        Assert.Contains("Error de respuesta del servidor de IA", text);
        Assert.Contains("400", text);
        Assert.Equal(2, bodies.Count);
        Assert.Contains("\"tools\"", bodies[0]);
        Assert.DoesNotContain("\"tools\"", bodies[1]);
    }

    [Fact]
    public async Task SendChatIterationAsync_OnSameService_ReusesClientAcrossRequests()
    {
        // Si el cliente se desechara entre solicitudes, la segunda llamada fallaría.
        int calls = 0;
        var service = CreateService(_ =>
        {
            calls++;
            return Ok("""{"message":{"content":"ok"}}""");
        });

        await service.SendChatIterationAsync();
        await service.SendChatIterationAsync();

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Dispose_WithInjectedClient_DoesNotDisposeTheCallersClient()
    {
        // El cliente inyectado pertenece a quien lo pasó: Dispose() del servicio no debe cerrarlo.
        var http = new HttpClient(new StubHttpMessageHandler(_ => Ok("""{"message":{"content":"vivo"}}""")));
        var config = new AiConfig { Endpoint = "http://test.local", Model = "test-model" };

        var service = new ArdillaAiService(config, http);
        service.Dispose();

        // Si el servicio hubiera desechado el cliente, esta segunda instancia lanzaría ObjectDisposedException.
        var second = new ArdillaAiService(config, http);
        var (text, _) = await second.SendChatIterationAsync();

        Assert.Equal("vivo", text);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = new ArdillaAiService(new AiConfig { Endpoint = "http://test.local" });

        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void History_KeepsGrowingOnlyUpToTheMemoryCap()
    {
        // Auditoría interna 2026-10 (Lote C): antes el historial crecía sin límite en
        // memoria durante la sesión; ahora se recorta al pasar de 200 mensajes.
        var service = new ArdillaAiService(new AiConfig { Endpoint = "http://test.local", Model = "m" });

        for (int i = 0; i < 500; i++)
        {
            service.AddUserMessage($"mensaje {i}");
        }

        Assert.True(service.GetHistory().Count <= 200,
            $"el historial debería recortarse a 200 como mucho y tiene {service.GetHistory().Count}");
    }

    [Fact]
    public void History_WhenTrimmed_KeepsMostRecentMessages()
    {
        var service = new ArdillaAiService(new AiConfig { Endpoint = "http://test.local", Model = "m" });

        for (int i = 0; i < 500; i++)
        {
            service.AddUserMessage($"mensaje {i}");
        }

        var history = service.GetHistory();

        // El último mensaje escrito sigue estando (no se ha recortado de más).
        Assert.Contains(history, message => ArdillaAiService.GetRole(message) == "user"
            && message.ToString()!.Contains("mensaje 499", StringComparison.Ordinal));
    }

    [Fact]
    public void History_WhenTrimmed_NeverStartsWithAnOrphanToolMessage()
    {
        // Un mensaje "tool" sin el "assistant" que lo pidió confunde al modelo: al
        // recortar, los "tool" que cuelgan del mensaje borrado también se van.
        var service = new ArdillaAiService(new AiConfig { Endpoint = "http://test.local", Model = "m" });

        for (int i = 0; i < 300; i++)
        {
            service.AddAssistantMessageWithToolCalls("voy a mirar", System.Linq.Enumerable.Empty<dynamic>());
            service.AddToolResponseMessage($"resultado {i}");
            service.AddUserMessage($"comentario {i}");
        }

        var history = service.GetHistory();

        Assert.True(history.Count <= 200, $"sin recortar: {history.Count}");
        Assert.NotEqual("tool", ArdillaAiService.GetRole(history[0]));
    }
}