using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ArdillaUpdater;
using ArdillaUpdater.AI;
using Xunit;

namespace ArdillaUpdater.Tests;

public sealed class ArdillaAiAgentTests
{
    [Theory]
    [InlineData("magia", "GP08")]
    [InlineData("sobrenatural", "GP04")]
    [InlineData("familia", "EP13")]
    [InlineData("construccion", "GP10")]
    [InlineData("granja", "EP11")]
    public void RecommendPacks_MatchesRequestedTheme(string theme, string expectedPack)
    {
        string result = ArdillaAiAgent.RecommendPacks(theme);

        Assert.Contains(expectedPack, result);
    }

    [Fact]
    public void RecommendPacks_UnknownTheme_ReturnsGeneralPicks()
    {
        string result = ArdillaAiAgent.RecommendPacks("cocina");

        Assert.Contains("EP05", result);
    }

    [Fact]
    public void TryRecoverToolCallsFromPlainText_ExtractsToolFromJsonCodeBlock()
    {
        string text = "Voy a revisar.\n```json\n{\"name\":\"consultar_estado_juego\",\"arguments\":{}}\n```";

        var calls = ArdillaAiAgent.TryRecoverToolCallsFromPlainText(text);

        Assert.NotNull(calls);
        Assert.Single(calls!);
        Assert.Equal("consultar_estado_juego", (string)calls![0].function.name);
    }

    [Fact]
    public void TryRecoverToolCallsFromPlainText_ReturnsNullWithoutJson()
    {
        Assert.Null(ArdillaAiAgent.TryRecoverToolCallsFromPlainText("Solo texto normal, sin json."));
    }

    [Fact]
    public void TryRecoverToolCallsFromPlainText_ReturnsNullOnInvalidJson()
    {
        Assert.Null(ArdillaAiAgent.TryRecoverToolCallsFromPlainText("{ \"name\": }"));
    }

    [Fact]
    public async Task ProcessUserMessageAsync_WithoutEndpoint_ReturnsGracefulMessage()
    {
        var agent = CreateAgent(_ => Ok(""), () => string.Empty, endpoint: string.Empty);

        string response = await agent.ProcessUserMessageAsync("Hola, ¿qué tal?");

        Assert.Contains("No hay un endpoint configurado", response);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_WithoutToolCalls_ReturnsAssistantText()
    {
        var bodies = new List<string>();
        var agent = CreateAgent(
            Capture(bodies, _ => Ok("""{"message":{"content":"Hola, soy Ardilla AI."}}""")),
            () => @"C:\JuegoSimulado");

        string response = await agent.ProcessUserMessageAsync("Hola");

        Assert.Equal("Hola, soy Ardilla AI.", response);
        Assert.Single(bodies);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_WithRecommendTool_ExecutesToolAndReturnsFinalAnswer()
    {
        var bodies = new List<string>();
        int calls = 0;
        var agent = CreateAgent(
            Capture(bodies, _ =>
            {
                calls++;
                return calls == 1
                    ? Ok("""{"message":{"content":"","tool_calls":[{"function":{"name":"recomendar_packs","arguments":"{\"tematica\":\"magia\"}"}}]}}""")
                    : Ok("""{"message":{"content":"¡Listo!"}}""");
            }),
            () => @"C:\JuegoSimulado");

        string response = await agent.ProcessUserMessageAsync("Recomiéndame packs de magia");

        Assert.Equal("¡Listo!", response);
        Assert.Equal(2, bodies.Count);
        Assert.Contains("\"role\":\"tool\"", bodies[1]);
        Assert.Contains("GP08", bodies[1]);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_WithSelectPacksTool_InvokesCallbackWithNormalizedIds()
    {
        List<string>? captured = null;
        int calls = 0;
        var agent = CreateAgent(
            Capture(new List<string>(), _ =>
            {
                calls++;
                return calls == 1
                    ? Ok("""{"message":{"content":"","tool_calls":[{"function":{"name":"seleccionar_packs_para_instalar","arguments":"{\"ids_packs\":[\"gp01\",\" ep03 \"]}"}}]}}""")
                    : Ok("""{"message":{"content":"Preparados."}}""");
            }),
            () => @"C:\JuegoSimulado",
            onPacks: ids => captured = ids);

        await agent.ProcessUserMessageAsync("Instala Vampiros");

        Assert.NotNull(captured);
        Assert.Equal(new[] { "GP01", "EP03" }, captured);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_UnknownTool_LoopsAndStopsAtEightIterations()
    {
        var bodies = new List<string>();
        var agent = CreateAgent(
            Capture(bodies, _ => Ok(
                """{"message":{"content":"","tool_calls":[{"function":{"name":"herramienta_inexistente","arguments":"{}"}}]}}""")),
            () => @"C:\JuegoSimulado");

        await agent.ProcessUserMessageAsync("Haz algo raro");

        // El bucle del agente tiene un tope de 8 iteraciones (loopCount < 8).
        // Como el modelo siempre pide una tool, debe detenerse exactamente en la 8.ª,
        // no antes ni después: eso comprueba el límite real, no solo "alguna respuesta".
        Assert.Equal(8, bodies.Count);
        Assert.Contains("Herramienta 'herramienta_inexistente' desconocida.", bodies[1]);
    }

    [Fact]
    public void PartitionPackIds_SeparatesKnownFromUnknown()
    {
        var (valid, unknown) = ArdillaAiAgent.PartitionPackIds(
            new[] { "gp01", "EP99", " EP03 ", "GP01" },
            new[] { "GP01", "EP03", "EP05" });

        Assert.Equal(new[] { "GP01", "EP03" }, valid);
        Assert.Equal(new[] { "EP99" }, unknown);
    }

    [Fact]
    public void NormalizePackIds_TrimsUppercasesAndRemovesDuplicates()
    {
        var ids = ArdillaAiAgent.NormalizePackIds(new[] { "gp01", " EP03 ", "GP01", "" });

        Assert.Equal(new[] { "GP01", "EP03" }, ids);
    }

    [Fact]
    public void EnrichRecommendationWithCatalog_MarksAvailableAndMissingPacks()
    {
        string enriched = ArdillaAiAgent.EnrichRecommendationWithCatalog(
            "Te recomiendo EP05 y GP04.", new[] { "EP05" });

        Assert.Contains("Disponibles: EP05", enriched);
        Assert.Contains("no los recomiendes", enriched);
        Assert.Contains("GP04", enriched);
    }

    [Fact]
    public void EnrichRecommendationWithCatalog_WithoutCatalog_ReturnsBaseText()
    {
        string baseText = "Te recomiendo EP05 y GP04.";

        string enriched = ArdillaAiAgent.EnrichRecommendationWithCatalog(baseText, Array.Empty<string>());

        Assert.Equal(baseText, enriched);
    }

    [Fact]
    public async Task ProcessUserMessageAsync_StreamsChunksAndSignalsEachIteration()
    {
        var bodies = new List<string>();
        int calls = 0;
        var agent = CreateAgent(
            Capture(bodies, _ =>
            {
                calls++;
                return calls == 1
                    ? Ok("""{"message":{"content":"","tool_calls":[{"function":{"name":"listar_dlcs_disponibles","arguments":"{}"}}]}}""")
                    : Ok("""{"message":{"content":"Listo"}}""");
            }),
            () => @"C:\JuegoSimulado");

        var chunks = new List<string>();
        int segments = 0;

        string response = await agent.ProcessUserMessageAsync(
            "¿Qué DLCs hay?",
            onStatusUpdate: null,
            onChunkReceived: chunks.Add,
            cancellationToken: default,
            onAssistantMessageStarted: () => segments++);

        // Dos vueltas del bucle => dos mensajes del asistente; solo la segunda aporta texto.
        Assert.Equal("Listo", response);
        Assert.Equal(2, segments);
        Assert.Equal(2, bodies.Count);
        Assert.Contains("Listo", chunks);
    }

    private static ArdillaAiAgent CreateAgent(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        Func<string> gamePath,
        Action<List<string>>? onPacks = null,
        string endpoint = "http://test.local")
    {
        var config = new AiConfig { Endpoint = endpoint, Model = "test-model" };
        var service = new ArdillaAiService(config, new HttpClient(new StubHttpMessageHandler(responder)));
        var catalog = new DlcCatalogService(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            _ => { });
        return new ArdillaAiAgent(service, gamePath, catalog, onPacks);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Capture(
        List<string> bodies,
        Func<HttpRequestMessage, HttpResponseMessage> inner) =>
        request =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return inner(request);
        };

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}