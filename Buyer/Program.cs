using Buyer;
using Buyer.Configuration;
using Buyer.Services;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs_.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IAutoBuyingService, AutoBuyingService>();
builder.Services.AddSingleton<ITelegramGiftBuyer, TelegramGiftBuyer>();
builder.Services.Configure<BuyerConfig>(builder.Configuration.GetSection("BuyerConfig"));
builder.Services.AddSerilog();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var giftBuyer = scope.ServiceProvider.GetRequiredService<ITelegramGiftBuyer>();
    await giftBuyer.InitAsync();
}

host.Run();