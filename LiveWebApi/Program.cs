var builder = WebApplication.CreateBuilder(args);

// ��ӷ���
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 1. ����Ĭ��ҳ�棨������UseStaticFiles֮ǰ��
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

// 2. ���þ�̬�ļ����ʣ�Vue����ļ�����wwwroot�£�
app.UseStaticFiles();

// 3. ������������Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 4. HTTPS�ض������Ȩ
app.UseHttpsRedirection();
app.UseAuthorization();

// 5. API·��
app.MapControllers();

// 6. ����Vue Router Historyģʽ������δƥ���·��ָ��index.html��
app.MapFallbackToFile("index.html");

app.Run();
