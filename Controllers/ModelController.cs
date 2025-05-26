using GeminiAIServices.Helpers;
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
                new { model_id = Constan.STR_MODEL_ID_1, model_name = Constan.STR_MODEL_NAME_1 },
                new { model_id = Constan.STR_MODEL_ID_1, model_name = Constan.STR_MODEL_NAME_2 },
            };

            // Return the models as a JSON response
            return Ok(models);
        }
    }
}
