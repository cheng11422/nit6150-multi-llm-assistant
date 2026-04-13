using System;
using System.Collections.Generic;
using System.Linq;
using MultiLLMProjectAssistant.UI.Models;

namespace MultiLLMProjectAssistant.UI.Services
{
    public class MemoryService : IMemoryService
    {
        private readonly IMemoryStorage _storage;
        private List<MemoryNote> _notes;

        public MemoryService(IMemoryStorage storage)
        {
            _storage = storage;
            _notes = _storage.LoadNotes();
        }

        public void AddNote(string title, string content, List<string> tags)
        {
            var note = MemoryNote.Create(title, content, tags);
            _notes.Add(note);
            _storage.SaveNotes(_notes);
        }

        public void EditNote(string id, string title, string content, List<string> tags)
        {
            var note = _notes.FirstOrDefault(n => n.Id == id);
            if (note == null) return;

            note.Title = title;
            note.Content = content;
            note.Tags = tags ?? new List<string>();
            note.UpdatedAt = DateTime.UtcNow;
            _storage.SaveNotes(_notes);
        }

        public void DeleteNote(string id)
        {
            _notes.RemoveAll(n => n.Id == id);
            _storage.SaveNotes(_notes);
        }

        public List<MemoryNote> GetAllNotes()
        {
            return _notes;
        }

        public List<MemoryNote> SearchNotes(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return _notes;

            var lower = keyword.ToLower();
            return _notes.Where(n =>
                n.Title.ToLower().Contains(lower) ||
                n.Content.ToLower().Contains(lower) ||
                n.Tags.Any(t => t.ToLower().Contains(lower))
            ).ToList();
        }

        public List<MemoryNote> FilterByTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return _notes;

            return _notes.Where(n =>
                n.Tags.Any(t => t.ToLower() == tag.ToLower())
            ).ToList();
        }

        public List<MemoryNote> GetMostRelevant(string prompt, int topK)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return _notes.Take(topK).ToList();

            var words = prompt.ToLower().Split(' ',
                StringSplitOptions.RemoveEmptyEntries);

            var scored = _notes.Select(n =>
            {
                var text = $"{n.Title} {n.Content} {string.Join(" ", n.Tags)}"
                           .ToLower();
                var score = words.Sum(w =>
                    text.Split(' ').Count(t => t == w));
                return new { Note = n, Score = score };
            });

            return scored
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select(x => x.Note)
                .ToList();
        }
    }
}