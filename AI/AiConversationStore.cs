using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArdillaUpdater.AI
{
    /// <summary>
    /// Guarda y recupera la conversación del asistente en un archivo JSON dentro de
    /// %LOCALAPPDATA%\ArdillaUpdater (antes era junto al .exe, carpeta a menudo
    /// protegida), para no perderla al cerrar la ventana. Clase pequeña y sin estado.
    /// </summary>
    internal static class AiConversationStore
    {
        private const string FileName = "ai_conversation.json";

        // PR #1 auditoría 2026-10: de BaseDirectory a %LOCALAPPDATA%.
        internal static string DefaultPath => Path.Combine(AppPaths.UserDataDir, FileName);

        /// <summary>Guarda el historial indicado. Si falla, no rompe la app (se informa en el log).</summary>
        public static void Save(IEnumerable<object>? messages, string? path = null, Action<string>? log = null)
        {
            try
            {
                string target = path ?? DefaultPath;

                var list = new List<object>();
                if (messages != null)
                {
                    foreach (var message in messages)
                    {
                        if (message != null)
                        {
                            list.Add(message);
                        }
                    }
                }

                var json = JsonConvert.SerializeObject(list, Formatting.Indented);

                // PR #1 auditoría 2026-10: escritura atómica (.tmp + reemplazo) y carpeta
                // por si acaso; si algo falla a mitad, el historial anterior no se corrompe.
                string? dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                AppPaths.SaveJsonAtomic(target, json);
            }
            catch (Exception ex)
            {
                // Un fallo al guardar no debe romper la conversación en curso.
                log?.Invoke($"No se pudo guardar la conversación: {ex.Message}");
            }
        }

        /// <summary>Carga el historial guardado. Si no existe o está corrupto, devuelve una lista vacía.</summary>
        public static List<object> Load(string? path = null, Action<string>? log = null)
        {
            var result = new List<object>();
            try
            {
                string target = path ?? DefaultPath;

                // PR #1 auditoría 2026-10: migración (solo ruta por defecto) del historial
                // antiguo que vivía junto al .exe a %LOCALAPPDATA%, una sola vez.
                if (path == null)
                {
                    AppPaths.TryMigrateLegacyFile(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName), target, log);
                }

                if (!File.Exists(target))
                {
                    return result;
                }

                var json = File.ReadAllText(target);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return result;
                }

                var array = JArray.Parse(json);
                foreach (var item in array)
                {
                    if (item != null)
                    {
                        result.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                // Archivo corrupto o ilegible: se empieza de cero, pero avisando por el
                // log antes de que el próximo guardado lo sobrescriba.
                log?.Invoke($"Conversación ilegible ({ex.Message}); se empieza de cero.");
            }

            return result;
        }

        /// <summary>Borra la conversación guardada.</summary>
        public static void Clear(string? path = null)
        {
            try
            {
                string target = path ?? DefaultPath;
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }
            catch
            {
                // Si no se puede borrar, no es crítico.
            }
        }
    }
}
