using Microsoft.AspNetCore.Mvc;
using LiveWebApi.Services;
using System.Threading.Tasks;

namespace LiveWebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PythonScriptController : ControllerBase
    {
        private readonly PythonScriptService _scriptService;

        public PythonScriptController(PythonScriptService scriptService)
        {
            _scriptService = scriptService;
        }

        /// <summary>
        /// 启动抖音直播抓取脚本
        /// </summary>
        /// <param name="liveId">直播间ID（如：495144572832）</param>
        /// <returns>执行结果</returns>
        [HttpPost("StartDouyinFetch")]
        public async Task<IActionResult> StartDouyinFetch([FromQuery] string liveId= "869935219305")
        {
            try
            {
                var result = await _scriptService.ExecuteScriptAsync(liveId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}