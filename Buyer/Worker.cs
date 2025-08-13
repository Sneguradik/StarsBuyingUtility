using Buyer.Services;

namespace Buyer;

public class Worker(IServiceProvider sp) : BackgroundService
{
    

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = sp.CreateScope();
        var autoBuyingService = scope.ServiceProvider.GetRequiredService<IAutoBuyingService>();
        await autoBuyingService.RunAsync(stoppingToken);
    }
}