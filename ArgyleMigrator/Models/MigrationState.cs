using System;
using System.Collections.Generic;

namespace ArgyleMigrator.Models
{
    public class MigrationState
    {
        public string TeamId { get; set; }
        public string TeamName { get; set; }
        public DateTime MigrationStarted { get; set; }
        public DateTime? MigrationCompleted { get; set; }
        public List<ChannelMigrationState> Channels { get; set; } = new List<ChannelMigrationState>();
    }

    public class ChannelMigrationState
    {
        public string ChannelId { get; set; }
        public string ChannelName { get; set; }
        public string SlackChannelId { get; set; }
        public bool ChannelCreated { get; set; }
        public bool MessagesImported { get; set; }
        public bool FilesImported { get; set; }
        public bool MigrationCompleted { get; set; }
        public DateTime? LastUpdated { get; set; }
        public int MessageCount { get; set; }
        public int FileCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
} 