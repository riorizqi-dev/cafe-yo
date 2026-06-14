namespace cafe_yo.Models
{
    public sealed class MenuCategory
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
