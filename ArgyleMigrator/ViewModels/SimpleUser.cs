namespace ArgyleMigrator.ViewModels
{
    public class SimpleUser
    {
        public string userId { get; set; }
        public string name { get; set; }
        public string O365Id { get; set; }
        public string real_name { get; set; }
        public string email { get; set; }
        public bool is_bot { get; set; } = false; 
    }
}
