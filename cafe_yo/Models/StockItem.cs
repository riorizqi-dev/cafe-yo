namespace cafe_yo.Models
{
    public sealed class StockItem
    {
        public int StockItemId { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public int? Quantity { get; set; }
        public int? MinQuantity { get; set; }
    }
}
