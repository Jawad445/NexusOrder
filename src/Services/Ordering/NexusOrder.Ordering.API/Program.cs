using HealthChecks.UI.Client;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using NexusOrder.Contracts;
using NexusOrder.Ordering.Domain;
using NexusOrder.Ordering.Infrastructure.Data;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Serilog;

var builder = WebApplication.CreateBuilder(args);


builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Ordering.API")
    .WriteTo.Console()
    .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341")
    .WriteTo.File("logs/ordering-service-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

var postgresConn = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("PostgreSQL connection string missing.");
builder.Services.AddDbContext<OrderingDbContext>(opts => opts.UseNpgsql(postgresConn));

// 3. MassTransit with PostgreSQL Transactional Outbox
var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<OrderingDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });

        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: 5,
            minInterval: TimeSpan.FromMilliseconds(200),
            maxInterval: TimeSpan.FromSeconds(5),
            intervalDelta: TimeSpan.FromMilliseconds(400)));

        cfg.ConfigureEndpoints(context);
    });
});


// 4. OpenTelemetry Tracing (Jaeger via OTLP) & Prometheus Metrics
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("NexusOrder.Ordering.API"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsql()
        .AddSource("MassTransit")
        .AddOtlpExporter(opts =>
        {
            opts.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

var rabbitUser = builder.Configuration["RabbitMQ:Username"] ?? "guest";
var rabbitPass = builder.Configuration["RabbitMQ:Password"] ?? "guest";
var rabbitUri = $"amqp://{rabbitUser}:{rabbitPass}@{rabbitHost}:5672" ?? "amqp://guest:guest@localhost:5672";// 5. Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(postgresConn, name: "postgres", tags: ["ready"])
    .AddRabbitMQ(
        sp =>
        {
            var factory = new ConnectionFactory
            {
                HostName = rabbitHost,
                Port = 5672,
                UserName = rabbitUser,
                Password = rabbitPass
            };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        },
        name: "rabbitmq",
        tags: ["ready"]);


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});


// Create Order Minimal API Endpoint
app.MapPost("/api/orders", async (CreateOrderRequest request, OrderingDbContext db, IPublishEndpoint bus, CancellationToken ct) =>
{
    var order = Order.Create(
        request.CustomerId,
        request.Items.Select(i => (i.ProductId, i.Quantity, i.UnitPrice)));

    db.Orders.Add(order);

    // Written to the EF Outbox table within the exact same database transaction
    await bus.Publish(new OrderPlacedEvent(
        order.Id,
        order.CustomerId,
        order.TotalAmount,
        order.Items.Select(i => new OrderItemDto(i.ProductId, i.Quantity, i.UnitPrice)).ToList(),
        order.CreatedAtUtc
    ), ct);

    await db.SaveChangesAsync(ct);

    return Results.Accepted($"/api/orders/{order.Id}", new { order.Id, order.TotalAmount });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();

public record CreateOrderRequest(Guid CustomerId, List<CreateOrderItemRequest> Items);
public record CreateOrderItemRequest(Guid ProductId, int Quantity, decimal UnitPrice);