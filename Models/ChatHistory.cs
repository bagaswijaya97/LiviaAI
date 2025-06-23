using System;
using System.Collections.Generic;
using System.Linq;

namespace LiviaAI.Models.Gemini
{
    public class FileAttachment
    {
        public string file_name { get; set; } = string.Empty;
        public string file_type { get; set; } = string.Empty; // image, pdf, document, etc.
        public string mime_type { get; set; } = string.Empty;
        public long file_size { get; set; } // dalam bytes
        public string file_url { get; set; } = string.Empty; // jika disimpan di storage
        public string file_base64 { get; set; } = string.Empty; // jika disimpan sebagai base64
        public DateTime uploaded_at { get; set; } = DateTime.UtcNow;
    }

    public class ChatTurn
    {
        public string user_message { get; set; } = string.Empty;
        public string livia_response { get; set; } = string.Empty;
        public List<FileAttachment> attachments { get; set; } = new List<FileAttachment>();
        public bool has_attachments => attachments.Any();
    }

    public class ChatHistory
    {
        public string chat_id { get; set; } = string.Empty;
        public List<ChatTurn> turns { get; set; } = new List<ChatTurn>();
        public DateTime created_at { get; set; } = DateTime.UtcNow;
        public DateTime last_updated { get; set; } = DateTime.UtcNow;
    }

    public class ChatHistorySummary
    {
        public string session_id { get; set; } = string.Empty;
        public string chat_id { get; set; } = string.Empty;
        public string user_message { get; set; } = string.Empty;
        public int total_turns { get; set; }
        public string last_message { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public DateTime last_updated { get; set; }
    }

    public class ChatHistoryDetailResponse
    {
        public string session_id { get; set; } = string.Empty;
        public string chat_id { get; set; } = string.Empty;
        public List<ChatTurn> turns { get; set; } = new List<ChatTurn>();
        public DateTime created_at { get; set; }
        public DateTime last_updated { get; set; }
        public int total_turns { get; set; }
    }
}
