using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArdillaUpdater.AI
{
    internal class ArdillaAiAgent
    {
        private readonly ArdillaAiService _aiService;
        private readonly Func<string> _getGamePathFunc;
        private readonly DlcCatalogService _dlcCatalog;
        private readonly Action<List<string>>? _onTriggerPacksSelection;
        private readonly object[] _tools;

        internal ArdillaAiAgent(
            ArdillaAiService aiService,
            Func<string> getGamePathFunc,
            DlcCatalogService dlcCatalog,
            Action<List<string>>? onTriggerPacksSelection = null)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _getGamePathFunc = getGamePathFunc ?? (() => string.Empty);
            _dlcCatalog = dlcCatalog ?? throw new ArgumentNullException(nameof(dlcCatalog));
            _onTriggerPacksSelection = onTriggerPacksSelection;

            _tools =
            [
                Tool("consultar_estado_juego", "Verifica la ruta de Los Sims 4, qué DLCs están instalados actualmente en el equipo y cuáles faltan por instalar.", new { }),
                Tool("listar_dlcs_disponibles", "Obtiene la lista completa de todos los DLCs y packs oficiales disponibles en los repositorios de GitHub de Ardilla Updater con sus nombres y tamaños.", new { }),
                Tool("diagnosticar_errores_juego", "Inspecciona la carpeta de usuario de Los Sims 4 en Documentos para analizar los archivos lastException.txt, lastCrash.txt y detectar mods rotos, problemas de scripts o fallos del juego.", new { }),
                Tool("verificar_espacio_disco", "Comprueba el espacio libre disponible en el disco duro donde está instalado Los Sims 4 para saber si cabe nuevo contenido.", new { }),
                Tool("recomendar_packs", "Recomienda los mejores DLCs según una temática solicitada por el usuario (ej: magia, vida cotidiana, construcción, vacaciones, etc.).", new
                {
                    tematica = new { type = "string", description = "Temática o interés del usuario (ej: 'sobrenatural', 'familia', 'construccion', 'viajes')" }
                }, "tematica"),
                Tool("seleccionar_packs_para_instalar", "Prepara y selecciona una lista de packs específicos para que el usuario pueda descargarlos e instalarlos en Ardilla Updater.", new
                {
                    ids_packs = new { type = "array", items = new { type = "string" }, description = "Lista de identificadores de packs (ej: ['EP01', 'GP02', 'SP05'])" }
                }, "ids_packs"),
                Tool("verificar_integridad_juego", "Comprueba si los ejecutables principales (TS4_x64.exe) y carpetas esenciales del juego existen en la ruta configurada.", new { })
            ];
        }

        private static object Tool(string name, string description, object properties, params string[] required)
        {
            return new
            {
                type = "function",
                function = new
                {
                    name,
                    description,
                    parameters = new
                    {
                        type = "object",
                        properties,
                        required = required ?? Array.Empty<string>()
                    }
                }
            };
        }

        /// <summary>
        /// Procesa un mensaje del usuario con el bucle agéntico (máximo 8 vueltas).
        /// <paramref name="onChunkReceived"/> recibe el texto a medida que el modelo lo genera
        /// (streaming) y <paramref name="onAssistantMessageStarted"/> avisa de que empieza un
        /// mensaje nuevo del asistente (cada vuelta del bucle es un mensaje distinto).
        /// </summary>
        public async Task<string> ProcessUserMessageAsync(
            string userInput,
            Action<string>? onStatusUpdate = null,
            Action<string>? onChunkReceived = null,
            CancellationToken cancellationToken = default,
            Action? onAssistantMessageStarted = null)
        {
            var trimmed = userInput.Trim();
            _aiService.AddUserMessage(trimmed);

            string detectedGamePath = GetEffectiveGamePath();

            string systemPrompt =
                "Eres Ardilla AI, el asistente inteligente y experto integrado en 'Ardilla Updater' para The Sims 4. " +
                "Tu objetivo es ayudar a los jugadores a gestionar, actualizar, descargar DLCs desde GitHub, diagnosticar errores del juego (como lastException.txt) y recomendar las mejores expansiones. " +
                $"La ruta de juego configurada actualmente es: '{(string.IsNullOrEmpty(detectedGamePath) ? "No detectada aún" : detectedGamePath)}'. " +
                "REGLAS OBLIGATORIAS: " +
                "1. Responde siempre en español, con un tono amable, claro, gamer y profesional. " +
                "2. Si el usuario te pregunta qué DLCs tiene, qué le falta, si hay errores o qué espacio tiene, DEBES USAR las herramientas (tools) disponibles en lugar de adivinar. " +
                "3. Cuando uses 'seleccionar_packs_para_instalar', explica claramente qué packs vas a preparar para la descarga. " +
                "4. Si analizas errores de lastException, sé directo explicando si el problema suele ser de un mod desactualizado (UI Cheats, MC Command Center, etc.) o de un archivo corrupto. " +
                "5. Nunca devuelvas código JSON sin procesar al usuario en la respuesta final.";

            int loopCount = 0;
            string finalResponse = "";

            while (loopCount < 8)
            {
                loopCount++;

                // Cada vuelta produce un mensaje nuevo del asistente: se avisa a la UI
                // para que abra un bloque distinto y el texto se vea en streaming.
                onAssistantMessageStarted?.Invoke();
                onStatusUpdate?.Invoke(loopCount == 1 ? "Analizando tu consulta..." : "Procesando información de Ardilla...");

                var (aiText, toolCalls) = await _aiService.SendChatIterationAsync(
                    systemPrompt, onChunkReceived, _tools, cancellationToken).ConfigureAwait(false);

                bool isErrorResponse = !string.IsNullOrEmpty(aiText) && (aiText.StartsWith("⚠️", StringComparison.Ordinal) || aiText.StartsWith("❌", StringComparison.Ordinal) || aiText.StartsWith("🌐", StringComparison.Ordinal) || aiText.StartsWith("🛑", StringComparison.Ordinal) || aiText.StartsWith("⏳", StringComparison.Ordinal));

                if (!isErrorResponse)
                {
                    _aiService.AddAssistantMessageWithToolCalls(aiText, toolCalls);
                }
                else
                {
                    _aiService.AddTechnicalNote(aiText);
                }

                toolCalls ??= TryRecoverToolCallsFromPlainText(aiText);

                if (toolCalls == null || !toolCalls.Any())
                {
                    finalResponse = aiText;
                    if (string.IsNullOrWhiteSpace(finalResponse) && loopCount > 1)
                        finalResponse = "He completado las acciones solicitadas.";
                    break;
                }

                foreach (var tool in toolCalls)
                {
                    string toolName = tool.function?.name?.ToString() ?? "";
                    string toolArgsJson = tool.function?.arguments?.ToString() ?? "{}";
                    dynamic args = JsonConvert.DeserializeObject(toolArgsJson) ?? new { };
                    string executionResult = "";

                    onStatusUpdate?.Invoke(GetStatusMessageForTool(toolName));

                    try
                    {
                        switch (toolName)
                        {
                            case "consultar_estado_juego":
                                executionResult = await CheckGameStateAsync(cancellationToken).ConfigureAwait(false);
                                break;

                            case "listar_dlcs_disponibles":
                                executionResult = await ListAvailableDlcsAsync(cancellationToken).ConfigureAwait(false);
                                break;

                            case "diagnosticar_errores_juego":
                                executionResult = DiagnoseSims4Errors();
                                break;

                            case "verificar_espacio_disco":
                                executionResult = CheckDiskSpace();
                                break;

                            case "recomendar_packs":
                                string tematica = args.tematica?.ToString() ?? "";
                                executionResult = await BuildRecommendationAsync(tematica, cancellationToken).ConfigureAwait(false);
                                break;

                            case "seleccionar_packs_para_instalar":
                                executionResult = await SelectPacksForDownloadAsync(args, cancellationToken).ConfigureAwait(false);
                                break;

                            case "verificar_integridad_juego":
                                executionResult = CheckGameIntegrity();
                                break;

                            default:
                                executionResult = $"Herramienta '{toolName}' desconocida.";
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        executionResult = $"Error ejecutando {toolName}: {ex.Message}";
                    }

                    _aiService.AddToolResponseMessage(executionResult);
                }
            }

            if (string.IsNullOrWhiteSpace(finalResponse))
            {
                finalResponse = "No pude completar la solicitud. Intenta reformular tu pregunta " +
                                "o pedir una acción más concreta.";
            }

            return finalResponse;
        }

        private static string GetStatusMessageForTool(string toolName)
        {
            return toolName switch
            {
                "consultar_estado_juego" => "🔍 Comprobando DLCs instalados en tu juego...",
                "listar_dlcs_disponibles" => "🌐 Consultando catálogo de GitHub de Ardilla...",
                "diagnosticar_errores_juego" => "⚠️ Analizando registros de error (lastException)...",
                "verificar_espacio_disco" => "💾 Calculando espacio libre en disco...",
                "recomendar_packs" => "💡 Generando recomendaciones personalizadas...",
                "seleccionar_packs_para_instalar" => "📦 Preparando packs para descarga...",
                "verificar_integridad_juego" => "🛡️ Comprobando archivos de instalación...",
                _ => "⚡ Ejecutando operación..."
            };
        }

        private string GetEffectiveGamePath()
        {
            string gamePath = _getGamePathFunc();
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                var detected = GamePathDetector.DetectGamePath();
                if (detected != null && !string.IsNullOrEmpty(detected.Path))
                    gamePath = detected.Path;
            }
            return gamePath ?? string.Empty;
        }

        private async Task<string> CheckGameStateAsync(CancellationToken cancellationToken)
        {
            string gamePath = GetEffectiveGamePath();

            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                return "No se ha encontrado una instalación válida de The Sims 4. Por favor, especifica la ruta de instalación en la ventana principal o usa el botón 'Detectar'.";
            }

            var entries = Directory.GetDirectories(gamePath)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            var installedPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var regex = new Regex(@"^(EP|GP|SP|FP)\d+$", RegexOptions.IgnoreCase);

            foreach (var dir in entries)
            {
                if (regex.IsMatch(dir!))
                    installedPacks.Add(dir!.ToUpperInvariant());
            }

            var catalogNames = await LoadNamesSafeAsync(cancellationToken).ConfigureAwait(false);

            var sb = new StringBuilder();
            sb.AppendLine($"Ruta analizada: {gamePath}");
            sb.AppendLine($"Packs instalados detectados: {installedPacks.Count}");

            if (catalogNames.Count > 0)
            {
                var missing = catalogNames.Keys
                    .Where(k => regex.IsMatch(k) && !installedPacks.Contains(k))
                    .OrderBy(k => k)
                    .ToList();

                sb.AppendLine($"Packs disponibles en catálogo pero faltantes: {missing.Count}");
                if (missing.Count > 0)
                {
                    sb.AppendLine("Packs que te faltan:");
                    foreach (var m in missing.Take(25))
                    {
                        var name = catalogNames.TryGetValue(m, out var n) ? n.es : m;
                        sb.AppendLine($"- {m}: {name}");
                    }
                    if (missing.Count > 25)
                    {
                        sb.AppendLine($"... y {missing.Count - 25} packs adicionales.");
                    }
                }
                else
                {
                    sb.AppendLine("¡Tienes todos los DLCs conocidos instalados!");
                }
            }
            else
            {
                sb.AppendLine($"Packs presentes: {string.Join(", ", installedPacks.OrderBy(x => x))}");
            }

            return sb.ToString();
        }

        private async Task<string> ListAvailableDlcsAsync(CancellationToken cancellationToken)
        {
            var names = await LoadNamesSafeAsync(cancellationToken).ConfigureAwait(false);
            if (names.Count == 0)
            {
                return "No se pudo cargar el catálogo de GitHub en este momento. Revisa la conexión a Internet.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Total de packs registrados en GitHub: {names.Count}");
            int count = 0;
            foreach (var kv in names.OrderBy(k => k.Key))
            {
                count++;
                sb.AppendLine($"{kv.Key}: {kv.Value.es}");
                if (count >= 40)
                {
                    sb.AppendLine($"... y {names.Count - 40} más.");
                    break;
                }
            }
            return sb.ToString();
        }

        private async Task<Dictionary<string, (string es, string en)>> LoadNamesSafeAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _dlcCatalog.LoadNamesAsync(AppConfig.DlcsListSources, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return new Dictionary<string, (string es, string en)>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private string DiagnoseSims4Errors()
        {
            // Auditoría interna 2026-10 (Lote D): la búsqueda de la carpeta de usuario se
            // delega en Sims4ErrorAnalyzer, que contempla OneDrive, "Documentos" redirigido
            // por el Registro y el nombre del juego en otros idiomas (antes solo probaba
            // dos rutas exactas: "The Sims 4" y "Los Sims 4").
            string? foundDir = Sims4ErrorAnalyzer.TryFindUserFolder();

            if (foundDir == null)
            {
                string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return $"No se encontró la carpeta de usuario de Los Sims 4 en Documentos " +
                       $"({docsPath}\\Electronic Arts). ¿El juego se ha ejecutado al menos una vez en este equipo?";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Carpeta de usuario examinada: {foundDir}");

            // El archivo puede estar en la raíz o en subcarpetas conocidas (Mods...).
            string? lastExceptionPath = Sims4ErrorAnalyzer.FindLastExceptionFile(foundDir);
            if (lastExceptionPath != null)
            {
                var fi = new FileInfo(lastExceptionPath);
                sb.AppendLine($"⚠️ 'lastException.txt' encontrado (Última modificación: {fi.LastWriteTime:yyyy-MM-dd HH:mm})");
                sb.AppendLine();
                sb.AppendLine(Sims4ErrorAnalyzer.AnalyzeLastException(lastExceptionPath));
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("✅ No hay archivo lastException.txt (el juego no ha registrado errores recientes de script).");
            }

            string lastCrashPath = Path.Combine(foundDir, "lastCrash.txt");
            if (File.Exists(lastCrashPath))
            {
                var fi = new FileInfo(lastCrashPath);
                sb.AppendLine($"⚠️ 'lastCrash.txt' encontrado (Última modificación: {fi.LastWriteTime:yyyy-MM-dd HH:mm}). Indica un cierre inesperado del juego.");
            }

            string modsDir = Path.Combine(foundDir, "Mods");
            if (Directory.Exists(modsDir))
            {
                try
                {
                    var modFiles = Directory.GetFiles(modsDir, "*.package", SearchOption.AllDirectories);
                    var ts4scripts = Directory.GetFiles(modsDir, "*.ts4script", SearchOption.AllDirectories);
                    sb.AppendLine($"Carpeta Mods detectada: {modFiles.Length} archivos .package y {ts4scripts.Length} scripts .ts4script.");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Carpeta Mods detectada, pero no se pudo enumerar: {ex.Message}");
                }
            }

            return sb.ToString();
        }

        private string CheckDiskSpace()
        {
            string gamePath = GetEffectiveGamePath();

            string driveLetter = "C:\\";
            if (!string.IsNullOrWhiteSpace(gamePath))
            {
                var root = Path.GetPathRoot(gamePath);
                if (!string.IsNullOrEmpty(root)) driveLetter = root;
            }

            try
            {
                var drive = new DriveInfo(driveLetter);
                long freeBytes = drive.AvailableFreeSpace;
                long totalBytes = drive.TotalSize;
                double freeGb = freeBytes / (1024.0 * 1024 * 1024);
                double totalGb = totalBytes / (1024.0 * 1024 * 1024);

                return $"Unidad {drive.Name}: {freeGb:F1} GB libres de {totalGb:F1} GB totales. " +
                       (freeGb < 15 ? "⚠️ ¡Poco espacio libre! Los Sims 4 con todos los DLCs puede requerir 60-80 GB." : "✅ Hay buen espacio disponible para descargas e instalaciones.");
            }
            catch (Exception ex)
            {
                return $"Error al consultar la unidad {driveLetter}: {ex.Message}";
            }
        }

        private string CheckGameIntegrity()
        {
            string gamePath = GetEffectiveGamePath();

            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                return "Ruta del juego no encontrada o inválida.";
            }

            string exe64 = Path.Combine(gamePath, "Game", "Bin", "TS4_x64.exe");
            string exeDx9 = Path.Combine(gamePath, "Game", "Bin", "TS4_DX9_x64.exe");
            string dataClient = Path.Combine(gamePath, "Data", "Client");

            var sb = new StringBuilder();
            sb.AppendLine($"Verificando integridad en: {gamePath}");
            sb.AppendLine(File.Exists(exe64) ? "✅ TS4_x64.exe presente." : (File.Exists(exeDx9) ? "✅ TS4_DX9_x64.exe presente." : "❌ Ejecutable principal del juego no encontrado en Game\\Bin."));
            sb.AppendLine(Directory.Exists(dataClient) ? "✅ Carpeta Data\\Client presente." : "⚠️ Falta la carpeta Data\\Client.");

            return sb.ToString();
        }

        internal static string RecommendPacks(string tematica)
        {
            string t = tematica.ToLowerInvariant();
            if (t.Contains("mag") || t.Contains("sobrenatural") || t.Contains("vampir") || t.Contains("fantasia") || t.Contains("bruj") || t.Contains("lican"))
            {
                return "Recomendaciones de packs sobrenaturales y fantasía:\n" +
                       "- GP04: Vampiros (Vampires) - Poderes, ciudad oscura de Forgotten Hollow.\n" +
                       "- GP08: Y el Reino de la Magia (Realm of Magic) - Hechiceros, duelos, pociones y mundo mágico.\n" +
                       "- GP12: Licántropos (Werewolves) - Hombres lobo, manadas y Moonwood Mill.\n" +
                       "- SP18: Fenómenos Paranormales (Paranormal Stuff) - Casas encantadas, sesiones espiritistas y profesión de investigador paranormal.";
            }

            if (t.Contains("famili") || t.Contains("bebe") || t.Contains("hijo") || t.Contains("vida") || t.Contains("generacion"))
            {
                return "Recomendaciones para juego familiar y generaciones:\n" +
                       "- EP13: Creciendo en Familia (Growing Together) - Dinámicas familiares, hitos de vida e infancia.\n" +
                       "- GP05: Papás y Mamás (Parenthood) - Valores de conducta, disciplina y educación.\n" +
                       "- EP05: Las Cuatro Estaciones (Seasons) - Clima, festividades y vida familiar durante el año.\n" +
                       "- EP08: Días de Universidad (Discover University) - Vida universitaria y carreras avanzadas.";
            }

            if (t.Contains("constru") || t.Contains("decor") || t.Contains("diseñ") || t.Contains("arquitect"))
            {
                return "Recomendaciones para constructores y decoradores:\n" +
                       "- GP10: Interiorismo (Dream Home Decorator) - Muebles modulares y profesión de interiorista.\n" +
                       "- SP19: Minicasas (Tiny Living) - Retos de espacio y objetos multifuncionales.\n" +
                       "- EP10: Escapada en la Nieve (Snowy Escape) - Estilo arquitectónico japonés y zen.\n" +
                       "- SP36 a SP45: Kits de decoración y construcción modernos.";
            }

            if (t.Contains("naturalez") || t.Contains("granja") || t.Contains("animal") || t.Contains("caballo") || t.Contains("campo"))
            {
                return "Recomendaciones de vida en el campo y animales:\n" +
                       "- EP11: Vida en el Pueblo (Cottage Living) - Vacas, gallinas, llamas y agricultura en Henford-on-Bagley.\n" +
                       "- EP14: Rancho de Caballos (Horse Ranch) - Caballos, cabras y vida de rancho.\n" +
                       "- EP04: Perros y Gatos (Cats & Dogs) - Mascotas y clínica veterinaria.\n" +
                       "- EP10: Escapada en la Nieve (Snowy Escape) - Montañismo y aguas termales.";
            }

            return "Packs imprescindibles generales más aclamados por la comunidad:\n" +
                   "1. EP05: Las Cuatro Estaciones (Seasons) - Clima y festividades que cambian todo el juego.\n" +
                   "2. EP13: Creciendo en Familia (Growing Together) - Profundidad en relaciones e hitos.\n" +
                   "3. EP03: Urbanitas (City Living) - Apartamentos, festivales y vida en la gran ciudad.\n" +
                   "4. EP11: Vida en el Pueblo (Cottage Living) - Campo y animales.\n" +
                   "5. GP05: Papás y Mamás (Parenthood) - Educación y crianza.";
        }

        /// <summary>
        /// Normaliza identificadores de packs: recorta espacios, pasa a mayúsculas, descarta
        /// vacíos y elimina duplicados. Función pura, fácil de probar.
        /// </summary>
        internal static List<string> NormalizePackIds(IEnumerable<string> packIds)
        {
            return packIds
                .Select(id => (id ?? string.Empty).Trim().ToUpperInvariant())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Separa una lista de identificadores de packs en válidos y desconocidos según el
        /// catálogo. Es una función pura para poder probarla sin depender de la red.
        /// </summary>
        internal static (List<string> Valid, List<string> Unknown) PartitionPackIds(
            IEnumerable<string> packIds,
            IReadOnlyCollection<string> knownIds)
        {
            var known = new HashSet<string>(knownIds, StringComparer.OrdinalIgnoreCase);
            var valid = new List<string>();
            var unknown = new List<string>();

            foreach (var id in NormalizePackIds(packIds))
            {
                if (known.Contains(id))
                {
                    valid.Add(id);
                }
                else
                {
                    unknown.Add(id);
                }
            }

            return (valid, unknown);
        }

        /// <summary>
        /// Valida la lista de packs que propone la IA contra el catálogo real antes de
        /// pasarla a la descarga selectiva. Si el catálogo no está disponible no se puede
        /// validar, así que se respeta la lista tal cual (comportamiento anterior).
        /// </summary>
        private async Task<string> SelectPacksForDownloadAsync(dynamic args, CancellationToken cancellationToken)
        {
            var rawList = args.ids_packs as JArray;
            if (rawList == null || rawList.Count == 0)
            {
                return "No se especificaron identificadores de packs para seleccionar.";
            }

            var packIds = rawList
                .Select(x => x?.ToString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (packIds.Count == 0)
            {
                return "La lista de packs está vacía.";
            }

            var names = await LoadNamesSafeAsync(cancellationToken).ConfigureAwait(false);

            List<string> valid;
            List<string> unknown;

            if (names.Count > 0)
            {
                (valid, unknown) = PartitionPackIds(packIds, names.Keys.ToList());
            }
            else
            {
                // Sin catálogo no podemos validar: se respeta la lista original.
                valid = NormalizePackIds(packIds);
                unknown = new List<string>();
            }

            if (valid.Count == 0)
            {
                return $"Ninguno de los identificadores propuestos existe en el catálogo de Ardilla Updater ({string.Join(", ", unknown)}). " +
                       "Usa solo identificadores reales (por ejemplo EP01, GP04, SP05); puedes obtenerlos con 'listar_dlcs_disponibles'.";
            }

            _onTriggerPacksSelection?.Invoke(valid);

            string result = $"Se han marcado {valid.Count} packs para preparación y descarga: {string.Join(", ", valid)}. La interfaz de descarga selectiva se ha actualizado.";
            if (unknown.Count > 0)
            {
                result += $" Nota: se descartaron identificadores que no existen en el catálogo: {string.Join(", ", unknown)}.";
            }

            return result;
        }

        /// <summary>
        /// Añade a una recomendación temática la disponibilidad real de cada pack mencionado,
        /// para que la IA no sugiera packs que no existen en Ardilla Updater. Si no hay
        /// catálogo (lista vacía) devuelve el texto original sin cambios.
        /// </summary>
        internal static string EnrichRecommendationWithCatalog(string baseText, IReadOnlyCollection<string> knownIds)
        {
            if (knownIds == null || knownIds.Count == 0)
            {
                return baseText;
            }

            var mentioned = Regex.Matches(baseText, @"\b(EP|GP|SP|FP)\d+\b", RegexOptions.IgnoreCase)
                .Select(m => m.Value.ToUpperInvariant())
                .Distinct()
                .ToList();

            if (mentioned.Count == 0)
            {
                return baseText;
            }

            var known = new HashSet<string>(knownIds, StringComparer.OrdinalIgnoreCase);
            var available = mentioned.Where(known.Contains).ToList();
            var notInCatalog = mentioned.Where(id => !known.Contains(id)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine(baseText);
            sb.AppendLine();
            sb.AppendLine("Verificación contra el catálogo real de Ardilla Updater:");
            sb.AppendLine(available.Count > 0
                ? $"- Disponibles: {string.Join(", ", available)}."
                : "- Ninguno de los packs sugeridos está en el catálogo actual.");
            if (notInCatalog.Count > 0)
            {
                sb.AppendLine($"- No encontrados en el catálogo (no los recomiendes): {string.Join(", ", notInCatalog)}.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Recomendación temática enriquecida con la disponibilidad real del catálogo.
        /// Si el catálogo no carga, se devuelve la recomendación base.
        /// </summary>
        private async Task<string> BuildRecommendationAsync(string tematica, CancellationToken cancellationToken)
        {
            string baseText = RecommendPacks(tematica);

            var names = await LoadNamesSafeAsync(cancellationToken).ConfigureAwait(false);
            if (names.Count == 0)
            {
                return baseText;
            }

            return EnrichRecommendationWithCatalog(baseText, names.Keys.ToList());
        }

        internal static List<dynamic>? TryRecoverToolCallsFromPlainText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var match = Regex.Match(text, @"```(?:json)?\s*(\{\s*""(?:name|tool|function)""[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = Regex.Match(text, @"(\{\s*""(?:name|tool|function)""[\s\S]*?\})", RegexOptions.IgnoreCase);
            }

            if (match.Success)
            {
                try
                {
                    var parsed = JObject.Parse(match.Groups[1].Value);
                    var name = parsed["name"]?.ToString() ?? parsed["tool"]?.ToString() ?? parsed["function"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        var args = parsed["parameters"] ?? parsed["arguments"] ?? parsed["args"] ?? new JObject();
                        var toolCall = new JObject(
                            new JProperty("function", new JObject(
                                new JProperty("name", name),
                                new JProperty("arguments", args.ToString())
                            ))
                        );
                        return new List<dynamic> { toolCall };
                    }
                }
                catch
                {
                    // Ignorar si no es JSON válido
                }
            }

            return null;
        }
    }
}
