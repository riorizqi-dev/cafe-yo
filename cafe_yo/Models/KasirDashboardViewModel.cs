namespace cafe_yo.Models
{
    public sealed class KasirDashboardViewModel
    {
        public List<KasirTableCard> Tables { get; set; } = new();
        public List<KasirMenuOption> MenuItems { get; set; } = new();
    }

    public sealed class KasirTableCard
    {
        public int TableNumber { get; set; }
        public string Status { get; set; } = "Kosong";
    }

    public sealed class KasirMenuOption
    {
        public int MenuItemId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public bool IsAvailable { get; set; }
        public string? ImageUrl { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
    }
}
