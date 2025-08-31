using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Buyer.Configuration;
using Buyer.Models;
using Microsoft.Extensions.Options;

namespace Buyer.Services;

public interface IAutoBuyingService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
public class AutoBuyingService(
    ILogger<AutoBuyingService> logger,
    ITelegramGiftBuyer telegramGiftBuyer,
    IOptions<BuyerConfig> config) : IAutoBuyingService
{
    private readonly object _knownLock = new();
    private readonly HashSet<long> _knownGifts = new();

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("🚀 AutoBuyingService запущен");
        
        var maxConcurrentInvoices = config.Value.MaxConcurrentInvoices > 0
            ? config.Value.MaxConcurrentInvoices
            : 3;
        using var invoiceLimiter = new SemaphoreSlim(maxConcurrentInvoices, maxConcurrentInvoices);

        while (!cancellationToken.IsCancellationRequested)
        {
            IEnumerable<Gift> giftList = [];

            try
            {
                logger.LogInformation("Started checking");

                giftList = await telegramGiftBuyer.GetAvailableGiftsAsync(cancellationToken);

                var (newIds, knownSnapshot) = UpdateKnownGifts(giftList.Select(g => g.Id));

                LogFullReport(
                    currentGiftIds: giftList.Select(g => g.Id),
                    knownSnapshot: knownSnapshot,
                    newIds: newIds,
                    error: null);
                
                var invoices = config.Value.GiftInvoices
                    .Where(i => i.Amount > 0)
                    .ToList();

                if (invoices.Count > 0)
                {
                    var tasks = new List<Task>();
                    foreach (var invoice in invoices)
                    {
                        tasks.Add(RunWithLimiterAsync(
                            invoiceLimiter,
                            () => ProcessInvoiceAsync(invoice, giftList, cancellationToken),
                            cancellationToken));
                    }

                    await Task.WhenAll(tasks);
                    
                    config.Value.GiftInvoices.RemoveAll(i => i.Amount <= 0);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Loop error: {Message}", ex.Message);

                LogFullReport(
                    currentGiftIds: giftList.Select(g => g.Id),
                    knownSnapshot: SafeSnapshotKnown(),
                    newIds: Enumerable.Empty<long>(),
                    error: ex);
            }
        }
    }

    // helper: запускает задачу под семафором (не более N одновременно)
    private static async Task RunWithLimiterAsync(
        SemaphoreSlim limiter,
        Func<Task> action,
        CancellationToken ct)
    {
        await limiter.WaitAsync(ct);
        try
        {
            await action();
        }
        finally
        {
            limiter.Release();
        }
    }

    // Заглушка: здесь твоя логика под конкретный инвойс (фильтрация gifts по цене и т.п.)
    private async Task ProcessInvoiceAsync(
        GiftInvoice invoice,
        IEnumerable<Gift> allGifts,
        CancellationToken ct)
    {
        // пример: только новые лимитированные с остатком (если уже отслеживаешь это)
        var candidates = allGifts
            .Where(g => g.Limited && g.CurrentSupply > 0) // можно сузить фильтр под стратегию
            .Where(g => invoice.MinPrice <= g.Price && g.Price <= invoice.MaxPrice)
            .Where(g => invoice.MaxSupply is null || g.TotalSupply <= invoice.MaxSupply)
            .OrderByDescending(g => g.Price)
            .Take(invoice.Amount)
            .ToList();

        if (!candidates.Any())
        {
            logger.LogDebug("Инвойс #{InvoiceId}: подходящих подарков нет", invoice.Id);
            return;
        }

        logger.LogInformation("Инвойс #{InvoiceId}: начата обработка {Cnt} кандидатов (осталось {Left})",
            invoice.Id, candidates.Count, invoice.Amount);

        // тут можно вызвать твой BuyGiftAsync и уменьшать invoice.Amount по факту успеха
        foreach (var gift in candidates)
        {
            if (invoice.Amount <= 0) break;

            try
            {
                var res = await telegramGiftBuyer.BuyGiftAsync(gift, invoice.RecipientId, invoice.RecipientType, ct);
                if (res is not null /* и/или res.Success */)
                {
                    invoice.Amount -= 1;
                    logger.LogInformation("🎁 Куплен gift {GiftId} для инвойса #{InvoiceId}. Осталось {Left}",
                        gift.Id, invoice.Id, invoice.Amount);
                }
                else
                {
                    logger.LogWarning("⚠️ Покупка gift {GiftId} отклонена/неуспешна для инвойса #{InvoiceId}",
                        gift.Id, invoice.Id);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "❌ Ошибка покупки gift {GiftId} для инвойса #{InvoiceId}", gift.Id, invoice.Id);
            }
        }

        logger.LogInformation("Инвойс #{InvoiceId}: завершена обработка. Осталось {Left}",
            invoice.Id, invoice.Amount);
    }

    private (IEnumerable<long> newIds, IEnumerable<long> snapshotKnown) UpdateKnownGifts(IEnumerable<long> currentIds)
    {
        List<long> added = new();

        lock (_knownLock)
        {
            foreach (var id in currentIds)
                if (_knownGifts.Add(id)) added.Add(id);

            var snap = _knownGifts.OrderBy(x => x).ToArray();
            return (added, snap);
        }
    }

    private IEnumerable<long> SafeSnapshotKnown()
    {
        lock (_knownLock)
            return _knownGifts.OrderBy(x => x).ToArray();
    }

    private void LogFullReport(
        IEnumerable<long> currentGiftIds,
        IEnumerable<long> knownSnapshot,
        IEnumerable<long> newIds,
        Exception? error)
    {
        var payload = new
        {
            TsUtc = DateTimeOffset.UtcNow,
            CurrentGiftIds = currentGiftIds.OrderBy(x => x).ToArray(),
            KnownSetIds = knownSnapshot.OrderBy(x => x).ToArray(),
            NewIds = newIds.Distinct().OrderBy(x => x).ToArray(),
            Error = error is null ? null : new
            {
                Type = error.GetType().FullName,
                error.Message,
                error.StackTrace
            }
        };

        logger.LogInformation("📑 FullReport {@Report}", payload);
    }
}