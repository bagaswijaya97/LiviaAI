using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

public class TokenValidationRequest
{
    public string Token { get; set; } = string.Empty;
}

[ApiController]
[Route("api/[controller]")]
public class AuthValidationController : ControllerBase
{
    private readonly JWTHelper _jwtHelper;
    private readonly JWTOptions _jwtOptions;

    public AuthValidationController(JWTHelper jwtHelper, IOptions<JWTOptions> jwtOptions)
    {
        _jwtHelper = jwtHelper;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost]
    public IActionResult ValidateToken([FromBody] TokenValidationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Unauthorized(
                new
                {
                    meta_data = new { code = 401, message = "Token is required." },
                    data = (object?)null,
                }
            );
        }

        var parts = request.Token.Split('.');

        if (parts.Length < 4)
        {
            return Unauthorized(
                new
                {
                    meta_data = new { code = 401, message = "Token format is invalid." },
                    data = (object?)null,
                }
            );
        }

        var secreetKey = parts[^1];
        if (secreetKey != _jwtOptions.Key)
        {
            return Unauthorized(
                new
                {
                    meta_data = new { code = 401, message = "Invalid secret key in token." },
                    data = (object?)null,
                }
            );
        }

        var cleanToken = string.Join(".", parts[..^1]);
        var claims = _jwtHelper.DecodeJwt(cleanToken);

        if (claims != null)
        {
            return Ok(
                new
                {
                    meta_data = new { code = 200, message = "Token is valid." },
                    data = new { is_valid = true },
                }
            );
        }

        return Unauthorized(
            new
            {
                meta_data = new { code = 401, message = "Invalid or expired token." },
                data = (object?)null,
            }
        );
    }
}
