using Microsoft.EntityFrameworkCore;
using Payment.Api.Data;
using Payment.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOpenApi();

//Database
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("PaymentDb")));

//Payment Store
builder.Services.AddScoped<IPaymentStore, EfPaymentStore>();
builder.Services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
builder.Services.AddScoped<IRequestHasher, RequestHasher>();

//Payment Provider
builder.Services.AddHttpClient<IPaymentProviderClient, PaymentProviderClient>(client =>
{
    var baseUrl = builder.Configuration["PaymentProvider:BaseUrl"]
        ?? throw new InvalidOperationException(
            "PaymentProvider:BaseUrl is not configured.");
    
    client.BaseAddress = new Uri(baseUrl);
});

//Payment Service
builder.Services.AddScoped<IPaymentService, PaymentService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();