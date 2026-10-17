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

        /// <summary>Devuelve una copia del historial actual (para guardarlo en disco).</summary>
        public IReadOnlyList<object> GetHistory()
        {
            return _conversationHistory.ToList();
        }

        /// <summary>Reemplaza el historial (por ejemplo, al restaurar una conversación guardada).</summary>
        public void ReplaceHistory(IEnumerable<object>? messages)
        {
            _conversationHistory.Clear();
            if (messages == null)
            {
                return;
            }

            foreach (var message in messages)
            {
                if (message != null)
                {
                    _conversationHistory.Add(message);
                }
            }
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

        /// <summary>
        /// Añade un mensaje técnico al historial, marcado para que no se trate como una respuesta
        /// válida del asistente a menos que sea necesario. Esto evita que errores técnicos
        /// acaben apareciendo como "respuesta de Ardilla AI" en el historial persistido.
        /// </summary>
        public void AddTechnicalNote(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                return;
            }

            _conversationHistory.Add(new { role = "system", content = $"[NOTA TÉCNICA NO MOSTRAR COMO RESPUESTA]: {note}" });
        }

        public async Task<(string ResponseText, List<dynamic>? ToolCalls)> SendChatIterationAsync(
            string? systemPrompt = null,
            Action<string>? onChunkReceived = null,
            object[]? tools = null,
            CancellationToken cancellationToken = default,
            bool isTechnicalErrorResponse = false)
        {
            if (string.IsNullOrWhiteSpace(_config.Endpoint))
            {
                return ("⚠️ No hay un endpoint configurado. Ve a Ajustes para configurar el servidor de IA (ej: http://localhost:11434).", null);
            }

            try
            {
                var client = _httpClient;
                var configLocal2 = _config;

                if (!string.IsNullOrWhiteSpace(configLocal2.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", configLocal2.ApiKey);
                }

                var configLocal = _config;
                var url = configLocal.Endpoint.TrimEnd('/') + "/api/chat";

                // Poda del historial: se conservan los mensajes más recientes que caben en el
                // presupuesto sin romper el emparejamiento 'assistant' con tool_calls / 'tool'.
                int maxMsgs = Math.Max(4, _config.MaxHistoryMessages * 2);
                int startIdx = ComputeHistoryStartIndex(_conversationHistory, maxMsgs, _config.MaxContextChars);

                var historyToSend = _conversationHistory
                    .Skip(startIdx)
                    .Select(message => NormalizeHistoryMessage(message, _config.MaxToolMessageChars))
                    .ToList();

                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    if (historyToSend.Any())
                    {
                        var firstRole = GetRole(historyToSend[0]);
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
                        num_ctx = _config.NumCtx > 0 ? _config.NumCtx : 8192
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

        private async Task<HttpResponseMessage> SendChatRequestWithFallbackAsync(
            HttpClient client,
            string url,
            Dictionary<string, object> body,
            string json,
            CancellationToken cancellationToken)
        {
            var configInstanceLocal = _config;
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
                body["options"] = new
                {
                    temperature = configInstanceLocal.Temperature,
                    num_ctx = configInstanceLocal.NumCtx > 0 ? configInstanceLocal.NumCtx : 4096
                };
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

        /// <summary>
        /// Devuelve el rol ('user', 'assistant', 'tool', 'system') de un mensaje del historial.
        /// Acepta objetos anónimos, diccionarios o tokens de JSON.
        /// </summary>
        internal static string GetRole(object message)
        {
            try
            {
                var token = message as JToken ?? JToken.FromObject(message);
                return token["role"]?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Calcula desde qué índice enviar el historial: conserva los mensajes más recientes
        /// que caben en <paramref name="maxChars"/> caracteres (además del límite de número de
        /// mensajes) sin empezar nunca en un mensaje 'tool', porque este necesita el
        /// 'assistant' con 'tool_calls' que lo precede. Es una función pura, fácil de probar.
        /// </summary>
        internal static int ComputeHistoryStartIndex(IReadOnlyList<object> history, int maxMessages, int maxChars)
        {
            if (history == null || history.Count == 0)
            {
                return 0;
            }

            int start = Math.Max(0, history.Count - Math.Max(1, maxMessages));

            // No empezar en un mensaje 'tool': retroceder hasta su 'assistant'.
            while (start > 0 && GetRole(history[start]) == "tool")
            {
                start--;
            }

            if (maxChars <= 0)
            {
                return start;
            }

            var sizes = new long[history.Count];
            for (int i = 0; i < history.Count; i++)
            {
                sizes[i] = SerializedLength(history[i]);
            }

            long windowChars = 0;
            for (int i = start; i < history.Count; i++)
            {
                windowChars += sizes[i];
            }

            // Si el recorte se pasa del presupuesto, se sueltan grupos completos desde el
            // principio (nunca a medias para no dejar un 'tool' huérfano).
            while (windowChars > maxChars && start < history.Count)
            {
                int next = start + 1;
                while (next < history.Count && GetRole(history[next]) == "tool")
                {
                    next++;
                }

                if (next >= history.Count)
                {
                    break;
                }

                for (int i = start; i < next; i++)
                {
                    windowChars -= sizes[i];
                }

                start = next;
            }

            return start;
        }

        private static long SerializedLength(object message)
        {
            try
            {
                return JsonConvert.SerializeObject(message, Formatting.None).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static object NormalizeHistoryMessage(object message, int maxToolMessageChars = 0)
        {
            var token = message as JToken ?? JToken.FromObject(message);
            var role = token["role"]?.ToString() ?? string.Empty;
            var content = token["content"]?.ToString() ?? string.Empty;

            // Las salidas de herramientas pueden ser enormes: se recortan al enviarlas.
            if (role == "tool" && maxToolMessageChars > 0 && content.Length > maxToolMessageChars)
            {
                return new Dictionary<string, object>
                {
                    ["role"] = role,
                    ["content"] = content.Substring(0, maxToolMessageChars) + "\n…(salida recortada para ahorrar contexto)"
                };
            }

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
