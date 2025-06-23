using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LiviaAI.Services
{
    public interface IFileStorageService
    {
        Task<string> SaveFileAsync(string fileName, byte[] fileData, string mimeType);
        Task<byte[]> GetFileAsync(string filePath);
        Task<bool> DeleteFileAsync(string filePath);
        string GetFileUrl(string filePath);
    }

    public class FileStorageService : IFileStorageService
    {
        private readonly ILogger<FileStorageService> _logger;
        private readonly string _storagePath;
        private readonly string _baseUrl;

        public FileStorageService(ILogger<FileStorageService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _storagePath =
                configuration["FileStorage:Path"]
                ?? Path.Combine(Directory.GetCurrentDirectory(), "uploads");
            _baseUrl = configuration["FileStorage:BaseUrl"] ?? "http://localhost:5000/files";

            // Pastikan direktori storage ada
            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
                _logger.LogInformation("Created storage directory: {path}", _storagePath);
            }
        }

        public async Task<string> SaveFileAsync(string fileName, byte[] fileData, string mimeType)
        {
            try
            {
                // Buat nama file unik dengan timestamp
                var fileExtension = Path.GetExtension(fileName);
                var uniqueFileName =
                    $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{fileExtension}";
                var filePath = Path.Combine(_storagePath, uniqueFileName);

                // Simpan file
                await File.WriteAllBytesAsync(filePath, fileData);

                _logger.LogInformation(
                    "File saved successfully: {fileName} -> {filePath}",
                    fileName,
                    uniqueFileName
                );

                return uniqueFileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save file: {fileName}", fileName);
                throw;
            }
        }

        public async Task<byte[]> GetFileAsync(string filePath)
        {
            try
            {
                var fullPath = Path.Combine(_storagePath, filePath);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }

                return await File.ReadAllBytesAsync(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get file: {filePath}", filePath);
                throw;
            }
        }

        public async Task<bool> DeleteFileAsync(string filePath)
        {
            try
            {
                var fullPath = Path.Combine(_storagePath, filePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    _logger.LogInformation("File deleted successfully: {filePath}", filePath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file: {filePath}", filePath);
                return false;
            }
        }

        public string GetFileUrl(string filePath)
        {
            return $"{_baseUrl}/{filePath}";
        }
    }
}
