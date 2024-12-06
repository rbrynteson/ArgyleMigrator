using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace ArgyleMigrator.Models
{
    public class MsTeams
    {
        public class Team
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; }
            public string Visibility { get; set; }
            public DateTime? CreatedDateTime { get; set; }
            public List<Channel> Channels { get; set; }
            public string TeamCreationMode { get; set; }
            public bool? IsArchived { get; set; }
        }

        public class Channel
        {
            public string Id { get; set; }
            public string DisplayName { get; set; }
            public string Description { get; set; } = "";
            public string FolderId { get; set; } = "";
            public string MembershipType { get; set; } = "";
            public DateTime? CreatedDateTime { get; set; }
        }

        public class TeamCreationRequest
        {
            [JsonProperty("@microsoft.graph.teamCreationMode")]
            public string TeamCreationMode { get; set; } = "migration";

            [JsonProperty("template@odata.bind")]
            public string Template { get; set; } = "https://graph.microsoft.com/v1.0/teamsTemplates('standard')";

            [JsonProperty("displayName")]
            public string DisplayName { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("createdDateTime")]
            public string CreatedDateTime { get; set; }
        }

        public class ChannelCreationRequest
        {
            [JsonProperty("@microsoft.graph.channelCreationMode")]
            public string ChannelCreationMode { get; set; } = "migration";

            [JsonProperty("displayName")]
            public string DisplayName { get; set; }

            [JsonProperty("membershipType")]
            public string MembershipType { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("createdDateTime")]
            public string CreatedDateTime { get; set; }
        }

        public class ChannelResponse
        {
            [JsonProperty("value")]
            public List<Channel> Channels { get; set; }
        }


        public class ChannelFileFolderResponse
        {
            public string odatacontext { get; set; }
            public string id { get; set; }
            public DateTime createdDateTime { get; set; }
            public DateTime lastModifiedDateTime { get; set; }
            public string name { get; set; }
            public string webUrl { get; set; }
            public int size { get; set; }
            public Parentreference parentReference { get; set; }
            public Filesysteminfo fileSystemInfo { get; set; }
            public Folder folder { get; set; }
        }

        public class Parentreference
        {
            public string driveId { get; set; }
            public string driveType { get; set; }
        }

        public class Filesysteminfo
        {
            public DateTime createdDateTime { get; set; }
            public DateTime lastModifiedDateTime { get; set; }
        }

        public class Folder
        {
            public int childCount { get; set; }
        }

    }
}
