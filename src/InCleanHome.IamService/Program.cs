using InCleanHome.IamService.Application.Internal.CommandServices;
using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Application.Internal.QueryServices;
using InCleanHome.IamService.Configuration;
using InCleanHome.IamService.Discovery;
using InCleanHome.IamService.Domain.Repositories;
using InCleanHome.IamService.Domain.Services;
using InCleanHome.IamService.Infrastructure.ExternalServices.Auth0;
using InCleanHome.IamService.Infrastructure.Hashing;
using InCleanHome.IamService.Infrastructure.Persistence;
using InCleanHome.IamService.Infrastructure.Persistence.Repositories;
using InCleanHome.IamService.Infrastructure.Pipeline;
using InCleanHome.IamService.Infrastructure.Seeding;
using InCleanHome.IamService.Infrastructure.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Information()
    .CreateLogger();

try
{
    Log.Information("Starting InCleanHome IAM Service");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    //  Infrastructure settings from env
    var consulAddress = Environment.GetEnvironmentVariable("CONSUL_HTTP_ADDR")
                        ?? "http://consul:8500";
    var serviceName   = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "iam-service";
    var serviceHost   = Environment.GetEnvironmentVariable("SERVICE_HOST") ?? serviceName;
    var servicePort   = int.TryParse(Environment.GetEnvironmentVariable("SERVICE_PORT"), out var p) ? p : 5001;

    var dbConnection = Environment.GetEnvironmentVariable("IAM_DB_CONNECTION")
                       ?? builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException(
                           "IAM_DB_CONNECTION env var is required (PostgreSQL connection string).");

    var jwtSigningKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
                       ?? throw new InvalidOperationException("JWT_SIGNING_KEY env var is required.");

    Log.Information(
        "Identity: name={Name}, host={Host}, port={Port}, consul={Consul}",
        serviceName, serviceHost, servicePort, consulAddress);

    //  Load configuration from Consul KV (fallback: appsettings.json)
    var loadedFromConsul = await ConsulConfigurationLoader.LoadFromConsulAsync(
        builder.Configuration, consulAddress, serviceName);

    if (!loadedFromConsul)
        Log.Warning("Running with LOCAL configuration (appsettings.json).");

    //  ASP.NET Core services
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(opts =>
    {
        opts.SwaggerDoc("v1", new OpenApiInfo
        {
            Title       = "InCleanHome IAM Service",
            Version     = "v1",
            Description = "Identity and Access Management microservice"
        });
        opts.EnableAnnotations();
        
        opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "Ingresa el JWT interno generado por IAM. Ejemplo: Bearer eyJhbGciOi...",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

        opts.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    //  EF Core
    builder.Services.AddDbContext<IamDbContext>(opts =>
    {
        opts.UseNpgsql(dbConnection);
    });

    //  Repositories + UnitOfWork
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IWorkerDocumentRepository, WorkerDocumentRepository>();

    //  Application services
    builder.Services.AddScoped<IUserCommandService, UserCommandService>();
    builder.Services.AddScoped<IUserQueryService, UserQueryService>();

    //  Outbound services (hashing, JWT)
    builder.Services.AddScoped<IHashingService, HashingService>();
    builder.Services.AddScoped<ITokenService, TokenService>();

    // JWT settings: read non-sensitive values from config (Consul) and inject the
    // signing key from the JWT_SIGNING_KEY env var (it's a secret).
    builder.Services.Configure<TokenSettings>(opts =>
    {
        builder.Configuration.GetSection("Jwt").Bind(opts);
        opts.Secret = jwtSigningKey;
    });
    
    //  Auth0 (external IdP)
    builder.Services.Configure<Auth0Settings>(builder.Configuration.GetSection("Auth0"));
    builder.Services.AddHttpClient("auth0", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(10);
        c.DefaultRequestHeaders.Add("User-Agent", "InCleanHome-IamService/1.0");
    });
    builder.Services.AddScoped<IAuth0Service, Auth0Service>();

    //  CORS
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? new[] { "http://localhost:8080" };
    builder.Services.AddCors(opts =>
    {
        opts.AddDefaultPolicy(p => p.WithOrigins(corsOrigins)
            .AllowAnyHeader().AllowAnyMethod().AllowCredentials());
    });

    //  Service discovery (opt-in)
    var registrationOptions = new ConsulRegistrationOptions
    {
        ConsulAddress      = consulAddress,
        ServiceName        = serviceName,
        ServiceId          = $"{serviceName}-{Environment.MachineName}",
        Host               = serviceHost,
        Port               = servicePort,
        Tags               = new[] { "iam", "dotnet" },
        HealthCheckUrl     = $"http://{serviceHost}:{servicePort}/health"
    };
    builder.Services.AddSingleton(Options.Create(registrationOptions));
    builder.Services.AddHttpClient<ConsulServiceRegistration>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });
    builder.Services.AddHostedService<ConsulRegistrationHostedService>();

    
    //  Health checks
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<IamDbContext>("iam-db");

    //  Build pipeline
    var app = builder.Build();

    // Apply database schema. For initial iteration we use EnsureCreated (mirrors
    // the monolith behavior). When you start using formal migrations, replace
    // with `await db.Database.MigrateAsync();` and commit the Migrations/ folder.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<IamDbContext>();
        try
        {
            // Wait for DB to be ready (compose healthcheck should ensure this).
            await db.Database.EnsureCreatedAsync();
            Log.Information("Database schema ensured.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not ensure database schema. Service will exit.");
            throw;
        }

        // Seed admin user if configured.
        await AdminSeeder.SeedAsync(app.Services, app.Logger);
    }

    app.UseSerilogRequestLogging();
    app.UseCors();

    // Health endpoint MUST be reachable without auth so Consul can probe it.
    app.MapHealthChecks("/health");

    // Root endpoint - quick status
    app.MapGet("/", () => Results.Ok(new
    {
        service      = serviceName,
        status       = "running",
        configSource = loadedFromConsul ? "consul" : "appsettings.json",
        version      = "1.0.0"
    }));

    // Swagger
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "InCleanHome IAM Service v1");
        c.RoutePrefix = "swagger";
    });

    // Custom JWT middleware that populates HttpContext.Items["User"]
    app.UseRequestAuthorization();

    app.MapControllers();

    Log.Information("InCleanHome IAM Service ready on port {Port}", servicePort);
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "IAM Service terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
