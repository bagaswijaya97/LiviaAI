using System.Collections.Generic;

namespace LiviaAI.Models.Gemini
{
    public class ChatTurn
    {
        public string UserMessage { get; set; } = string.Empty;
        public string LiviaResponse { get; set; } = string.Empty;
    }

    public class ChatHistory
    {
        public string ChatId { get; set; } = string.Empty;
        public List<ChatTurn> Turns { get; set; } = new List<ChatTurn>();
    }
}
