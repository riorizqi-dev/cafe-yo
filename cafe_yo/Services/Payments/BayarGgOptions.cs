namespace cafe_yo.Services.Payments
{
    public sealed class BayarGgOptions
    {
        public const string SectionName = "PaymentGateway:BayarGG";

        public string BaseUrl { get; set; } = "https://www.bayar.gg";
        public string ApiKey { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 30;
    }
}
