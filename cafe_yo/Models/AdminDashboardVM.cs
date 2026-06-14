using System;
using System.Collections.Generic;

namespace cafe_yo.Models
{
    public sealed class AdminDashboardVM
    {
        public int TotalMenu { get; set; }
        public int TotalTables { get; set; }
        public int TotalOrders { get; set; }
        public int LowStockCount { get; set; }
        public decimal? TodayRevenue { get; set; }
        public int? TodayOrders { get; set; }
        public List<RecentOrderRow> RecentOrders { get; set; } = new();
    }

    public sealed class RecentOrderRow
    {
        public int? OrderId { get; set; }
        public string? TableNumber { get; set; }
        public decimal? TotalAmount { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? Status { get; set; }
    }
}
