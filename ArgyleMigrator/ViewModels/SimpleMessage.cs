using System.Collections.Generic;

namespace ArgyleMigrator.ViewModels
{
    public class SimpleMessage
    {
        public string id {get;set;}
        public string user { get; set; }
        public string userId { get; set; }
        public string text { get; set; }
        public string editedByUser { get; set; }
        public string editedByUserId { get; set; }
        public string ts { get; set; }
        public string threadTs { get; set; }
        public MessageType messageType { get; set; }
        public List<Attachments> attachments { get; set; }
        public List<Reaction> reactions { get; set; }
        public FileAttachment fileAttachment { get; set; }

        public class FileAttachment
        {
            public string id { get; set; }
            public string originalName { get; set; }
            public string originalTitle { get; set; }
            public string originalUrl { get; set; }
            
            public string spoId {get;set;}
            public string spoUrl {get;set;}
        }

        public class Attachments
        {
            public string name { get; set; }
            public string url {get;set;}
        }

        public class Reaction
        {
            public string name { get; set; }
            public List<string> users { get; set; }
        }
    }

    public enum MessageType
    {
        Message,
        Reply,
        System
    }
}
