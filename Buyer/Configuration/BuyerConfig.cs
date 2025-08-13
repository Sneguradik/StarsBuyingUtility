using Buyer.Models;

namespace Buyer.Configuration;

public class BuyerConfig
{
    public List<GiftInvoice> GiftInvoices { get; set; } = new();
}