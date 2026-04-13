using System.Collections.Generic;
using MultiLLMProjectAssistant.UI.Models;

namespace MultiLLMProjectAssistant.UI.Services
{
    public interface IMemoryStorage
    {
        List<MemoryNote> LoadNotes();
        void SaveNotes(List<MemoryNote> notes);
    }
}