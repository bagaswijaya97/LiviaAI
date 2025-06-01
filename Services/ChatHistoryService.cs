using System;
using System.Collections.Generic;
using LiviaAI.Models.Gemini;
using Microsoft.Extensions.Caching.Memory;

namespace LiviaAI.Services
{
    public class ChatHistoryService
    {
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromDays(7);

        public ChatHistoryService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public ChatHistory GetOrCreateHistory(string sessionId)
        {
            var chatHistoryKey = $"gemini:history:{sessionId}";
            if (
                !_cache.TryGetValue(chatHistoryKey, out ChatHistory chatHistory)
                || chatHistory == null
            )
            {
                chatHistory = new ChatHistory { Turns = new List<ChatTurn>() };
                _cache.Set(chatHistoryKey, chatHistory, _cacheDuration);
            }
            return chatHistory;
        }

        public void SaveHistory(string sessionId, ChatHistory chatHistory)
        {
            var chatHistoryKey = $"gemini:history:{sessionId}";
            _cache.Set(chatHistoryKey, chatHistory, _cacheDuration);
        }

        public string GetCacheKey(string sessionId)
        {
            return $"gemini:{sessionId}";
        }

        public bool TryGetCachedHtml(string sessionId, out string html)
        {
            var cacheKey = GetCacheKey(sessionId);
            return _cache.TryGetValue(cacheKey, out html);
        }

        public void SaveCachedHtml(string sessionId, string html)
        {
            var cacheKey = GetCacheKey(sessionId);
            _cache.Set(cacheKey, html, _cacheDuration);
        }
    }
}
