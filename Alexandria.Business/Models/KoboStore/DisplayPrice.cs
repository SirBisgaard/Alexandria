namespace Alexandria.Business.Models.KoboStore;

public record DisplayPrice
{
    public string CurrencyCode { get; set; } = "USD";
    public int TotalAmount { get; set; } = 0;
}