namespace Buyer.Models;

public class GiftInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long RecipientId { get; set; }
    public RecipientType RecipientType { get; set; }
    public double? MinPrice { get; set; }
    public double? MaxPrice { get; set; }
    public int Amount { get; set; }
    public int? MaxSupply { get; set; }
    public DateTime Created { get; set; } =  DateTime.UtcNow;
}