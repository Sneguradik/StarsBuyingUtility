using System.ComponentModel.DataAnnotations;

namespace Buyer.Models;

public class GiftTransaction
{
    [Key]
    public long Id { get; set; }
    public long RecipientId { get; set; }
    public int Price { get; set; }
    public long GiftId { get; set; } 
    public DateTime TransactionDate { get; set; }
}