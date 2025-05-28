using System.Collections.Generic;

namespace LiviaAI.Models.Gemini
{
    public class ChatHistory
    {
        public string ChatId { get; set; } = string.Empty;
        public List<string> Messages { get; set; } = new List<string>();
    }
}
