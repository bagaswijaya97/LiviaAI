using System;
using System.Collections.Generic;
using System.Linq;
using LiviaAI.Models.Gemini;
using Microsoft.Extensions.Caching.Memory;

namespace LiviaAI.Services
{
    public class ChatHistoryService
    {
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromDays(7);
        private const string SESSION_KEYS_CACHE_KEY = "gemini:session_keys";

        public ChatHistoryService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public ChatHistory GetOrCreateHistory(string sessionId)
        {
            var chatHistoryKey = $"gemini:history:{sessionId}";
            if (
                !_cache.TryGetValue(chatHistoryKey, out ChatHistory? chatHistory)
                || chatHistory == null
            )
            {
                chatHistory = new ChatHistory 
                { 
                    chat_id = sessionId,
                    turns = new List<ChatTurn>(),
                    created_at = DateTime.UtcNow,
                    last_updated = DateTime.UtcNow
                };
                _cache.Set(chatHistoryKey, chatHistory, _cacheDuration);
                
                // Track session keys
                AddSessionKey(sessionId);
            }
            return chatHistory;
        }

        public void SaveHistory(string sessionId, ChatHistory chatHistory)
        {
            var chatHistoryKey = $"gemini:history:{sessionId}";
            chatHistory.last_updated = DateTime.UtcNow;
            if (string.IsNullOrEmpty(chatHistory.chat_id))
            {
                chatHistory.chat_id = sessionId;
            }
            _cache.Set(chatHistoryKey, chatHistory, _cacheDuration);
            
            // Track session keys
            AddSessionKey(sessionId);
        }

        public string GetCacheKey(string sessionId)
        {
            return $"gemini:{sessionId}";
        }

        public bool TryGetCachedHtml(string sessionId, out string? html)
        {
            var cacheKey = GetCacheKey(sessionId);
            return _cache.TryGetValue(cacheKey, out html);
        }

        public void SaveCachedHtml(string sessionId, string html)
        {
            var cacheKey = GetCacheKey(sessionId);
            _cache.Set(cacheKey, html, _cacheDuration);
        }

        public List<ChatHistorySummary> GetAllHistories()
        {
            var histories = new List<ChatHistorySummary>();
            
            // Get tracked session keys
            var sessionKeys = GetSessionKeys();
            
            foreach (var sessionId in sessionKeys)
            {
                var chatHistory = GetHistoryBySessionId(sessionId);
                if (chatHistory != null && chatHistory.turns.Any())
                {
                    var summary = new ChatHistorySummary
                    {
                        session_id = sessionId,
                        chat_id = chatHistory.chat_id,
                        user_message = chatHistory.turns.First().user_message, // Hanya chat pertama kali
                        created_at = chatHistory.created_at,
                        last_updated = chatHistory.last_updated,
                        total_turns = chatHistory.turns.Count
                    };
                    histories.Add(summary);
                }
            }
            
            return histories.OrderByDescending(h => h.last_updated).ToList();
        }

        public ChatHistory? GetHistoryBySessionId(string sessionId)
        {
            var chatHistoryKey = $"gemini:history:{sessionId}";
            if (_cache.TryGetValue(chatHistoryKey, out ChatHistory? chatHistory))
            {
                return chatHistory;
            }
            return null;
        }

        public bool DeleteHistory(string sessionId)
        {
            var chatHistoryKey = $"gemini:history:{sessionId}";
            _cache.Remove(chatHistoryKey);
            
            var cacheKey = GetCacheKey(sessionId);
            _cache.Remove(cacheKey);
            
            // Remove from tracked session keys
            RemoveSessionKey(sessionId);
            
            return true;
        }
        
        private void AddSessionKey(string sessionId)
        {
            var sessionKeys = GetSessionKeys();
            if (!sessionKeys.Contains(sessionId))
            {
                sessionKeys.Add(sessionId);
                _cache.Set(SESSION_KEYS_CACHE_KEY, sessionKeys, _cacheDuration);
            }
        }
        
        private void RemoveSessionKey(string sessionId)
        {
            var sessionKeys = GetSessionKeys();
            if (sessionKeys.Remove(sessionId))
            {
                _cache.Set(SESSION_KEYS_CACHE_KEY, sessionKeys, _cacheDuration);
            }
        }
        
        private List<string> GetSessionKeys()
        {
            if (_cache.TryGetValue(SESSION_KEYS_CACHE_KEY, out List<string>? sessionKeys) && sessionKeys != null)
            {
                return sessionKeys;
            }
            return new List<string>();
        }
    }
}
