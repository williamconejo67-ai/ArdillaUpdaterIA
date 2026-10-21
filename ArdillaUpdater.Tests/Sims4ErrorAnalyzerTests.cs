using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArdillaUpdater.AI;
using Xunit;

namespace ArdillaUpdater.Tests;

/// <summary>
/// Pruebas del diagnóstico de errores de Los Sims 4 (Lote D de la auditoría interna).
/// Se centran en la parte de análisis, que es lógica pura y no depende del equipo del
/// usuario. La búsqueda en disco real solo se comprueba de forma básica (no debe lanzar).
/// </summary>
public sealed class Sims4ErrorAnalyzerTests : IDisposable
{
    private readonly string _tempRoot;

    public Sims4ErrorAnalyzerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ArdillaUpdater.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    /// <summary>lastException.txt real: cabecera XML + traza de Python + mod sospechoso.</summary>
    private static readonly string[] RealisticLastException =
    [
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><root>",
        "<report>",
        "<version>1.108.318.1020</version>",
        "<sessionId>00000000-0000-0000-0000-000000000000</sessionId>",
        "<productSku>124</productSku>",
        "</report>",
        "<mods>",
        "<mod>Mod: mc_cmd_center.package</mod>",
        "<mod>Mod: ui_cheats_extension.ts4script</mod>",
        "</mods>",
        "</root>",
        "Traceback (most recent call last):",
        "  File \"T:\\InGame\\Gameplay\\Scripts\\Server\\zone.py\", line 517, in update",
        "  File \"T:\\InGame\\Gameplay\\Scripts\\Core\\sims4\\utils.py\", line 179, in wrapper",
        "AttributeError: 'NoneType' object has no attribute 'get_plex_zone_id'"
    ];

    private string WriteLastException(params string[] lines)
    {
        string path = Path.Combine(_tempRoot, "lastException.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    // ---------------------------------------------------------------- análisis

    [Fact]
    public void AnalyzeLastException_WithRealisticFile_DetectsPythonExceptionType()
    {
        // Antes se buscaba la primera línea con "Exception" y "Type", que en este archivo
        // no existe: se colaba información equivocada. Ahora debe salir el tipo real.
        string path = WriteLastException(RealisticLastException);

        string result = Sims4ErrorAnalyzer.AnalyzeLastException(path);

        Assert.Contains("AttributeError", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeLastException_WithRealisticFile_DetectsSuspiciousMods()
    {
        string path = WriteLastException(RealisticLastException);

        string result = Sims4ErrorAnalyzer.AnalyzeLastException(path);

        Assert.Contains("mc_cmd_center.package", result, StringComparison.Ordinal);
        Assert.Contains("ui_cheats_extension.ts4script", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeLastException_WithRealisticFile_SummarizesXmlHeader()
    {
        string path = WriteLastException(RealisticLastException);

        string result = Sims4ErrorAnalyzer.AnalyzeLastException(path);

        Assert.Contains("Datos del informe", result, StringComparison.Ordinal);
        Assert.Contains("1.108.318.1020", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeLastException_WithRealisticFile_IncludesExcerptForTheAi()
    {
        string path = WriteLastException(RealisticLastException);

        string result = Sims4ErrorAnalyzer.AnalyzeLastException(path);

        Assert.Contains("Extracto completo", result, StringComparison.Ordinal);
        Assert.Contains("Traceback (most recent call last):", result, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeLastException_WithEmptyFile_ReportsEmpty()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "lastException.txt"), string.Empty);

        string result = Sims4ErrorAnalyzer.AnalyzeLastException(Path.Combine(_tempRoot, "lastException.txt"));

        Assert.Contains("vacío", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalyzeLastException_WhenFileMissing_ReturnsEmptyString()
    {
        string result = Sims4ErrorAnalyzer.AnalyzeLastException(Path.Combine(_tempRoot, "no-existe.txt"));

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void AnalyzeLastException_IncludesAllScriptErrorMessages()
    {
        // El bloque que recogía "Script Error" estaba vacío en el código original
        // (detectaba las líneas y no guardaba nada): ahora deben aparecer.
        string path = WriteLastException(
            "<root></root>",
            "Script Error: cannot find interaction 'foo'",
            "TypeError: unsupported operand type(s) for +: 'int' and 'str'");

        string result = Sims4ErrorAnalyzer.AnalyzeLastException(path);

        Assert.Contains("Mensajes de error encontrados", result, StringComparison.Ordinal);
        Assert.Contains("cannot find interaction 'foo'", result, StringComparison.Ordinal);
        Assert.Contains("TypeError", result, StringComparison.Ordinal);
    }

    // ------------------------------------------------------- tipo de excepción

    [Fact]
    public void ExtractExceptionType_PrefersPythonErrorFromTrace()
    {
        string? type = Sims4ErrorAnalyzer.ExtractExceptionType(RealisticLastException);

        Assert.NotNull(type);
        Assert.Contains("AttributeError", type, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractExceptionType_RecognizesDotNetExceptionAsFallback()
    {
        var lines = new List<string>
        {
            "System.NullReferenceException: Object reference not set to an instance of an object."
        };

        string? type = Sims4ErrorAnalyzer.ExtractExceptionType(lines);

        Assert.Equal("System.NullReferenceException: Object reference not set to an instance of an object.", type);
    }

    [Fact]
    public void ExtractExceptionType_WhenNothingLooksLikeAnError_ReturnsNull()
    {
        var lines = new List<string> { "<root>", "<version>1.0</version>", "</root>" };

        Assert.Null(Sims4ErrorAnalyzer.ExtractExceptionType(lines));
    }

    // ------------------------------------------------------------ antigüedad

    [Fact]
    public void BuildStaleWarning_ForFreshFile_ReturnsNull()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0);

        string? warning = Sims4ErrorAnalyzer.BuildStaleWarning(now.AddDays(-2), now);

        Assert.Null(warning);
    }

    [Fact]
    public void BuildStaleWarning_ForOldFile_WarnsItMayNotBeCurrent()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0);

        string? warning = Sims4ErrorAnalyzer.BuildStaleWarning(now.AddDays(-90), now);

        Assert.NotNull(warning);
        Assert.Contains("antiguo", warning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeAge_UsesFriendlyUnits()
    {
        var now = new DateTime(2026, 10, 20, 12, 0, 0);

        Assert.Contains("min", Sims4ErrorAnalyzer.DescribeAge(now.AddMinutes(-30), now), StringComparison.Ordinal);
        Assert.Contains("días", Sims4ErrorAnalyzer.DescribeAge(now.AddDays(-5), now), StringComparison.Ordinal);
        Assert.Contains("meses", Sims4ErrorAnalyzer.DescribeAge(now.AddDays(-95), now), StringComparison.Ordinal);
    }

    // --------------------------------------------------- localización de archivo

    [Fact]
    public void FindLastExceptionFile_FindsFileInModsFolder()
    {
        // El archivo no siempre está en la raíz: también aparece bajo Mods.
        string gameFolder = Path.Combine(_tempRoot, "The Sims 4");
        string modsFolder = Path.Combine(gameFolder, "Mods");
        Directory.CreateDirectory(modsFolder);
        string expected = Path.Combine(modsFolder, "lastException.txt");
        File.WriteAllText(expected, "contenido");

        string? found = Sims4ErrorAnalyzer.FindLastExceptionFile(gameFolder);

        Assert.Equal(expected, found);
    }

    [Fact]
    public void FindLastExceptionFile_WhenNoFile_ReturnsNull()
    {
        string gameFolder = Path.Combine(_tempRoot, "The Sims 4");
        Directory.CreateDirectory(gameFolder);

        Assert.Null(Sims4ErrorAnalyzer.FindLastExceptionFile(gameFolder));
    }

    // ------------------------------------------------------- búsqueda de carpeta

    [Fact]
    public void TryFindUserFolder_WithInjectedCandidate_FindsGameFolder()
    {
        // Carpeta con nombre NO inglés ni español ("Die Sims 4"): antes no se encontraba
        // porque solo se probaban dos rutas exactas.
        string docs = Path.Combine(_tempRoot, "OneDrive", "Dokumente");
        string gameFolder = Path.Combine(docs, "Electronic Arts", "Die Sims 4");
        Directory.CreateDirectory(Path.Combine(gameFolder, "Mods"));
        File.WriteAllText(Path.Combine(gameFolder, "lastException.txt"), "x");

        string? found = Sims4ErrorAnalyzer.TryFindUserFolder(new[] { docs });

        Assert.Equal(gameFolder, found);
    }

    [Fact]
    public void TryFindUserFolder_WithNoGameAnywhere_ReturnsNull()
    {
        string emptyDocs = Path.Combine(_tempRoot, "vacio");
        Directory.CreateDirectory(emptyDocs);

        // Pasar el candidato vacío no debe hacer que aparezca por otro lado.
        string? found = Sims4ErrorAnalyzer.TryFindUserFolder(new[] { emptyDocs });

        Assert.True(found == null || !found.Contains(_tempRoot, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------ informe completo (Lote E)

    [Fact]
    public void BuildDiagnosticReport_NeverThrows_AndReturnsSomethingUseful()
    {
        // El informe completo se apoya en la búsqueda real de carpetas, así que no se
        // puede fijar su contenido exacto (depende del equipo). Lo que sí se garantiza:
        // nunca lanza y siempre devuelve un texto con el que trabajar (bien un informe,
        // bien un mensaje claro de "no se encontró el juego").
        string report = Sims4ErrorAnalyzer.BuildDiagnosticReport();

        Assert.False(string.IsNullOrWhiteSpace(report));
    }

    [Fact]
    public void BuildDiagnosticReport_UsesLogCallbackWithoutCrashing()
    {
        var logged = new List<string>();

        string report = Sims4ErrorAnalyzer.BuildDiagnosticReport(logged.Add);

        Assert.False(string.IsNullOrWhiteSpace(report));
        // El log es opcional: puede no recibir nada si todo fue bien. Lo importante es
        // que pasar un callback no rompe el diagnóstico.
    }
}
