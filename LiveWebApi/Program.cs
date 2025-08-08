using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
// ���� WebSocketHandler ���������ռ�
using LiveWebApi.Handlers;

var builder = WebApplication.CreateBuilder(args);

// ���� Kestrel ����������������������豸���ʣ�
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(8690); // HTTP �˿�
    serverOptions.ListenAnyIP(5001, opt => opt.UseHttps()); // ��ȷ HTTPS �˿ڣ����� 5001��
});

// ��ӿ�����֧��
builder.Services.AddControllers();

// ע�� Swagger������ǰ�˵��ԣ�
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

// ע�� Python �ű����񣨺���ҵ���߼���
builder.Services.AddScoped<LiveWebApi.Services.PythonScriptService>();

// ע�� Python ����ѡ��� appsettings ��ȡ��
builder.Services.Configure<LiveWebApi.PythonSettings>(
    builder.Configuration.GetSection("PythonSettings")
);

// ע�� WebSocket ������������ģʽ�������ӣ�
builder.Services.AddSingleton<WebSocketHandler>();

// �������ã�����ǰ����������API�����������������ƾ���������
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()  // ����������ʱ����������Դ�����������滻Ϊǰ��ʵ������
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// 1. ����Ĭ��ҳ�棨Vue��ڣ�
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

// 2. ���þ�̬�ļ���Vue����ļ���λ��wwwroot��
app.UseStaticFiles();

// 3. ������������Swagger��API�����ĵ���
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "LiveWebApi v1");
        c.RoutePrefix = "swagger"; // Swaggerҳ��·����/swagger
    });
}

// 4. ���ÿ��򣨱�����UseRouting֮ǰ��
app.UseCors("AllowFrontend");

// 5. ���� WebSocket ֧�֣���Ӵ��У�
app.UseWebSockets();

// 6. HTTPS�ض������Ȩ
//app.UseHttpsRedirection();
app.UseAuthorization();

// 7. API·��ӳ��
app.UseRouting();
app.MapControllers();

// 8. ���� WebSocket �˵�
app.Map("/ws", async context =>
{
    var handler = context.RequestServices.GetRequiredService<WebSocketHandler>();
    // ���� context.RequestAborted ��Ϊȡ�����ƣ�ASP.NET Core �Զ����������������ڣ�
    await handler.HandleWebSocketAsync(context, context.RequestAborted);
});

// 9. Vue Router Historyģʽ֧�֣�δƥ��·��ָ��index.html��
app.MapFallbackToFile("index.html");

app.Run();

// ����ģ�ͣ�Python�ű�·�������ã�
namespace LiveWebApi
{
    public class PythonSettings
    {
        public string PythonPath { get; set; } = string.Empty; // �磺pythonw.exe �� python3
        public string ScriptPath { get; set; } = "PythonScripts/main.py"; // ������Ŀ¼��·��
    }
}