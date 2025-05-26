using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LiviaAI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ModelController : ControllerBase
    {
        /// <summary>
        /// Get the model types.
        /// </summary>
        /// <returns>List of model types with their IDs and names.</returns>
        [HttpGet]
        [Authorize]
        public IActionResult GetModelTypes()
        {
            // Create a list of models
            var models = new[]
            {
                new { model_id = "gemini-2.0-flash-lite", model_name = "Livia" },
                new { model_id = "gemini-2.5-flash-preview-05-20", model_name = "Livia V2" },
            };

            // Return the models as a JSON response
            return Ok(models);
        }
    }
}
