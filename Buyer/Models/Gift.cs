namespace Buyer.Models;

public class Gift
{
    public long Id { get; set; }
    public long Price { get; set; }
    public bool Limited { get; set; }
    public int? TotalSupply { get; set; }
    public int? CurrentSupply { get; set; }
}