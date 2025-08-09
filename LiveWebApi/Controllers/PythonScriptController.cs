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
        /// 停止抖音直播抓取脚本
        /// </summary>
        /// <param name="liveId">直播间ID（如：495144572832）</param>
        /// <returns>执行结果</returns>
        [HttpPost("StopDouyinFetch")]
        public async Task<IActionResult> StopDouyinFetch([FromQuery] string liveId = "869935219305")
        {
            try
            {
                var result = await _scriptService.StopScriptAsync(liveId);
                return Ok(new { Success = true, Message = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Success = false, Message = ex.Message });
            }
        }

        /// <summary>
        /// 启动抖音直播抓取脚本（同一liveId不重复启动）
        /// </summary>
        /// <param name="liveId">直播间ID（如：495144572832）</param>
        /// <returns>执行结果</returns>
        [HttpPost("StartDouyinFetch")]
        public async Task<IActionResult> StartDouyinFetch([FromQuery] string liveId = "869935219305")
        {
            try
            {
                var result = await _scriptService.ExecuteScriptAsync(liveId);
                return Ok(new { Success = true, Message = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Success = false, Message = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(new { Success = false, Message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Success = false, Message = ex.Message });
            }
        }
    }
}