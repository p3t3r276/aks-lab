using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration.GetConnectionString("Mongo")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

var app = builder.Build();

IMongoCollection<AccessLog> Logs(IMongoClient c) => c.GetDatabase("labdb").GetCollection<AccessLog>("access_logs");

app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapGet("/products", async (AppDbContext db, IConnectionMultiplexer redis, IMongoClient mongo) =>
{
    const string key = "products:all";
    var cache = redis.GetDatabase();
    var cached = await cache.StringGetAsync(key);
    var hit = cached.HasValue;

    List<Product> items;
    if (hit)
        items = JsonSerializer.Deserialize<List<Product>>(cached.ToString())!;
    else
    {
        items = await db.Products.AsNoTracking().ToListAsync();
        await cache.StringSetAsync(key, JsonSerializer.Serialize(items), TimeSpan.FromSeconds(60));
    }

    await Logs(mongo).InsertOneAsync(new AccessLog { Path = "/products", CacheHit = hit });
    return Results.Ok(new { cacheHit = hit, items });
});

app.MapPost("/products", async (Product p, AppDbContext db, IConnectionMultiplexer redis) =>
{
    db.Products.Add(p);
    await db.SaveChangesAsync();
    await redis.GetDatabase().KeyDeleteAsync("products:all");   // invalidate cache
    return Results.Created($"/products/{p.Id}", p);
});

app.MapGet("/logs", async (IMongoClient mongo) =>
{
    var logs = await Logs(mongo).Find(_ => true).SortByDescending(x => x.At).Limit(20).ToListAsync();
    return logs.Select(x => new { x.Path, x.CacheHit, x.At });
});

app.MapGet("/version", (IConfiguration config) => Results.Ok(new
{
    pod = Environment.MachineName,              // trong container, hostname = tên pod
    version = config["APP_VERSION"] ?? "local"  // commit SHA do pipeline truyền vào
}));


app.Run();

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    [Precision(18, 2)] public decimal Price { get; set; }
}

public class AccessLog
{
    public ObjectId Id { get; set; }
    public string Path { get; set; } = "";
    public bool CacheHit { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}
