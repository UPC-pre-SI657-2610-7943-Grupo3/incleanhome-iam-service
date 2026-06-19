using InCleanHome.IamService.Application.Internal.CommandServices;
using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Application.Internal.QueryServices;
using InCleanHome.IamService.Configuration;
using InCleanHome.IamService.Discovery;
using InCleanHome.IamService.Domain.Repositories;
using InCleanHome.IamService.Domain.Services;
using InCleanHome.IamService.Domain.Services.External;
using InCleanHome.IamService.Infrastructure.ExternalServices.Auth0;
using InCleanHome.IamService.Infrastructure.ExternalServices.ProfileService;
using InCleanHome.IamService.Infrastructure.Hashing;
using InCleanHome.IamService.Infrastructure.Persistence;
using InCleanHome.IamService.Infrastructure.Persistence.Repositories;
using InCleanHome.IamService.Infrastructure.Pipeline;
using InCleanHome.IamService.Infrastructure.Seeding;
using InCleanHome.IamService.Infrastructure.Tokens;
using MassTransit;
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

    // ────────────────────────────────────────────────────────────────────
    //  Infrastructure settings from env
    // ────────────────────────────────────────────────────────────────────
    var consulAddress = Environment.GetEnvironmentVariable("CONSUL_HTTP_ADDR") ?? "http://consul:8500";
    var serviceName   = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "iam-service";
    var serviceHost   = Environment.GetEnvironmentVariable("SERVICE_HOST") ?? serviceName;
    var servicePort   = int.TryParse(Environment.GetEnvironmentVariable("SERVICE_PORT"), out var p) ? p : 5001;

    var dbConnection = Environment.GetEnvironmentVariable("IAM_DB_CONNECTION")
                       ?? builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException(
                           "IAM_DB_CONNECTION env var is required (PostgreSQL connection string).");

    var jwtSigningKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
                       ?? throw new InvalidOperationException("JWT_SIGNING_KEY env var is required.");

    var rabbitMqUrl = Environment.GetEnvironmentVariable("RABBITMQ_URL") ?? string.Empty;
    var rabbitMqEnabled = !string.IsNullOrWhiteSpace(rabbitMqUrl)
                         && !rabbitMqUrl.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

    Log.Information(
        "Identity: name={Name}, host={Host}, port={Port}, consul={Consul}, broker={Broker}",
        serviceName, serviceHost, servicePort, consulAddress,
        rabbitMqEnabled ? "configured" : "DISABLED (placeholder)");

    // ────────────────────────────────────────────────────────────────────
    //  Load configuration from Consul KV (fallback: appsettings.json)
    // ────────────────────────────────────────────────────────────────────
    var loadedFromConsul = await ConsulConfigurationLoader.LoadFromConsulAsync(
        builder.Configuration, consulAddress, serviceName);
    if (!loadedFromConsul)
        Log.Warning("Running with LOCAL configuration (appsettings.json).");

    // ────────────────────────────────────────────────────────────────────
    //  ASP.NET Core services
    // ────────────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen(opts =>
    {
        opts.SwaggerDoc("v1", new OpenApiInfo
        {
            Title       = "InCleanHome IAM Service",
            Version     = "v1",
            Description = "Identity & Access Management — users, auth, worker docs"
        });
        opts.EnableAnnotations();

        // Add Bearer token support to Swagger UI ("Authorize" button)
        opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header. Example: 'Bearer eyJhbGciOi...'",
            Name        = "Authorization",
            In          = ParameterLocation.Header,
            Type        = SecuritySchemeType.ApiKey,
            Scheme      = "Bearer"
        });
        opts.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    });

    // ────────────────────────────────────────────────────────────────────
    //  EF Core
    // ────────────────────────────────────────────────────────────────────
    builder.Services.AddDbContext<IamDbContext>(opts => opts.UseNpgsql(dbConnection));

    // ────────────────────────────────────────────────────────────────────
    //  Repositories + UnitOfWork
    // ────────────────────────────────────────────────────────────────────
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IWorkerDocumentRepository, WorkerDocumentRepository>();

    // ────────────────────────────────────────────────────────────────────
    //  Application services
    // ────────────────────────────────────────────────────────────────────
    builder.Services.AddScoped<IUserCommandService, UserCommandService>();
    builder.Services.AddScoped<IUserQueryService, UserQueryService>();

    // ────────────────────────────────────────────────────────────────────
    //  Outbound services (hashing, JWT)
    // ────────────────────────────────────────────────────────────────────
    builder.Services.AddScoped<IHashingService, HashingService>();
    builder.Services.AddScoped<ITokenService, TokenService>();

    builder.Services.Configure<TokenSettings>(opts =>
    {
        builder.Configuration.GetSection("Jwt").Bind(opts);
        opts.Secret = jwtSigningKey;
    });

    // ────────────────────────────────────────────────────────────────────
    //  Auth0 (external IdP)
    // ────────────────────────────────────────────────────────────────────
    builder.Services.Configure<Auth0Settings>(builder.Configuration.GetSection("Auth0"));
    builder.Services.AddHttpClient("auth0", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(10);
        c.DefaultRequestHeaders.Add("User-Agent", "InCleanHome-IamService/1.0");
    });
    builder.Services.AddScoped<IIdentityProvider, Auth0IdentityProviderAdapter>();

    // ────────────────────────────────────────────────────────────────────
    //  Profile Service HTTP client (for /me, login, complete-registration)
    // ────────────────────────────────────────────────────────────────────
    builder.Services.AddHttpClient<IProfileServiceClient, ProfileServiceClient>(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(15);
    });

    // ────────────────────────────────────────────────────────────────────
    //  MassTransit + RabbitMQ (CloudAMQP). Soft-fail if URL is placeholder.
    // ────────────────────────────────────────────────────────────────────
    builder.Services.AddMassTransit(x =>
    {
        // IAM doesn't consume events itself (it only publishes).
        if (rabbitMqEnabled)
        {
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(rabbitMqUrl));
                cfg.ConfigureEndpoints(context);
            });
        }
        else
        {
            x.UsingInMemory();
        }
    });

    // ────────────────────────────────────────────────────────────────────
    //  CORS
    // ────────────────────────────────────────────────────────────────────
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? new[] { "http://localhost:8080" };
    builder.Services.AddCors(opts =>
    {
        opts.AddDefaultPolicy(p => p.WithOrigins(corsOrigins)
            .AllowAnyHeader().AllowAnyMethod().AllowCredentials());
    });

    // ────────────────────────────────────────────────────────────────────
    //  Service discovery (opt-in)
    // ────────────────────────────────────────────────────────────────────
    var registrationOptions = new ConsulRegistrationOptions
    {
        ConsulAddress  = consulAddress,
        ServiceName    = serviceName,
        ServiceId      = $"{serviceName}-{Environment.MachineName}",
        Host           = serviceHost,
        Port           = servicePort,
        Tags           = new[] { "iam", "dotnet" },
        HealthCheckUrl = $"http://{serviceHost}:{servicePort}/health"
    };
    builder.Services.AddSingleton(Options.Create(registrationOptions));
    builder.Services.AddHttpClient<ConsulServiceRegistration>(c => c.Timeout = TimeSpan.FromSeconds(10));
    builder.Services.AddHostedService<ConsulRegistrationHostedService>();

    // ────────────────────────────────────────────────────────────────────
    //  Health checks
    // ────────────────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks().AddDbContextCheck<IamDbContext>("iam-db");

    // ────────────────────────────────────────────────────────────────────
    //  Build pipeline
    // ────────────────────────────────────────────────────────────────────
    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<IamDbContext>();
        try
        {
            await db.Database.EnsureCreatedAsync();
            Log.Information("Database schema ensured.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not ensure database schema. Service will exit.");
            throw;
        }

        await AdminSeeder.SeedAsync(app.Services, app.Logger);
    }

    app.UseSerilogRequestLogging();
    app.UseCors();

    app.MapHealthChecks("/health");
    app.MapGet("/", () => Results.Ok(new
    {
        service      = serviceName,
        status       = "running",
        configSource = loadedFromConsul ? "consul" : "appsettings.json",
        broker       = rabbitMqEnabled ? "configured" : "disabled",
        version      = "1.0.0"
    }));

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "InCleanHome IAM Service v1");
        c.RoutePrefix = "swagger";
    });

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
