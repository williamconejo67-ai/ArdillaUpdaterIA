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
}