using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MultiLLMProjectAssistant.UI.Models;

namespace MultiLLMProjectAssistant.UI.Services
{
    public class FileStorageService : IMemoryStorage
    {
        private readonly string _filePath;

        public FileStorageService(string projectPath)
        {
            var memoryDir = Path.Combine(projectPath, "memory");
            Directory.CreateDirectory(memoryDir);
            _filePath = Path.Combine(memoryDir, "items.json");
        }

        public List<MemoryNote> LoadNotes()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new List<MemoryNote>();

                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<MemoryNote>>(json)
                       ?? new List<MemoryNote>();
            }
            catch (Exception)
            {
                return new List<MemoryNote>();
            }
        }

        public void SaveNotes(List<MemoryNote> notes)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(notes, options);
            File.WriteAllText(_filePath, json);
        }
    }
}