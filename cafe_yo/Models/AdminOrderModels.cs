using System;
using System.Collections.Generic;

namespace cafe_yo.Models
{
    public sealed class AdminOrderRow
    {
        public int OrderId { get; set; }
        public int? TableId { get; set; }
        public int? TableNumber { get; set; }
        public DateTime? OrderDate { get; set; }
        public string? Status { get; set; }
        public decimal? Total { get; set; }
    }

    public sealed class AdminOrderEditVM
    {
        public int OrderId { get; set; }
        public int? TableId { get; set; }
        public int? TableNumber { get; set; }
        public DateTime? OrderDate { get; set; }
        public string? Status { get; set; }
        public decimal? Total { get; set; }
        public List<AdminOrderItemRow> Items { get; set; } = new();
    }

    public sealed class AdminOrderItemRow
    {
        public string Name { get; set; } = "Item";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string? Notes { get; set; }
    }
}
