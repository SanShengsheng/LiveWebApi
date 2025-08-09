using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System.Collections.Generic;
using LiveWebApi.Handlers;
using LiveWebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// ���� Kestrel ����������
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(8690); // HTTP �˿�
    serverOptions.ListenAnyIP(5001, opt => opt.UseHttps()); // HTTPS �˿�
});

// ���ӿ�����֧��
builder.Services.AddControllers();

// ע�� Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LiveWebApi",
        Version = "v1",
        Description = "����ֱ��ץȡAPI + Vueǰ�˷���"
    });
});

// ע�� Python �ű����񣨵���ģʽ��ȷ��״̬������
builder.Services.AddSingleton<PythonScriptService>();

// ע�� Python ����ѡ��
builder.Services.Configure<PythonSettings>(
    builder.Configuration.GetSection("PythonSettings")
);

// ע�� WebSocket �������������������ӣ�
builder.Services.AddSingleton<WebSocketHandler>();

// ��������
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()  // ���������滻Ϊ����ǰ������
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// 在应用启动时关闭所有现有WebSocket连接
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var webSocketHandler = services.GetRequiredService<WebSocketHandler>();
        await webSocketHandler.CloseAllConnectionsAsync();
        Console.WriteLine("应用启动时已关闭所有现有WebSocket连接");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"关闭现有WebSocket连接时出错: {ex.Message}");
    }
}

// ����Ĭ��ҳ�棨Vue��ڣ�
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

// ���þ�̬�ļ���Vue����ļ���
app.UseStaticFiles();

// ������������Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "LiveWebApi v1");
        c.RoutePrefix = "swagger";
    });
}

// ���ÿ���
app.UseCors("AllowFrontend");

// ���� WebSocket ֧��
app.UseWebSockets();

// HTTPS�ض������Ȩ
// app.UseHttpsRedirection();
app.UseAuthorization();

// API·��ӳ��
app.UseRouting();
app.MapControllers();

// WebSocket �˵�����
app.Map("/ws", async context =>
{
    var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
    await handler.HandleWebSocketAsync(context, context.RequestAborted);
});

// Vue Router Historyģʽ֧��
app.MapFallbackToFile("index.html");

app.Run();

// Python����ģ��
namespace LiveWebApi
{
    public class PythonSettings
    {
        public string PythonPath { get; set; } = string.Empty; // �� python.exe �� python3
        public string ScriptPath { get; set; } = "PythonScripts/main.py"; // ������Ŀ¼��·��
    }
}