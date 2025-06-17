using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using TaskTodo.DAL.AccessService;
using TaskTodo.DAL.Repository;
using TaskTodo.Data;
using TaskTodo.Services.intreface;
using TaskTodo.Services.Repo;

var builder = WebApplication.CreateBuilder(args);
var Myallowedports = "AllowLocalhost3000";

// Add services to the container.

builder.Services.AddControllers(option =>
{
    option.RespectBrowserAcceptHeader = true;
    option.ReturnHttpNotAcceptable = true;
    
}).AddXmlSerializerFormatters();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddDbContext<ApplicationDbContext>(option => option.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")).LogTo(Console.WriteLine, LogLevel.Information));
builder.Services.AddScoped<ITask, TaskService>();
builder.Services.AddScoped<ITaskAuth, TaskAuth>();
builder.Services.AddScoped<IAuthenticate, Authenticate>();
builder.Services.AddScoped<IUser, UserService>();
 builder.Services.AddEndpointsApiExplorer();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "MyAPI", Version = "v1" });

    // Add JWT Bearer Auth to Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your valid token.\n\nExample: Bearer abc123xyz"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: Myallowedports,
                      policy =>
                      {
                          policy.WithOrigins("https://localhost:61634", "https://localhost:61635")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                      });
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>(); // Get a logger
    var context = services.GetRequiredService<TaskTodo.Data.ApplicationDbContext>();

    try
    {
        logger.LogInformation("Attempting to connect to database...");
        // Add a retry loop for robustness
        int maxRetries = 10; // Or adjust as needed
        int retryDelayMilliseconds = 5000; // 5 seconds
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                // This line will attempt to apply migrations.
                // It will also implicitly try to connect to the database.
                context.Database.Migrate();
                logger.LogInformation("Database migration completed successfully.");
                break; // Exit retry loop if successful
            }
            catch (SqlException ex) when (ex.Number == 17 || ex.Number == 53) // Common SQL Server transient errors
            {
                logger.LogWarning(ex, $"Database connection failed (Attempt {i + 1}/{maxRetries}). Retrying in {retryDelayMilliseconds / 1000} seconds...");
                if (i == maxRetries - 1) throw; // Re-throw if last attempt
                Thread.Sleep(retryDelayMilliseconds);
            }
            catch (Exception ex) // Catch other potential migration errors
            {
                logger.LogError(ex, "An error occurred while migrating the database.");
                throw; // Re-throw any non-transient errors
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while connecting or migrating the database.");
        // You might want to crash the application here if DB is absolutely required
        // Or handle gracefully if the app can run without immediate DB access
    }
}
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(Myallowedports);
app.UseAuthentication(); 

app.UseAuthorization();

app.MapHub<NotificationHub>("/NotificationHub");

app.MapControllers();
app.Run();
