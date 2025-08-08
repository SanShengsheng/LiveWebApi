var builder = WebApplication.CreateBuilder(args);

// 添加服务
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 1. 配置默认页面（必须在UseStaticFiles之前）
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

// 2. 启用静态文件访问（Vue打包文件放在wwwroot下）
app.UseStaticFiles();

// 3. 开发环境启用Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 4. HTTPS重定向和授权
app.UseHttpsRedirection();
app.UseAuthorization();

// 5. API路由
app.MapControllers();

// 6. 处理Vue Router History模式（所有未匹配的路由指向index.html）
app.MapFallbackToFile("index.html");

app.Run();
