using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiveWebApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductController : ControllerBase
    {
        private readonly ILogger<ProductController> _logger;
        private const string DataDirectory = "Data";

        public ProductController(ILogger<ProductController> logger)
        {
            _logger = logger;
        }

        [HttpPost("save-product-list")]
        public async Task<IActionResult> SaveProductList([FromBody] List<ProductItem> productItems)
        {
            try
            {
                if (productItems == null || productItems.Count == 0)
                {
                    return BadRequest("商品列表不能为空");
                }

                // 确保所有商品项都有 room_id
                if (productItems.Exists(item => string.IsNullOrEmpty(item.room_id)))
                {
                    return BadRequest("所有商品项必须包含 room_id");
                }

                // 获取所有唯一的 room_id
                var roomIds = new HashSet<string>(productItems.Select(item => item.room_id));

                // 确保数据目录存在
                var dataPath = Path.Combine(Directory.GetCurrentDirectory(), DataDirectory);
                if (!Directory.Exists(dataPath))
                {
                    Directory.CreateDirectory(dataPath);
                }

                // 按 room_id 分组并保存数据
                foreach (var roomId in roomIds)
                {
                    var roomItems = productItems.Where(item => item.room_id == roomId).ToList();
                    var filePath = Path.Combine(dataPath, $"{roomId}.json");

                    // 序列化数据
                    var jsonData = JsonSerializer.Serialize(roomItems, new JsonSerializerOptions { WriteIndented = true });

                    // 写入文件（覆盖现有文件）
                    await System.IO.File.WriteAllTextAsync(filePath, jsonData);

                    _logger.LogInformation($"已保存房间 {roomId} 的商品列表，共 {roomItems.Count} 条记录");
                }

                return Ok(new { Status = "success", Message = "商品列表保存成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存商品列表时发生错误");
                return StatusCode(500, "保存商品列表时发生内部错误");
            }
        }

        [HttpGet("get-all-products")]
        public async Task<IActionResult> GetAllProducts()
        {
            try
            {
                var dataPath = Path.Combine(Directory.GetCurrentDirectory(), DataDirectory);
                if (!Directory.Exists(dataPath))
                {
                    return Ok(new List<ProductItem>());
                }

                var allProducts = new List<ProductItem>();
                var jsonFiles = Directory.GetFiles(dataPath, "*.json");

                foreach (var filePath in jsonFiles)
                {
                    var jsonData = await System.IO.File.ReadAllTextAsync(filePath);
                    var products = JsonSerializer.Deserialize<List<ProductItem>>(jsonData);
                    if (products != null)
                    {
                        allProducts.AddRange(products);
                    }
                }

                _logger.LogInformation($"成功查询到 {allProducts.Count} 条商品数据");
                return Ok(allProducts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询商品数据时发生错误");
                return StatusCode(500, "查询商品数据时发生内部错误");
            }
        }
    }

    public class ProductItem
    {
        public string card_id { get; set; }
        public int auth_type { get; set; }
        public int operation { get; set; }
        public string room_id { get; set; }
        public string p_name { get; set; }
        public string p_img { get; set; }
        public string p_id { get; set; }
    }
}