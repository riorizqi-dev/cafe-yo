namespace cafe_yo.Models
{
    public sealed class OwnerDashboardVm
    {
        public decimal TodayRevenue { get; set; }
        public int TodayTransactions { get; set; }
        public decimal WeekRevenue { get; set; }
        public int WeekTransactions { get; set; }
        public decimal MonthRevenue { get; set; }
        public int MonthTransactions { get; set; }
        public decimal ExpensesToday { get; set; }
        public decimal ExpensesWeek { get; set; }
        public decimal ExpensesMonth { get; set; }
        public decimal EstimatedCost { get; set; }
        public decimal EstimatedProfit { get; set; }
        public decimal AverageTransaction { get; set; }
        public decimal AveragePurchasePerCustomer { get; set; }
        public string BusiestHourLabel { get; set; } = "-";
        public string BusiestDayLabel { get; set; } = "-";
        public string ReportRange { get; set; } = "monthly";
        public bool ShowDebug { get; set; }
        public int DebugQualifiedOrders { get; set; }
        public decimal DebugQualifiedTotal { get; set; }
        public List<OwnerTopProductVm> TopProducts { get; set; } = new();
        public List<OwnerTopProductVm> LowProducts { get; set; } = new();
        public List<OwnerTopProductVm> TopProfitProducts { get; set; } = new();
        public List<OwnerRecentTransactionVm> RecentTransactions { get; set; } = new();
        public List<OwnerOmzetPointVm> OmzetSeries { get; set; } = new();
        public List<OwnerOmzetPointVm> TransactionSeries { get; set; } = new();
        public List<OwnerCategoryRevenueVm> CategoryRevenue { get; set; } = new();
        public List<OwnerBusinessAlertVm> Alerts { get; set; } = new();
        public List<OwnerAutoInsightVm> AutoInsights { get; set; } = new();
    }

    public sealed class OwnerTopProductVm
    {
        public string Name { get; set; } = string.Empty;
        public int Qty { get; set; }
    }

    public sealed class OwnerRecentTransactionVm
    {
        public int OrderId { get; set; }
        public string? Table { get; set; }
        public DateTime Date { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = "-";
    }

    public sealed class OwnerOmzetPointVm
    {
        public string Label { get; set; } = string.Empty;
        public decimal Value { get; set; }
    }

    public sealed class OwnerCategoryRevenueVm
    {
        public string Category { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
    }

    public sealed class OwnerBusinessAlertVm
    {
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Severity { get; set; } = "info";
    }

    public sealed class OwnerAutoInsightVm
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
