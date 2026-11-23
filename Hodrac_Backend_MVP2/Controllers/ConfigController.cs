using Hodrac_Backend_MVP2.Services.Images;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hodrac_Backend_MVP2.Controllers
{
    /// <summary>Public client configuration.</summary>
    [ApiController]
    [Route("api/config")]
    [AllowAnonymous]
    public class ConfigController : ControllerBase
    {
        private readonly IOptions<ImagesOptions> _images;

        public ConfigController(IOptions<ImagesOptions> images) => _images = images;

        public record ImageConfigResponse(string PrimaryBaseUrl, IReadOnlyList<string> FallbackBaseUrls);

        /// <summary>
        /// Image hosts for the frontend: images load from primaryBaseUrl; when one fails, swap that
        /// prefix for the first fallbackBaseUrls entry and retry once. Every base ends with "/".
        /// </summary>
        [HttpGet("images")]
        public ActionResult<ImageConfigResponse> GetImages()
        {
            var options = _images.Value;
            return Ok(new ImageConfigResponse(options.NormalizedPublicBaseUrl, options.NormalizedFallbackBaseUrls));
        }
    }
}
