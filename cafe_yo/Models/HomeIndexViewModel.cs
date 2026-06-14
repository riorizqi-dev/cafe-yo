namespace cafe_yo.Models
{
    public class HomeIndexViewModel
    {
        public string QrisImageUrl { get; set; } = string.Empty;
        public List<MenuItem> MenuItems { get; set; } = new();
    }
}
