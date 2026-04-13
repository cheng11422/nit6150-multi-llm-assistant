using System.Collections.Generic;
using MultiLLMProjectAssistant.UI.Models;

namespace MultiLLMProjectAssistant.UI.Services
{
    public interface IMemoryService
    {
        void AddNote(string title, string content, List<string> tags);
        void EditNote(string id, string title, string content, List<string> tags);
        void DeleteNote(string id);
        List<MemoryNote> SearchNotes(string keyword);
        List<MemoryNote> FilterByTag(string tag);
        List<MemoryNote> GetMostRelevant(string prompt, int topK);
        List<MemoryNote> GetAllNotes();
    }
}