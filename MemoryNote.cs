using System;
using System.Collections.Generic;

namespace MultiLLMProjectAssistant.UI.Models
{
    public class MemoryNote
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public static MemoryNote Create(string title, string content, List<string> tags)
        {
            return new MemoryNote
            {
                Id = GenerateId(),
                Title = title,
                Content = content,
                Tags = tags ?? new List<string>(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static string GenerateId()
        {
            var random = new Random();
            return "MEM-" + random.Next(1000, 9999).ToString();
        }
    }
}