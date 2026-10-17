using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ArdillaUpdater.AI
{
    /// <summary>
    /// Extrae y resume la información más útil de lastException.txt para que la IA
    /// pueda ofrecer un diagnóstico correcto, indicando qué mod es el culpable cuando
    /// sea posible.
    /// </summary>
    internal static class Sims4ErrorAnalyzer
    {
        /// <summary>Devuelve un análisis legible de lastException.txt si existe.</summary>
        public static string AnalyzeLastException(string lastExceptionPath)
        {
            if (string.IsNullOrWhiteSpace(lastExceptionPath) || !File.Exists(lastExceptionPath))
            {
                return string.Empty;
            }

            try
            {
                var lines = File.ReadAllLines(lastExceptionPath);
                if (lines.Length == 0)
                {
                    return "lastException.txt está vacío.";
                }

                var sb = new StringBuilder();
                var modCandidates = new List<string>();

                // Buscar patrones típicos: "Mod:", "modname", "LastModule", "Package:", "Loading module:", etc.
                foreach (var raw in lines)
                {
                    string line = raw.TrimEnd();
                    // Buscar línea con "Mod:" o que indique nombre de mod
                    if (line.IndexOf("Mod:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ExtractMod(line, modCandidates);
                    }

                    // Píldoras de Sims 4: suele mostrar el nombre del .package o ruta
                    if (line.IndexOf(".package", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ExtractPackage(line, modCandidates);
                    }

                    // La línea "LastModule" suele indicar el módulo/archivo
                    if (line.IndexOf("LastModule", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ExtractLastModule(line, modCandidates);
                    }

                    // Mensajes de script
                    if (line.IndexOf("Script Error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Guardar esas líneas pero solo las más compactas
                        if (line.Length > 10 && line.Length < 250)
                        {
                            // Añadirla para resumen, pero no duplicar
                        }
                    }
                }

                var fi = new FileInfo(lastExceptionPath);
                sb.AppendLine($"Archivo: {lastExceptionPath}");
                sb.AppendLine($"Última modificación: {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine();

                // Extraer línea tipo (Exception, Type, etc.)
                string? exceptionType = lines.FirstOrDefault(l => l.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0 && l.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0);
                if (exceptionType != null)
                {
                    sb.AppendLine("Tipo de excepción detectado:");
                    sb.AppendLine(exceptionType.Trim());
                    sb.AppendLine();
                }

                if (modCandidates.Count > 0)
                {
                    sb.AppendLine("Mod(s) sospechoso(s) encontrado(s):");
                    foreach (var m in modCandidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(5))
                    {
                        sb.AppendLine($"- {m}");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("Extracto completo (para que la IA confirme el diagnóstico):");
                // Mostrar las 40 primeras líneas para que la IA vea todo el contexto
                var takeCount = Math.Min(40, lines.Length);
                for (int i = 0; i < takeCount; i++)
                {
                    sb.AppendLine(lines[i]);
                }

                if (lines.Length > 40)
                {
                    sb.AppendLine($"... (+{lines.Length - 40} líneas más en el archivo)");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"No se pudo leer lastException.txt: {ex.Message}";
            }
        }

        private static void ExtractMod(string line, List<string> candidates)
        {
            int idx = line.IndexOf("Mod:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;
            string rest = line.Substring(idx + 4).Trim();
            if (rest.Length == 0) return;

            // Quitar nombres internos o números extra
            // Cortar en comillas, paréntesis...
            string candidate = rest;
            if (candidate.IndexOf('(') > 0)
            {
                candidate = candidate.Substring(0, candidate.IndexOf('(')).Trim();
            }
            if (candidate.IndexOf('"') > 0)
            {
                candidate = candidate.Substring(candidate.IndexOf('"') + 1);
                if (candidate.IndexOf('"') > 0)
                {
                    candidate = candidate.Substring(0, candidate.IndexOf('"'));
                }
            }

            if (candidate.Length > 4 && !candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(candidate);
            }
        }

        private static void ExtractPackage(string line, List<string> candidates)
        {
            int idx = line.IndexOf(".package", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;

            // Buscar hacia atrás hasta espacio o \
            int start = idx - 1;
            while (start > 0)
            {
                char c = line[start];
                if (char.IsWhiteSpace(c) || c == '\\' || c == '/' || c == ':' || c == '>' || c == ')')
                {
                    start++;
                    break;
                }
                start--;
            }
            if (start < 0) start = 0;
            if (start >= idx) return;

            string name = line.Substring(start, idx - start + ".package".Length).Trim();
            if (name.Length > 8 && !candidates.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(name);
            }
        }

        private static void ExtractLastModule(string line, List<string> candidates)
        {
            int idx = line.IndexOf("LastModule:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;
            string rest = line.Substring(idx + "LastModule:".Length).Trim();
            if (rest.Length == 0) return;
            if (rest.Length > 4 && !candidates.Contains(rest, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(rest);
            }
        }
    }
}
