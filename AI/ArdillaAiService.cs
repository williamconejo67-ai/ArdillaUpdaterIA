using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArdillaUpdater.AI
{
    internal class ArdillaAiService : IDisposable
    {
        private readonly AiConfig _config;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly List<object> _conversationHistory = new();
        private bool _disposed;

        public ArdillaAiService(AiConfig config)
            : this(config, CreateDefaultHttpClient(), ownsHttpClient: true)
        {
        }

        internal ArdillaAiService(AiConfig config, HttpClient httpClient)
            : this(config, httpClient, ownsHttpClient: false)
        {
        }

        private ArdillaAiService(AiConfig config, HttpClient httpClient, bool ownsHttpClient)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = ownsHttpClient;
        }

        private static HttpClient CreateDefaultHttpClient() =>
            new() { Timeout = TimeSpan.FromMinutes(5) };

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Solo se libera el cliente cuando lo creó este servicio.
            // Si fue inyectado (tests / reutilización), el dueño es quien lo pasó.
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }

        public void ClearHistory()
        {
            _conversationHistory.Clear();
        }

        public void AddUserMessage(string message)
        {
            _conversationHistory.Add(new { role = "user", content = message });
        }

        public void AddToolResponseMessage(string toolContent)
        {
            _conversationHistory.Add(new { role = "tool", content = toolContent });
        }

        public void AddAssistantMessageWithToolCalls(string? content, IEnumerable<dynamic>? toolCalls)
        {
            var msg = new Dictionary<string, object>
            {
                ["role"] = "assistant",
                ["content"] = content ?? string.Empty
            };

            if (toolCalls != null && toolCalls.Any())
            {
                msg["tool_calls"] = NormalizeToolCalls(toolCalls);
            }

            _conversationHistory.Add(msg);
        }

        public async Task<(string ResponseText, List<dynamic>? ToolCalls)> SendChatIterationAsync(
            string? systemPrompt = null,
            Action<string>? onChunkReceived = null,
            object[]? tools = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_config.Endpoint))
            {
                return ("⚠️ No hay un endpoint configurado. Ve a Ajustes para configurar el servidor de IA (ej: http://localhost:11434).", null);
            }

            try
            {
                var client = _httpClient;

                if (!string.IsNullOrWhiteSpace(_config.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
                }

                var url = _config.Endpoint.TrimEnd('/') + "/api/chat";

                // Poda de historial para no saturar la ventana de contexto
                int maxMsgs = Math.Max(4, _config.MaxHistoryMessages * 2);
                int startIdx = Math.Max(0, _conversationHistory.Count - maxMsgs);

                while (startIdx > 0 && startIdx < _conversationHistory.Count)
                {
                    var msgDynamic = JObject.FromObject(_conversationHistory[startIdx]);
                    if (msgDynamic["role"]?.ToString() == "tool")
                    {
                        startIdx--;
                    }
                    else
                    {
                        break;
                    }
                }

                var historyToSend = _conversationHistory
                    .Skip(startIdx)
                    .Select(NormalizeHistoryMessage)
                    .ToList();

                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    if (historyToSend.Any())
                    {
                        var firstRole = JObject.FromObject(historyToSend[0])["role"]?.ToString();
                        if (firstRole == "system")
                            historyToSend[0] = new { role = "system", content = systemPrompt };
                        else
                            historyToSend.Insert(0, new { role = "system", content = systemPrompt });
                    }
                    else
                    {
                        historyToSend.Insert(0, new { role = "system", content = systemPrompt });
                    }
                }

                bool isStreaming = onChunkReceived != null;

                var bodyObj = new Dictionary<string, object>
                {
                    ["model"] = _config.Model,
                    ["messages"] = historyToSend,
                    ["stream"] = isStreaming,
                    ["options"] = new
                    {
                        temperature = _config.Temperature,
                        num_ctx = 8192
                    }
                };

                if (tools != null && tools.Length > 0)
                {
                    bodyObj["tools"] = tools;
                }

                var json = JsonConvert.SerializeObject(bodyObj, Formatting.None);
                using var response = await SendChatRequestWithFallbackAsync(client, url, bodyObj, json, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return ($"❌ Error de respuesta del servidor de IA (Código {(int)response.StatusCode}): {(string.IsNullOrEmpty(errorJson) ? response.ReasonPhrase : errorJson)}", null);
                }

                string assistantText = "";
                List<dynamic>? toolCalls = null;

                if (isStreaming)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var reader = new StreamReader(stream);
                    var sb = new StringBuilder();

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var chunkResp = JObject.Parse(line);
                            var msg = chunkResp["message"];
                            if (msg != null)
                            {
                                string? chunkText = msg["content"]?.ToString();
                                if (!string.IsNullOrEmpty(chunkText))
                                {
                                    sb.Append(chunkText);
                                    onChunkReceived?.Invoke(chunkText);
                                }

                                var chunkToolCalls = msg["tool_calls"] as JArray;
                                if (chunkToolCalls != null && chunkToolCalls.Count > 0)
                                {
                                    toolCalls ??= new List<dynamic>();
                                    foreach (var tc in chunkToolCalls)
                                    {
                                        toolCalls.Add(JsonConvert.DeserializeObject<dynamic>(tc.ToString())!);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Ignorar fragmentos parciales
                        }
                    }
                    assistantText = sb.ToString();
                }
                else
                {
                    var resultJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var result = JObject.Parse(resultJson);
                    var msg = result["message"];
                    if (msg != null)
                    {
                        assistantText = msg["content"]?.ToString() ?? "";
                        var jToolCalls = msg["tool_calls"] as JArray;
                        if (jToolCalls != null && jToolCalls.Count > 0)
                        {
                            toolCalls = new List<dynamic>();
                            foreach (var tc in jToolCalls)
                            {
                                toolCalls.Add(JsonConvert.DeserializeObject<dynamic>(tc.ToString())!);
                            }
                        }
                    }
                }

                return (assistantText, toolCalls);
            }
            catch (OperationCanceledException)
            {
                return (cancellationToken.IsCancellationRequested ? "🛑 Solicitud cancelada." : "⏳ La solicitud excedió el tiempo límite.", null);
            }
            catch (HttpRequestException ex)
            {
                return ($"🌐 No se pudo conectar al servidor de IA en '{_config.Endpoint}'.\n\nVerifica que Ollama u otro servidor esté abierto y corriendo.\nDetalle: {ex.Message}", null);
            }
            catch (Exception ex)
            {
                return ($"❌ Error inesperado: {ex.Message}", null);
            }
        }

        private static async Task<HttpResponseMessage> SendChatRequestWithFallbackAsync(
            HttpClient client,
            string url,
            Dictionary<string, object> body,
            string json,
            CancellationToken cancellationToken)
        {
            async Task<HttpResponseMessage> SendAsync(string requestJson)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                };

                return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }

            var response = await SendAsync(json).ConfigureAwait(false);
            var isBadRequest = response.StatusCode == System.Net.HttpStatusCode.BadRequest;
            var isRunnerFailure = response.StatusCode == System.Net.HttpStatusCode.InternalServerError;

            if ((!isBadRequest && !isRunnerFailure) || !body.ContainsKey("tools"))
                return response;

            response.Dispose();
            body.Remove("tools");
            if (isRunnerFailure)
            {
                body["options"] = new { temperature = 0.5, num_ctx = 4096 };
            }

            return await SendAsync(JsonConvert.SerializeObject(body, Formatting.None)).ConfigureAwait(false);
        }

        private static List<object> NormalizeToolCalls(IEnumerable<dynamic> toolCalls)
        {
            var normalized = new List<object>();

            foreach (var toolCall in toolCalls)
            {
                var token = toolCall as JToken ?? JToken.FromObject(toolCall);
                var functionToken = token["function"];
                var name = functionToken?["name"]?.ToString() ?? string.Empty;
                var argumentsToken = functionToken?["arguments"];

                object normalizedArguments = new JObject();
                if (argumentsToken != null)
                {
                    if (argumentsToken.Type == JTokenType.Object)
                    {
                        normalizedArguments = argumentsToken;
                    }
                    else
                    {
                        var rawArguments = argumentsToken.ToString();
                        try
                        {
                            normalizedArguments = string.IsNullOrWhiteSpace(rawArguments)
                                ? new JObject()
                                : JObject.Parse(rawArguments);
                        }
                        catch
                        {
                            normalizedArguments = new JObject();
                        }
                    }
                }

                normalized.Add(new
                {
                    function = new
                    {
                        name,
                        arguments = normalizedArguments
                    }
                });
            }

            return normalized;
        }

        private static object NormalizeHistoryMessage(object message)
        {
            var token = message as JToken ?? JToken.FromObject(message);
            var role = token["role"]?.ToString() ?? string.Empty;
            var content = token["content"]?.ToString() ?? string.Empty;

            if (role != "assistant")
                return message;

            var toolCalls = token["tool_calls"] as JArray;
            if (toolCalls == null || toolCalls.Count == 0)
            {
                return new Dictionary<string, object>
                {
                    ["role"] = role,
                    ["content"] = content
                };
            }

            return new Dictionary<string, object>
            {
                ["role"] = role,
                ["content"] = content,
                ["tool_calls"] = NormalizeToolCalls(toolCalls)
            };
        }
    }
}
