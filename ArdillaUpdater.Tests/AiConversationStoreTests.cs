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
