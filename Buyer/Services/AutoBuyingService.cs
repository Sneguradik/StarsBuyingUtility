using System.Collections.Concurrent;
using Buyer.Configuration;
using Buyer.Models;
using Microsoft.Extensions.Options;

namespace Buyer.Services;

public interface IAutoBuyingService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
public class AutoBuyingService(ILogger<AutoBuyingService> logger, ITelegramGiftBuyer telegramGiftBuyer, IOptions<BuyerConfig> config): IAutoBuyingService
{
    private HashSet<long> _knownGifts = new();
    
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("🚀 AutoBuyingService запущен");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation($"{DateTime.Now:yyyy-MM-dd hh-mm-ss} Started checking");
                var giftList = (await telegramGiftBuyer.GetAvailableGiftsAsync(cancellationToken)).ToArray();

                
                var giftsToBuy = giftList
                    //.Where(x => !x.Limited || x.CurrentSupply>0)
                    .Where(x => x is {  CurrentSupply: > 0 ,Limited: true })
                    .ToArray();

                if (giftsToBuy.Length == 0)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }
                
                
                var invoices = config.Value.GiftInvoices;

                logger.LogInformation("📄 Загружено {Count} инвойсов для обработки", invoices.Count);
                
                var tasks = new List<Task>();
                foreach (var invoice in invoices)
                {
                    tasks.Add(Task.Run(() => ProcessInvoiceAsync(invoice, giftsToBuy, cancellationToken), cancellationToken));
                }

                await Task.WhenAll(tasks);
                    

                _knownGifts = giftList.Select(x => x.Id).ToHashSet();
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("⏹ AutoBuyingService остановлен по CancellationToken");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Ошибка в AutoBuyingService");
                await Task.Delay(1000, cancellationToken);
            }
        }
        
        
        logger.LogInformation("✅ AutoBuyingService завершил работу");
    }
    
    private async Task ProcessInvoiceAsync(GiftInvoice invoice, Gift[] gifts, CancellationToken cancellationToken = default)
    {
        var suitableGiftsQuery = gifts
            .Where(x => invoice.MinPrice <= x.Price && x.Price <= invoice.MaxPrice);
        
        if (invoice.MaxSupply is not null) suitableGiftsQuery = suitableGiftsQuery
            .Where(x=>x.TotalSupply <= invoice.MaxSupply);
            
        
        var suitableGifts = suitableGiftsQuery.OrderByDescending(x => x.Price).ToArray();

        if (!suitableGifts.Any())
        {
            logger.LogDebug("Инвойс #{InvoiceId}: нет подходящих подарков", invoice.Id);
            return;
        }

        logger.LogInformation("📄 Обрабатываю инвойс #{InvoiceId}, осталось купить {Amount} шт.",
            invoice.Id, invoice.Amount);

        var buyGifts = suitableGifts.Take(invoice.Amount).ToList();

        if (buyGifts.Count < invoice.Amount)
        {
            var index = 0;
            while (buyGifts.Count < invoice.Amount && suitableGifts.Length > 0)
            {
                buyGifts.Add(suitableGifts[index % suitableGifts.Length]);
                index++;
            }
        }
        
        var buyTasks = buyGifts
            .Select(gift => telegramGiftBuyer.BuyGiftAsync(gift, invoice.RecipientId, invoice.RecipientType, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(buyTasks);
        var successful = results.Where(x => x is not null).Cast<GiftTransaction>().ToList();

        invoice.Amount -= successful.Count;

        logger.LogInformation("🎁 Куплено {Count} подарков для инвойса #{InvoiceId}",
            successful.Count, invoice.Id);

        if (invoice.Amount <= 0)
        {
            config.Value.GiftInvoices.Remove(invoice);
            logger.LogInformation("✅ Инвойс #{InvoiceId} полностью исполнен", invoice.Id);
        }
        
    }
}