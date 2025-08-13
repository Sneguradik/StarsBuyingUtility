using Buyer;
using Buyer.Configuration;
using Buyer.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IAutoBuyingService, AutoBuyingService>();
builder.Services.AddSingleton<ITelegramGiftBuyer, TelegramGiftBuyer>();
builder.Services.Configure<BuyerConfig>(builder.Configuration.GetSection("BuyerConfig"));

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var giftBuyer = scope.ServiceProvider.GetRequiredService<ITelegramGiftBuyer>();
    await giftBuyer.InitAsync();
}

host.Run();