using AuraScan.Server.Data;
using AuraScan.Server.Hubs;
using AuraScan.Server.Infrastructure;
using AuraScan.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<AuraScanDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("AuraScanDb") ?? "Data Source=AuraScan.db"));

// Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IStudyService, StudyService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IConfigService, ConfigService>();

// DICOM SCP
builder.Services.Configure<DicomScpOptions>(builder.Configuration.GetSection("DicomScp"));
builder.Services.AddHostedService<DicomScpHostedService>();

// Authentication
builder.Services.AddAuthentication(ApiKeyAuthOptions.SchemeName)
    .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(ApiKeyAuthOptions.SchemeName, _ => { });
builder.Services.AddAuthorization();

// API / SignalR / Swagger
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AuraScan Server API", Version = "v1" });
    c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-API-Key",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Description = "API key authentication"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// CORS — restricted to configured origins
var allowedOrigins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0 && !allowedOrigins.Contains("*"))
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
        else
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
    options.AddPolicy("SignalR", policy =>
    {
        if (allowedOrigins.Length > 0 && !allowedOrigins.Contains("*"))
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        else
            policy.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
    });
});

var app = builder.Build();

// Auto-migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuraScanDbContext>();
    await db.Database.MigrateAsync();
}

// Middleware
var requireHttps = builder.Configuration.GetValue<bool>("Security:RequireHttps");
if (requireHttps)
{
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<AuraScanHub>("/hubs/aurascan", options =>
{
    options.AllowStatefulReconnects = true;
}).RequireCors("SignalR");

app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

await app.RunAsync();
