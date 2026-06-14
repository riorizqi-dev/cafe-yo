namespace cafe_yo.Models
{
    public sealed class KitchenOrderItemVm
    {
        public int Quantity { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public sealed class KitchenOrderCardVm
    {
        public int OrderId { get; set; }
        public int? TableNumber { get; set; }
        public DateTime? OrderDate { get; set; }
        public string Status { get; set; } = "pending";
        public DateTime UpdatedAt { get; set; }
        public string? Note { get; set; }
        public List<KitchenOrderItemVm> Items { get; set; } = new();
    }

    public sealed class KitchenDashboardVm
    {
        public List<KitchenOrderCardVm> PendingOrders { get; set; } = new();
        public List<KitchenOrderCardVm> ProcessingOrders { get; set; } = new();
        public List<KitchenOrderCardVm> ReadyOrders { get; set; } = new();
    }

    public sealed class KitchenNotificationVm
    {
        public int NotificationId { get; set; }
        public int OrderId { get; set; }
        public int? TableNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
