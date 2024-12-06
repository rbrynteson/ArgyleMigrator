namespace ArgyleMigrator.Models
{
    public class OneDrive
    {
        public class DriveItemResponse
        {
            public string Id { get; set; }
            public string WebUrl { get; set; }
        }

        public class UploadSessionResponse
        {
            public string UploadUrl { get; set; }
        }
    }
}
