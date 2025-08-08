using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System.Collections.Generic;
using LiveWebApi.Handlers;
using LiveWebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// 配置 Kestrel 监听局域网
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(8690); // HTTP 端口
    serverOptions.ListenAnyIP(5001, opt => opt.UseHttps()); // HTTPS 端口
});

// 添加控制器支持
builder.Services.AddControllers();

// 注册 Swagger
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

// 注册 Python 脚本服务（单例模式，确保状态共享）
builder.Services.AddSingleton<PythonScriptService>();

// 注册 Python 配置选项
builder.Services.Configure<PythonSettings>(
    builder.Configuration.GetSection("PythonSettings")
);

// 注册 WebSocket 处理器（单例管理连接）
builder.Services.AddSingleton<WebSocketHandler>();

// 跨域配置
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()  // 生产环境替换为具体前端域名
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// 配置默认页面（Vue入口）
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

// 启用静态文件（Vue打包文件）
app.UseStaticFiles();

// 开发环境启用Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "LiveWebApi v1");
        c.RoutePrefix = "swagger";
    });
}

// 启用跨域
app.UseCors("AllowFrontend");

// 启用 WebSocket 支持
app.UseWebSockets();

// HTTPS重定向和授权
// app.UseHttpsRedirection();
app.UseAuthorization();

// API路由映射
app.UseRouting();
app.MapControllers();

// WebSocket 端点配置
app.Map("/ws", async context =>
{
    var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
    await handler.HandleWebSocketAsync(context, context.RequestAborted);
});

// Vue Router History模式支持
app.MapFallbackToFile("index.html");

app.Run();

// Python配置模型
namespace LiveWebApi
{
    public class PythonSettings
    {
        public string PythonPath { get; set; } = string.Empty; // 如 python.exe 或 python3
        public string ScriptPath { get; set; } = "PythonScripts/main.py"; // 相对输出目录的路径
    }
}