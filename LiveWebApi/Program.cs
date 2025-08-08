using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
// 引入 WebSocketHandler 所在命名空间
using LiveWebApi.Handlers;

var builder = WebApplication.CreateBuilder(args);

// 配置 Kestrel 监听局域网（允许局域网设备访问）
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(8690); // HTTP 端口
    serverOptions.ListenAnyIP(5001, opt => opt.UseHttps()); // 明确 HTTPS 端口（例如 5001）
});

// 添加控制器支持
builder.Services.AddControllers();

// 注册 Swagger（适配前端调试）
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LiveWebApi",
        Version = "v1",
        Description = "抖音直播抓取API + Vue前端服务"
    });
});

// 注册 Python 脚本服务（核心业务逻辑）
builder.Services.AddScoped<LiveWebApi.Services.PythonScriptService>();

// 注册 Python 配置选项（从 appsettings 读取）
builder.Services.Configure<LiveWebApi.PythonSettings>(
    builder.Configuration.GetSection("PythonSettings")
);

// 注册 WebSocket 处理器（单例模式管理连接）
builder.Services.AddSingleton<WebSocketHandler>();

// 跨域配置（允许前端域名访问API，生产环境建议限制具体域名）
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()  // 开发环境临时允许所有来源，生产环境替换为前端实际域名
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// 1. 配置默认页面（Vue入口）
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

// 2. 启用静态文件（Vue打包文件，位于wwwroot）
app.UseStaticFiles();

// 3. 开发环境启用Swagger（API调试文档）
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "LiveWebApi v1");
        c.RoutePrefix = "swagger"; // Swagger页面路径：/swagger
    });
}

// 4. 启用跨域（必须在UseRouting之前）
app.UseCors("AllowFrontend");

// 5. 启用 WebSocket 支持（添加此行）
app.UseWebSockets();

// 6. HTTPS重定向和授权
//app.UseHttpsRedirection();
app.UseAuthorization();

// 7. API路由映射
app.UseRouting();
app.MapControllers();

// 8. 配置 WebSocket 端点
app.Map("/ws", async context =>
{
    var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
    // 传入 context.RequestAborted 作为取消令牌（ASP.NET Core 自动管理请求生命周期）
    await handler.HandleWebSocketAsync(context, context.RequestAborted);
});

// 9. Vue Router History模式支持（未匹配路由指向index.html）
app.MapFallbackToFile("index.html");

app.Run();

// 配置模型（Python脚本路径等设置）
namespace LiveWebApi
{
    public class PythonSettings
    {
        public string PythonPath { get; set; } = string.Empty; // 如：pythonw.exe 或 python3
        public string ScriptPath { get; set; } = "PythonScripts/main.py"; // 相对输出目录的路径
    }
}