using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ArdillaUpdater.AI
{
    internal class AiConfig
    {
        public string Endpoint { get; set; } = "http://localhost:11434";
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "nemotron-3-nano:30b-cloud";
        public int MaxHistoryMessages { get; set; } = 20;
        public double Temperature { get; set; } = 0.5;

        private static string DefaultConfigPath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ai_config.json"
        );

        public static List<string> GetAvailableModels(string? modelsFilePath = null)
        {
            var list = new List<string>();
            try
            {
                string modelsPath = modelsFilePath
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models.txt");
                if (modelsFilePath == null && !File.Exists(modelsPath))
                {
                    var parentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Models.txt");
                    if (File.Exists(parentPath)) modelsPath = parentPath;
                }

                if (File.Exists(modelsPath))
                {
                    var lines = File.ReadAllLines(modelsPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                        {
                            list.Add(trimmed);
                        }
                    }
                }
            }
            catch
            {
            }

            if (list.Count == 0)
            {
                list.AddRange(new[]
                {
                    "nemotron-3-nano:30b-cloud",
                    "nemotron-3-super:cloud",
                    "nemotron-3-ultra:cloud",
                    "gemma4:31b-cloud",
                    "gpt-oss:120b-cloud",
                    "gpt-oss:20b-cloud",
                    "gemma3:4b",
                    "dolphin-mistral:latest",
                    "qwen3.5:0.8b"
                });
            }

            return list;
        }

        public static AiConfig Load(string? configPath = null)
        {
            string path = configPath ?? DefaultConfigPath;
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var config = JsonConvert.DeserializeObject<AiConfig>(json);
                    if (config != null)
                    {
                        // Migrar automáticamente si el modelo guardado era el modelo retirado
                        if (string.IsNullOrWhiteSpace(config.Model) ||
                            config.Model.Contains("deepseek-v3.1", StringComparison.OrdinalIgnoreCase))
                        {
                            config.Model = "nemotron-3-nano:30b-cloud";
                            config.Save(path);
                        }
                        return config;
                    }
                }
            }
            catch
            {
                // Fallback silencioso con valores por defecto
            }

            var defaultConfig = new AiConfig();
            defaultConfig.Save(path);
            return defaultConfig;
        }

        public void Save(string? configPath = null)
        {
            string path = configPath ?? DefaultConfigPath;
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignorar error de escritura en disco protegido
            }
        }
    }
}
