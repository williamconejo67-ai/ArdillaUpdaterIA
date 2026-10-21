using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ArdillaUpdater.AI
{
    /// <summary>
    /// Diagnóstico de los errores de Los Sims 4.
    ///
    /// Se encarga de dos cosas:
    ///  1. <b>Localizar</b> la carpeta de usuario del juego en Documentos de forma robusta
    ///     (OneDrive, Documentos redirigidos, juego en español o en otro idioma).
    ///  2. <b>Analizar</b> <c>lastException.txt</c> (y compañía) para sacar la información
    ///     útil: tipo de excepción, mod sospechoso y un extracto, en un formato que la IA
    ///     pueda interpretar.
    ///
    /// Auditoría interna 2026-10 (Lote D): la búsqueda de carpetas estaba en
    /// <c>ArdillaAiAgent</c> y solo probaba dos rutas exactas. Se trajo aquí para poder
    /// probarla con tests y para no perder casos como OneDrive o "Les Sims 4".
    /// </summary>
    internal static class Sims4ErrorAnalyzer
    {
        // No recorrer subcarpetas sin fin (un usuario con miles de mods no debe colgar el diagnóstico).
        private const int MaxScanDepth = 3;
        private const int MaxScanFolders = 800;

        /// <summary>Días a partir de los cuales un error se considera "viejo".</summary>
        internal const int StaleErrorDays = 30;

        /// <summary>Líneas del extracto que se le pasan a la IA.</summary>
        internal const int MaxExcerptLines = 40;

        // ===========================================================================
        //  1) Localizar la carpeta de usuario del juego
        // ===========================================================================

        /// <summary>
        /// Busca la carpeta de usuario de Los Sims 4 (donde vive <c>lastException.txt</c>).
        /// Prueba, por orden: la ruta pedida a propósito, la carpeta de Documentos del
        /// sistema, y un barrido corto por las carpetas habituales (incluida OneDrive,
        /// que redirige "Documentos" en muchas instalaciones recientes).
        ///
        /// <paramref name="extraCandidates"/> permite a los tests inyectar ubicaciones
        /// sin tocar el disco real.
        /// Para los tests, este método nunca debe lanzar: si algo falla devuelve null.
        /// </summary>
        /// <returns>Ruta de la carpeta de usuario, o null si no aparece.</returns>
        internal static string? TryFindUserFolder(IEnumerable<string>? extraCandidates = null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // a) Ubicaciones candidatas directas (Documentos, OneDrive, inyectadas…).
            foreach (string candidate in EnumerateCandidateRoots(extraCandidates))
            {
                string eaFolder = Path.Combine(candidate, "Electronic Arts");
                if (!Directory.Exists(eaFolder))
                    continue;

                string? found = FindGameUserFolderInEa(eaFolder, seen);
                if (found != null)
                    return found;
            }

            // b) Barrido corto por debajo de las carpetas candidatas, por si el juego
            //    terminó en un "Documentos\OneDrive\..." inesperado.
            foreach (string candidate in EnumerateCandidateRoots(extraCandidates))
            {
                string? found = SearchGameUserFolder(candidate, 0, seen, new int[1]);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Devuelve las carpetas "raíz" donde suele estar <c>Electronic Arts</c>.
        /// No lanza: si una consulta falla, simplemente se omite esa opción.
        /// </summary>
        internal static IEnumerable<string> EnumerateCandidateRoots(IEnumerable<string>? extraCandidates = null)
        {
            var roots = new List<string>();

            if (extraCandidates != null)
            {
                foreach (string? extra in extraCandidates)
                {
                    if (!string.IsNullOrWhiteSpace(extra))
                        roots.Add(extra);
                }
            }

            // Documentos según el sistema (puede venir ya redirigido a OneDrive).
            TryAdd(roots, SafeGet(() => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
            // OneDrive personal, si existe.
            TryAdd(roots, SafeGet(() => Environment.GetEnvironmentVariable("OneDrive")));
            TryAdd(roots, SafeGet(() => Environment.GetEnvironmentVariable("OneDriveConsumer")));
            // Valor de "Personal" en el Registro (Documentos redirigido a mano).
            TryAdd(roots, SafeGet(GetDocumentsFolderFromRegistry));
            // Ubicación clásica por si todo lo anterior viene vacío.
            TryAdd(roots, SafeGet(() => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents")));

            return roots.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Lee la carpeta "Personal" (Documentos) del Registro del usuario actual.</summary>
        private static string? GetDocumentsFolderFromRegistry()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders");
            return key?.GetValue("Personal") as string;
        }

        /// <summary>Dentro de "Electronic Arts", busca la carpeta del juego por su subcarpeta.</summary>
        private static string? FindGameUserFolderInEa(string eaFolder, HashSet<string> seen)
        {
            try
            {
                // Lo más fiable: la carpeta que contiene lastException/lastCrash/Mods.
                foreach (string sub in Directory.EnumerateDirectories(eaFolder))
                {
                    string name = Path.GetFileName(sub);
                    if (LooksLikeGameUserFolder(sub) ||
                        name.Contains("Sims", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Los Sims", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seen.Add(sub) && LooksLikeGameUserFolder(sub))
                            return sub;
                    }
                }

                // Si no hay hijos con pinta de carpeta de juego, el propio "Electronic Arts"
                // podría serlo en instalaciones raras.
                if (LooksLikeGameUserFolder(eaFolder))
                    return eaFolder;
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                // Carpeta ilegible: se considera no encontrada.
            }

            return null;
        }

        /// <summary>
        /// Pistas que indican "esta es la carpeta de usuario del juego": tener alguno de
        /// los archivos/carpetas típicos.
        /// </summary>
        private static bool LooksLikeGameUserFolder(string folder)
        {
            try
            {
                return File.Exists(Path.Combine(folder, "lastException.txt"))
                    || File.Exists(Path.Combine(folder, "lastCrash.txt"))
                    || File.Exists(Path.Combine(folder, "lastCleanException.txt"))
                    || File.Exists(Path.Combine(folder, "Options.ini"))
                    || Directory.Exists(Path.Combine(folder, "Mods"))
                    || Directory.Exists(Path.Combine(folder, "Saves"));
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                return false;
            }
        }

        /// <summary>Barrido corto por debajo de una carpeta, con tope de profundidad y de carpetas.</summary>
        private static string? SearchGameUserFolder(string folder, int depth, HashSet<string> seen, int[] budget)
        {
            if (depth > MaxScanDepth || budget[0] > MaxScanFolders)
                return null;

            try
            {
                foreach (string sub in Directory.EnumerateDirectories(folder))
                {
                    budget[0]++;
                    if (budget[0] > MaxScanFolders)
                        return null;

                    string name = Path.GetFileName(sub);

                    if (name.Equals("Electronic Arts", StringComparison.OrdinalIgnoreCase))
                    {
                        string? inEa = FindGameUserFolderInEa(sub, seen);
                        if (inEa != null)
                            return inEa;
                    }
                    else
                    {
                        string? deeper = SearchGameUserFolder(sub, depth + 1, seen, budget);
                        if (deeper != null)
                            return deeper;
                    }
                }
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                // Carpeta ilegible: se ignora esta rama.
            }

            return null;
        }

        // ===========================================================================
        //  2) Localizar el archivo de errores dentro de la carpeta del juego
        // ===========================================================================

        /// <summary>
        /// Busca <c>lastException.txt</c> en la carpeta del juego y en las ubicaciones
        /// conocidas (raíz y <c>Mods</c>, incluida una subcarpeta de Mods). Devuelve null
        /// si no aparece en ninguna.
        /// </summary>
        internal static string? FindLastExceptionFile(string gameUserFolder)
        {
            if (string.IsNullOrWhiteSpace(gameUserFolder) || !Directory.Exists(gameUserFolder))
                return null;

            foreach (string relative in new[]
                     {
                         "lastException.txt",
                         Path.Combine("Mods", "lastException.txt"),
                         Path.Combine("Mods", "Exceptions", "lastException.txt")
                     })
            {
                string path = Path.Combine(gameUserFolder, relative);
                try
                {
                    if (File.Exists(path))
                        return path;
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    // Ruta ilegible: se prueba la siguiente.
                }
            }

            return null;
        }

        // ===========================================================================
        //  3) Analizar el contenido
        // ===========================================================================

        /// <summary>
        /// Devuelve un análisis legible de <c>lastException.txt</c>. Si el archivo no
        /// existe devuelve cadena vacía; si algo falla, un mensaje entendible.
        /// </summary>
        public static string AnalyzeLastException(string lastExceptionPath)
        {
            if (string.IsNullOrWhiteSpace(lastExceptionPath) || !File.Exists(lastExceptionPath))
            {
                return string.Empty;
            }

            try
            {
                string[] lines = File.ReadAllLines(lastExceptionPath);
                if (lines.Length == 0)
                {
                    return "lastException.txt está vacío.";
                }

                var modCandidates = new List<string>();
                var scriptErrorLines = new List<string>();

                foreach (string raw in lines)
                {
                    string line = raw.TrimEnd();

                    if (line.IndexOf("Mod:", StringComparison.OrdinalIgnoreCase) >= 0)
                        ExtractMod(line, modCandidates);

                    if (line.IndexOf(".package", StringComparison.OrdinalIgnoreCase) >= 0)
                        ExtractPackage(line, modCandidates);

                    if (line.IndexOf("LastModule", StringComparison.OrdinalIgnoreCase) >= 0)
                        ExtractLastModule(line, modCandidates);

                    // Líneas de error de script: antes se detectaban pero NO se guardaban
                    // (el bloque estaba vacío). Ahora sí se recogen, sin duplicados.
                    if (ContainsErrorMarker(line)
                        && line.Length > 10 && line.Length < 250
                        && !scriptErrorLines.Contains(line, StringComparer.OrdinalIgnoreCase))
                    {
                        scriptErrorLines.Add(line);
                    }
                }

                var sb = new StringBuilder();
                var fi = new FileInfo(lastExceptionPath);

                sb.AppendLine($"Archivo: {lastExceptionPath}");
                sb.AppendLine($"Última modificación: {fi.LastWriteTime:yyyy-MM-dd HH:mm} ({DescribeAge(fi.LastWriteTime)})");
                sb.AppendLine();

                string? staleWarning = BuildStaleWarning(fi.LastWriteTime);
                if (staleWarning != null)
                {
                    sb.AppendLine(staleWarning);
                    sb.AppendLine();
                }

                string? exceptionType = ExtractExceptionType(lines);
                if (!string.IsNullOrWhiteSpace(exceptionType))
                {
                    sb.AppendLine("Tipo de excepción detectado:");
                    sb.AppendLine(exceptionType);
                    sb.AppendLine();
                }

                // El archivo suele empezar por XML (report/mods): se resume para que la IA
                // no tenga que adivinar entre etiquetas.
                string? xmlSummary = SummarizeXmlHeader(lines);
                if (xmlSummary != null)
                {
                    sb.AppendLine("Datos del informe:");
                    sb.AppendLine(xmlSummary);
                    sb.AppendLine();
                }

                if (scriptErrorLines.Count > 0)
                {
                    sb.AppendLine("Mensajes de error encontrados:");
                    foreach (string l in scriptErrorLines.Take(10))
                        sb.AppendLine($"- {l.Trim()}");
                    sb.AppendLine();
                }

                if (modCandidates.Count > 0)
                {
                    sb.AppendLine("Mod(s) sospechoso(s) encontrado(s):");
                    foreach (string m in modCandidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(5))
                        sb.AppendLine($"- {m}");
                    sb.AppendLine();
                }

                sb.AppendLine("Extracto completo (para que la IA confirme el diagnóstico):");
                int takeCount = Math.Min(MaxExcerptLines, lines.Length);
                for (int i = 0; i < takeCount; i++)
                    sb.AppendLine(lines[i]);

                if (lines.Length > MaxExcerptLines)
                    sb.AppendLine($"... (+{lines.Length - MaxExcerptLines} líneas más en el archivo)");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"No se pudo leer lastException.txt: {ex.Message}";
            }
        }

        /// <summary>
        /// Saca el tipo de excepción REAL. En el archivo de Los Sims 4 la traza es de
        /// Python y el tipo aparece como "AttributeError: ...", "TypeError: ...", etc.
        /// (antes se buscaba la primera línea con "Exception" y "Type", que solía ser una
        /// línea interna de comprobación de tipos: información engañosa).
        /// </summary>
        internal static string? ExtractExceptionType(IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return null;

            // 1) El tipo se escribe como "NombreError:" al final de la traza.
            string? fromTrace = lines.FirstOrDefault(line =>
                PythonExceptionPattern().IsMatch(line) && line.Trim().Length <= 200);
            if (fromTrace != null)
                return fromTrace.Trim();

            // 2) Caso .NET (menos común): "System.NullReferenceException: ...".
            string? fromDotNet = lines.FirstOrDefault(line =>
                line.IndexOf("Exception:", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf(".", StringComparison.Ordinal) > 0
                && line.Trim().Length <= 200);
            if (fromDotNet != null)
                return fromDotNet.Trim();

            // 3) Último recurso: la primera línea que mencione "Exception".
            string? fallback = lines.FirstOrDefault(line =>
                line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0
                && line.Trim().Length > 5 && line.Trim().Length <= 200);
            return fallback?.Trim();
        }

        /// <summary>Resume las etiquetas del XML inicial del informe (versión, sesión, mods…).</summary>
        internal static string? SummarizeXmlHeader(IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return null;

            var wanted = new[] { "version", "sessionid", "productsku", "build", "gamestartuptime", "mods" };
            var found = new List<string>();

            // Solo interesan las primeras líneas (el XML de cabecera).
            int limit = Math.Min(lines.Count, 10);
            for (int i = 0; i < limit; i++)
            {
                string line = lines[i];
                if (line.IndexOf('<') < 0)
                    continue;

                foreach (string tag in wanted)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        line, $"<{tag}[^>]*>\\s*([^<]+?)\\s*</{tag}>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && !found.Contains(match.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                    {
                        found.Add(match.Value.Trim());
                    }
                }
            }

            return found.Count == 0 ? null : string.Join(" | ", found);
        }

        /// <summary>Texto que avisa si el error es antiguo (puede no ser el problema actual).</summary>
        internal static string? BuildStaleWarning(DateTime lastWrite, DateTime? now = null)
        {
            DateTime reference = now ?? DateTime.Now;
            double days = (reference - lastWrite).TotalDays;
            if (days <= StaleErrorDays)
                return null;

            return days > 60
                ? $"⚠️ Este error es bastante antiguo (hace ~{Math.Round(days / 30)} meses): puede que ya no sea el problema actual."
                : $"⚠️ Este error tiene más de {StaleErrorDays} días: puede que ya no sea el problema actual.";
        }

        /// <summary>Describe la antigüedad del archivo en lenguaje natural (para el usuario).</summary>
        internal static string DescribeAge(DateTime lastWrite, DateTime? now = null)
        {
            DateTime reference = now ?? DateTime.Now;
            TimeSpan age = reference - lastWrite;

            if (age.TotalMinutes < 1) return "hace unos segundos";
            if (age.TotalHours < 1) return $"hace {(int)age.TotalMinutes} min";
            if (age.TotalDays < 1) return $"hace {(int)age.TotalHours} h";
            if (age.TotalDays < 30) return $"hace {(int)age.TotalDays} días";

            double months = Math.Round(age.TotalDays / 30);
            return months <= 1 ? "hace ~1 mes" : $"hace ~{months} meses";
        }

        // ===========================================================================
        //  Utilidades internas
        // ===========================================================================

        private static bool ContainsErrorMarker(string line)
        {
            return line.IndexOf("Script Error", StringComparison.OrdinalIgnoreCase) >= 0
                || PythonExceptionPattern().IsMatch(line)
                || line.IndexOf("Traceback", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf(".py\", line", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static System.Text.RegularExpressions.Regex PythonExceptionPattern() =>
            new(@"[A-Za-z_][A-Za-z0-9_]*Error\s*:", System.Text.RegularExpressions.RegexOptions.None);

        /// <summary>
        /// Fallos propios de consultar carpetas o el Registro: nunca deben romper la app,
        /// simplemente se ignora esa opción y se sigue con la siguiente.
        /// </summary>
        private static bool IsRecoverable(Exception ex) =>
            ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or ArgumentException
            or NotSupportedException;

        private static void ExtractMod(string line, List<string> candidates)
        {
            int idx = line.IndexOf("Mod:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;

            string rest = line.Substring(idx + 4).Trim();
            if (rest.Length == 0) return;

            string candidate = rest;
            if (candidate.IndexOf('(') > 0)
                candidate = candidate.Substring(0, candidate.IndexOf('(')).Trim();

            if (candidate.IndexOf('"') > 0)
            {
                candidate = candidate.Substring(candidate.IndexOf('"') + 1);
                if (candidate.IndexOf('"') > 0)
                    candidate = candidate.Substring(0, candidate.IndexOf('"'));
            }

            if (candidate.Length > 4 && !candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                candidates.Add(candidate);
        }

        private static void ExtractPackage(string line, List<string> candidates)
        {
            int idx = line.IndexOf(".package", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;

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
                candidates.Add(name);
        }

        private static void ExtractLastModule(string line, List<string> candidates)
        {
            int idx = line.IndexOf("LastModule:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;

            string rest = line.Substring(idx + "LastModule:".Length).Trim();
            if (rest.Length == 0) return;

            if (rest.Length > 4 && !candidates.Contains(rest, StringComparer.OrdinalIgnoreCase))
                candidates.Add(rest);
        }

        private static void TryAdd(List<string> roots, string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                roots.Add(path!);
        }

        private static string? SafeGet(Func<string?> getter)
        {
            try { return getter(); }
            catch (Exception ex) when (IsRecoverable(ex)) { return null; }
        }
    }
}
