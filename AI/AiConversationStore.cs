using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArdillaUpdater.AI
{
    /// <summary>
    /// Guarda y recupera la conversación del asistente en un archivo JSON junto al ejecutable,
    /// para no perderla al cerrar la ventana. Es una clase pequeña y sin estado.
    /// </summary>
    internal static class AiConversationStore
    {
        private const string FileName = "ai_conversation.json";

        internal static string DefaultPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

        /// <summary>Guarda el historial indicado. Si falla, no hace ruido (la app sigue funcionando).</summary>
        public static void Save(IEnumerable<object>? messages, string? path = null)
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
                File.WriteAllText(target, json);
            }
            catch
            {
                // Un fallo al guardar no debe romper la conversación en curso.
            }
        }

        /// <summary>Carga el historial guardado. Si no existe o está corrupto, devuelve una lista vacía.</summary>
        public static List<object> Load(string? path = null)
        {
            var result = new List<object>();
            try
            {
                string target = path ?? DefaultPath;
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
            catch
            {
                // Archivo corrupto o ilegible: se ignora y se empieza de cero.
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
