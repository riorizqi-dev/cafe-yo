namespace cafe_yo.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = "customer";
        public string PasswordHash { get; set; } = string.Empty; // simple placeholder
        public bool IsOnline { get; set; } = false;
    }
}