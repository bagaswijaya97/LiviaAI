using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Serilog;

[ApiController]
[Route("api/[controller]")]
public class AuthTokenController : ControllerBase
{
    private readonly JWTHelper _jwtHelper;
    private readonly JWTOptions _jwtOptions;

    public AuthTokenController(JWTHelper jwtHelper, IOptions<JWTOptions> jwtOptions)
    {
        _jwtHelper = jwtHelper;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpGet("{secreet_key}")]
    public IActionResult GetToken(string secreet_key)
    {
        string expectedKey = _jwtOptions.Key;

        if (secreet_key != expectedKey)
        {
            return Unauthorized(new { data = (object?)null });
        }

        var token = _jwtHelper.EncodeJwt(secreet_key);
        // Log.Information("Token Created : ", token, DateTime.UtcNow);
        return Ok(new { token = token + "." + secreet_key });
    }
}
