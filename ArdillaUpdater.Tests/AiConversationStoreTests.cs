using System;
using System.Collections.Generic;
using System.IO;
using ArdillaUpdater.AI;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ArdillaUpdater.Tests;

public sealed class AiConversationStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "ardilla_conv_" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Save_ThenLoad_RoundTripsMessages()
    {
        var history = new List<object>
        {
            new { role = "user", content = "hola" },
            new { role = "assistant", content = "¿En qué te ayudo?" }
        };

        AiConversationStore.Save(history, _path);
        var loaded = AiConversationStore.Load(_path);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("user", ArdillaAiService.GetRole(loaded[0]));
        Assert.Equal("hola", ((JToken)loaded[0])["content"]!.ToString());
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsEmpty()
    {
        Assert.Empty(AiConversationStore.Load(_path));
    }

    [Fact]
    public void Clear_RemovesFile()
    {
        AiConversationStore.Save(new List<object> { new { role = "user", content = "x" } }, _path);
        AiConversationStore.Clear(_path);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void DefaultPath_IsInLocalAppData_NotNextToExe()
    {
        // PR #1 auditoría 2026-10: la conversación ya no vive junto al .exe.
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArdillaUpdater",
            "ai_conversation.json");

        Assert.Equal(expected, AiConversationStore.DefaultPath);
    }

    [Fact]
    public void Save_UsesAtomicWrite_AndLeavesNoTmpFile()
    {
        AiConversationStore.Save(new List<object> { new { role = "user", content = "uno" } }, _path);
        AiConversationStore.Save(new List<object> { new { role = "user", content = "dos" } }, _path);

        Assert.False(File.Exists(_path + ".tmp"));
        var loaded = AiConversationStore.Load(_path);
        Assert.Single(loaded);
        Assert.Equal("dos", ((JToken)loaded[0])["content"]!.ToString());
    }

    [Fact]
    public void MigratesLegacyConversationNextToExe_OnlyOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "ardilla_migr_" + Guid.NewGuid().ToString("N"));
        try
        {
            string oldPath = Path.Combine(root, "exe", "ai_conversation.json");
            string newPath = Path.Combine(root, "appdata", "ai_conversation.json");
            Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
            File.WriteAllText(oldPath, """[{"role":"user","content":"hola"}]""");

            Assert.True(AppPaths.TryMigrateLegacyFile(oldPath, newPath));
            Assert.Single(AiConversationStore.Load(newPath));

            // Segunda vez: ya existe la nueva → no re-migra.
            File.WriteAllText(newPath, "[]");
            Assert.False(AppPaths.TryMigrateLegacyFile(oldPath, newPath));
            Assert.Empty(AiConversationStore.Load(newPath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
        }
    }
}
