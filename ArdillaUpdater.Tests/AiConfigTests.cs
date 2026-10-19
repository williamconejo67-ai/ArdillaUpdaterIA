using System;
using System.IO;
using ArdillaUpdater.AI;
using Xunit;

namespace ArdillaUpdater.Tests;

public sealed class AiConfigTests : IDisposable
{
    private readonly string _tempRoot;

    public AiConfigTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ArdillaUpdater.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private string ConfigPath => Path.Combine(_tempRoot, "ai_config.json");

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new AiConfig();

        Assert.Equal("http://localhost:11434", config.Endpoint);
        Assert.Equal(string.Empty, config.ApiKey);
        Assert.Equal("nemotron-3-nano:30b-cloud", config.Model);
        Assert.Equal(20, config.MaxHistoryMessages);
        Assert.Equal(0.5, config.Temperature);
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaultsAndCreatesFile()
    {
        var config = AiConfig.Load(ConfigPath);

        Assert.Equal("http://localhost:11434", config.Endpoint);
        Assert.Equal("nemotron-3-nano:30b-cloud", config.Model);
        Assert.True(File.Exists(ConfigPath));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsAllValues()
    {
        var original = new AiConfig
        {
            Endpoint = "http://otro-servidor:1234",
            ApiKey = "token-123",
            Model = "gemma3:4b",
            MaxHistoryMessages = 7,
            Temperature = 0.9
        };

        original.Save(ConfigPath);
        var loaded = AiConfig.Load(ConfigPath);

        Assert.Equal("http://otro-servidor:1234", loaded.Endpoint);
        Assert.Equal("token-123", loaded.ApiKey);
        Assert.Equal("gemma3:4b", loaded.Model);
        Assert.Equal(7, loaded.MaxHistoryMessages);
        Assert.Equal(0.9, loaded.Temperature);
    }

    [Fact]
    public void Load_WithDeprecatedModel_MigratesToDefaultButKeepsOtherValues()
    {
        File.WriteAllText(ConfigPath,
            """{"Endpoint":"http://localhost:11434","ApiKey":"","Model":"deepseek-v3.1-custom","MaxHistoryMessages":10,"Temperature":0.7}""");

        var config = AiConfig.Load(ConfigPath);

        Assert.Equal("nemotron-3-nano:30b-cloud", config.Model);
        Assert.Equal(10, config.MaxHistoryMessages);
        Assert.Equal(0.7, config.Temperature);
    }

    [Fact]
    public void Load_WithCorruptedJson_FallsBackToDefaults()
    {
        File.WriteAllText(ConfigPath, "{ esto no es json valido ]");

        var config = AiConfig.Load(ConfigPath);

        Assert.Equal("http://localhost:11434", config.Endpoint);
        Assert.Equal("nemotron-3-nano:30b-cloud", config.Model);
    }

    [Fact]
    public void GetAvailableModels_ReadsTrimmedNonCommentLines()
    {
        string modelsPath = Path.Combine(_tempRoot, "Models.txt");
        File.WriteAllLines(modelsPath, new[]
        {
            "# comentario que debe ignorarse",
            "",
            "  gemma3:4b  ",
            "qwen3.5:0.8b"
        });

        var models = AiConfig.GetAvailableModels(modelsPath);

        Assert.Equal(new[] { "gemma3:4b", "qwen3.5:0.8b" }, models);
    }

    [Fact]
    public void GetAvailableModels_WhenFileMissing_ReturnsBuiltInDefaults()
    {
        var models = AiConfig.GetAvailableModels(Path.Combine(_tempRoot, "no-existe.txt"));

        Assert.NotEmpty(models);
        Assert.Contains("nemotron-3-nano:30b-cloud", models);
    }

    [Fact]
    public void DefaultConfigPath_IsInLocalAppData_NotNextToExe()
    {
        // PR #1 auditoría 2026-10: la configuración ya no vive junto al .exe.
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArdillaUpdater",
            "ai_config.json");

        Assert.Equal(expected, AiConfig.DefaultConfigPath);
        Assert.DoesNotContain(AppDomain.CurrentDomain.BaseDirectory, AiConfig.DefaultConfigPath);
    }

    [Fact]
    public void Load_MigratesLegacyConfigNextToExe_OnlyOnce()
    {
        // Simula una instalación vieja: la config aún está en la carpeta del .exe
        // (aquí "exe") y la ruta nueva ("appdata") está vacía.
        string legacyDir = Path.Combine(_tempRoot, "exe");
        string oldPath = Path.Combine(legacyDir, "ai_config.json");
        string newPath = Path.Combine(_tempRoot, "appdata", "ai_config.json");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(oldPath, """{"Endpoint":"http://legacy:1234","Model":"modelo-viejo"}""");

        Assert.True(AppPaths.TryMigrateLegacyFile(oldPath, newPath));

        // El original no se toca y la copia se puede leer.
        Assert.True(File.Exists(oldPath));
        var migrated = AiConfig.Load(newPath);
        Assert.Equal("http://legacy:1234", migrated.Endpoint);
        Assert.Equal("modelo-viejo", migrated.Model);

        // Segunda vez: ya existe la nueva → no debe migrar (no pisar cambios).
        File.WriteAllText(newPath, """{"Endpoint":"http://nuevo:5678"}""");
        Assert.False(AppPaths.TryMigrateLegacyFile(oldPath, newPath));
        Assert.Equal("http://nuevo:5678", AiConfig.Load(newPath).Endpoint);
    }

    [Fact]
    public void Load_WithCorruptedJson_KeepsBackupBeforeRegenerating()
    {
        File.WriteAllText(ConfigPath, "{ esto no es json valido ]");

        var config = AiConfig.Load(ConfigPath);

        // Se regenera con valores por defecto...
        Assert.Equal("http://localhost:11434", config.Endpoint);
        // ...pero el contenido ilegible se conserva en .bak (antes se perdía).
        Assert.True(File.Exists(ConfigPath + ".bak"));
        Assert.Equal("{ esto no es json valido ]", File.ReadAllText(ConfigPath + ".bak"));
    }

    [Fact]
    public void Save_UsesAtomicWrite_AndLeavesNoTmpFile()
    {
        new AiConfig { Endpoint = "http://uno:1" }.Save(ConfigPath);
        // Segunda escritura sobre el mismo archivo usa File.Replace.
        new AiConfig { Endpoint = "http://dos:2" }.Save(ConfigPath);

        Assert.False(File.Exists(ConfigPath + ".tmp"));
        Assert.Equal("http://dos:2", AiConfig.Load(ConfigPath).Endpoint);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        string path = Path.Combine(_tempRoot, "carpeta-que-no-existe", "ai_config.json");

        new AiConfig { Endpoint = "http://x" }.Save(path);

        Assert.True(File.Exists(path));
    }
}