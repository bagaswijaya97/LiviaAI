using Microsoft.AspNetCore.Mvc;
using LiviaAI.Services;
using Microsoft.AspNetCore.Authorization;

namespace LiviaAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FilesController : ControllerBase
    {
        private readonly IFileStorageService _fileStorageService;
        private readonly ILogger<FilesController> _logger;

        public FilesController(IFileStorageService fileStorageService, ILogger<FilesController> logger)
        {
            _fileStorageService = fileStorageService;
            _logger = logger;
        }

        /// <summary>
        /// Endpoint untuk mengakses file yang disimpan
        /// </summary>
        [HttpGet("{fileName}")]
        public async Task<IActionResult> GetFile(string fileName)
        {
            try
            {
                var fileData = await _fileStorageService.GetFileAsync(fileName);
                
                // Tentukan content type berdasarkan ekstensi file
                var contentType = GetContentType(fileName);
                
                return File(fileData, contentType);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning("File not found: {fileName}", fileName);
                return NotFound(new { error = "File not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file: {fileName}", fileName);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Helper method untuk menentukan content type berdasarkan ekstensi file
        /// </summary>
        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                _ => "application/octet-stream"
            };
        }
    }
}