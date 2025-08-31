using Buyer.Models;

namespace Buyer.Configuration;

public class BuyerConfig
{
    public List<GiftInvoice> GiftInvoices { get; set; } = new();
    public int MaxConcurrentInvoices { get; set; }
    public long FallBackUserId { get; set; }
}