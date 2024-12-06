using ArgyleMigrator.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ArgyleMigrator.Utils
{
    public class Messages
    {
        public static async Task ScanMessagesByChannel(List<Models.Combined.ChannelsMapping> channelsMapping, string basePath,
            List<ViewModels.SimpleUser> slackUserList, string aadAccessToken, string selectedTeamId, bool copyFileAttachments)
        {
            foreach (var v in channelsMapping)
            {
                await GetAndUploadMessages(v, basePath, slackUserList, aadAccessToken, selectedTeamId, copyFileAttachments);
            }
        }

        static async Task<List<Models.Combined.AttachmentsMapping>> GetAndUploadMessages(Models.Combined.ChannelsMapping channelsMapping, string basePath, 
            List<ViewModels.SimpleUser> slackUserList, String aadAccessToken, String selectedTeamId, bool copyFileAttachments)
        {
            var messageList = new List<ViewModels.SimpleMessage>();
            messageList.Clear();
            
            var messageListJsonSource = new JArray();
            messageListJsonSource.Clear();

            List<Models.Combined.AttachmentsMapping> attachmentsToUpload = new List<Models.Combined.AttachmentsMapping>();
            attachmentsToUpload.Clear();

            Console.WriteLine("Migrating messages in channel " + channelsMapping.slackChannelName);
            foreach (var file in Directory.GetFiles(Path.Combine(basePath, channelsMapping.slackChannelName)))
            {
                Console.WriteLine("File " + file);
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                using (StreamReader sr = new StreamReader(fs))
                using (JsonTextReader reader = new JsonTextReader(sr))
                {
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.StartObject)
                        {
                            // Create Object
                            JObject obj = JObject.Load(reader);
                            var messageTs = (string)obj.SelectToken("ts");
                            var messageText = (string)obj.SelectToken("text");
                            var messageId = channelsMapping.slackChannelId + "." + messageTs;
                            var messageSender = Utils.Messages.FindMessageSender(obj, slackUserList);
                            var messageSenderId = (string)obj.SelectToken("user");
                            var threadTs = (string)obj.SelectToken("thread_ts");
                            MessageType messageType = string.IsNullOrEmpty(threadTs) || threadTs == messageTs ? MessageType.Message : MessageType.Reply;
                            
                            // Mark System Message So They Do Not Import
                            var subType = (string)obj.SelectToken("subtype");
                            if (subType == "channel_join")
                            {
                                messageType = MessageType.System;
                            }

                            //// Removing because right now all I know about is attachments
                            //// create a list of attachments to upload deal with "attachments" that are files specifically, files hosted by Slack

                            //// SelectToken returns null not an empty string if nothing is found
                            //var fileUrl = (string)obj.SelectToken("file.url_private");
                            //var fileId = (string)obj.SelectToken("file.id");
                            //var fileMode = (string)obj.SelectToken("file.mode");
                            //var fileName = (string)obj.SelectToken("file.name");

                            //ViewModels.SimpleMessage.FileAttachment fileAttachment = null;
                            //if (fileMode != "external" && fileId != null && fileUrl != null)
                            //{
                            //    Console.WriteLine("Message attachment found with ID " + fileId);
                            //    attachmentsToUpload.Add(new Models.Combined.AttachmentsMapping
                            //    {
                            //        attachmentId = fileId,
                            //        attachmentUrl = fileUrl,
                            //        attachmentChannelId = channelsMapping.slackChannelId,
                            //        attachmentFileName = fileName,
                            //        msChannelName = channelsMapping.DisplayName
                            //    });

                            //    // map the attachment to fileAttachment which is used in the viewmodel
                            //    fileAttachment = new ViewModels.SimpleMessage.FileAttachment
                            //    {
                            //        id = fileId,
                            //        originalName = (string)obj.SelectToken("file.name"),
                            //        originalTitle = (string)obj.SelectToken("file.title"),
                            //        originalUrl = (string)obj.SelectToken("file.permalink")
                            //    };
                            //}

                            // deal with "attachments" that aren't files
                            List<ViewModels.SimpleMessage.Attachments> attachmentsList = new List<ViewModels.SimpleMessage.Attachments>();
                            var attachmentsObject = (JArray)obj.SelectToken("files");
                            if (attachmentsObject != null)
                            {
                                foreach (var attachmentItem in attachmentsObject)
                                {
                                    var attachmentName = (string)attachmentItem.SelectToken("name");
                                    var attachmentItemToAdd = new ViewModels.SimpleMessage.Attachments();

                                    if (!String.IsNullOrEmpty(attachmentName))
                                    {
                                        attachmentItemToAdd.name = attachmentName;
                                    }

                                    var attachmentUrl = (string)attachmentItem.SelectToken("url_private");
                                    if (!String.IsNullOrEmpty(attachmentUrl))
                                    {
                                        attachmentItemToAdd.url = attachmentUrl;
                                    }
                                    attachmentsList.Add(attachmentItemToAdd);
                                }
                            }
                            else
                            {
                                attachmentsList = null;
                            }

                            // do some stuff with slack message threading at some point
                            messageList.Add(new ViewModels.SimpleMessage
                            {
                                id = messageId,
                                text = messageText,
                                ts = messageTs,
                                threadTs = threadTs,
                                user = messageSender,
                                userId = messageSenderId,
                                messageType = messageType,
                                //fileAttachment = fileAttachment,
                                attachments = attachmentsList,
                            });
                        }

                    }
                }
            }

            if (copyFileAttachments)
            {
                // Step 0: Get WebUrl of Teams Channel For Upload via Rest Sharp
                var teamsUploadEndpoint = await Utils.Channels.GetFilesFolderInfo(aadAccessToken, selectedTeamId, channelsMapping.Id);

                // Step 0A: Wait for SharePoint to catch up
                await Task.Delay(5000);

                // Step 0B: Get Folder ID for Upload
                string folderId = await Utils.Files.GetChannelFolderId(aadAccessToken, selectedTeamId, channelsMapping.DisplayName);

                foreach (var messageItem in messageList)
                {
                    if(messageItem.attachments != null)
                    {
                        foreach (var attachment in messageItem.attachments)
                        {
                            var attachmentUrl = attachment.url;

                            // Step 1: Download File
                            var tempFilePath = Utils.Files.DownloadFileFromUrl(attachmentUrl, attachment.name);

                            // Step 2: Check If Exists - Upload and Clean Up
                            if (File.Exists(tempFilePath))
                            {
                                // Step 2: Upload file to Teams Channel
                                // string aadAccessToken, string groupId, string parentId, string tempFilePath, string originalName
                                await Utils.Files.UploadFileToChannelFolder(aadAccessToken, selectedTeamId, folderId, tempFilePath, attachment.name);

                                // Step 3: Clean up - delete the temporary file
                                File.Delete(tempFilePath);
                            }
                        }
                    }
                }
            }

            await PostMessagesToChannel(aadAccessToken, selectedTeamId, channelsMapping.Id, messageList, slackUserList);

            return attachmentsToUpload;
        }

        public static async Task PostMessagesToChannel(string aadAccessToken, string selectedTeamId, string channelId, List<SimpleMessage> messageList, List<ViewModels.SimpleUser> slackUserList)
        {
            // Create RestSharp client
            var client = new RestClient("https://graph.microsoft.com/v1.0");

            // Dictionary to store ts -> messageId mapping for threading
            Dictionary<string, string> tsToMessageIdMap = new Dictionary<string, string>();

            // Step 1: Post All Main Messages
            foreach (var message in messageList)
            {
                // Validate Message Type
                if (message.messageType != MessageType.Message) continue;

                // Skip If No Text
                if (message.text == null)
                {
                    continue;
                }

                // Lookup the user in the slackUserList
                var user = slackUserList.FirstOrDefault(u => u.userId == message.userId);
                if (user == null)
                {
                    Console.WriteLine($"User with ID '{message.userId}' not found in Slack user list. Skipping message.");
                    continue;
                }

                // Use the O365Id from the user if available
                string o365Id = user.O365Id;
                if (string.IsNullOrEmpty(o365Id))
                {
                    Console.WriteLine($"User '{user.name}' does not have a valid O365Id. Skipping message.");
                    continue;
                }

                // Setup Time
                DateTime createdDateTime;

                // Try to convert the Unix timestamp from message.ts to DateTime
                string createdDateTimeString = Utils.General.ConvertUnixTimestampToDateTime(double.Parse(message.ts)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                // Construct the message payload
                var messagePayload = new
                {
                    createdDateTime = createdDateTimeString,
                    from = new
                    {
                        user = new
                        {
                            id = o365Id,
                            displayName = user.name,
                            userIdentityType = "aadUser"
                        }
                    },
                    body = new
                    {
                        contentType = "html",
                        content = message.text
                    }
                };

                // Serialize the payload to JSON
                var requestJson = JsonConvert.SerializeObject(messagePayload);

                // Create RestSharp request
                var request = new RestRequest($"teams/{selectedTeamId}/channels/{channelId}/messages", Method.Post);
                request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                request.AddHeader("Content-Type", "application/json");
                request.AddJsonBody(requestJson);

                try
                {
                    // Execute the request
                    var response = await client.ExecuteAsync(request);

                    // Check response status and log it
                    if (response.IsSuccessful)
                    {
                        Console.WriteLine($"Message successfully posted: {message.text}");
                        var responseObject = JsonConvert.DeserializeObject<dynamic>(response.Content);
                        tsToMessageIdMap[message.ts] = responseObject.id;
                    }
                    else
                    {
                        Console.WriteLine($"Error posting message: {response.StatusCode} - {response.StatusDescription}");
                        Console.WriteLine("Response Details: " + response.Content);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("An error occurred while posting the message: " + ex.Message);
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine("Inner exception: " + ex.InnerException.Message);
                    }
                }

                // Optional delay to avoid throttling if sending a lot of messages quickly
                await Task.Delay(500);
            }

            // Step 2: Post replies to messages
            foreach (var message in messageList)
            {
                if (message.messageType != MessageType.Reply) continue;

                // Find the parent message ID using threadTs
                if (!tsToMessageIdMap.TryGetValue(message.threadTs, out string parentMessageId))
                {
                    Console.WriteLine($"Parent message for thread '{message.threadTs}' not found. Skipping reply.");
                    continue;
                }

                string createdDateTimeString = Utils.General.ConvertUnixTimestampToDateTime(double.Parse(message.ts)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                // Construct the reply payload
                var replyPayload = new
                {
                    createdDateTime = createdDateTimeString,
                    from = new
                    {
                        user = new
                        {
                            id = slackUserList.First(u => u.userId == message.userId).O365Id,
                            displayName = message.user,
                            userIdentityType = "aadUser"
                        }
                    },
                    body = new
                    {
                        contentType = "html",
                        content = message.text
                    }
                };

                var replyRequestJson = JsonConvert.SerializeObject(replyPayload);
                var replyRequest = new RestRequest($"teams/{selectedTeamId}/channels/{channelId}/messages/{parentMessageId}/replies", Method.Post);
                replyRequest.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                replyRequest.AddHeader("Content-Type", "application/json");
                replyRequest.AddJsonBody(replyRequestJson);

                try
                {
                    var response = await client.ExecuteAsync(replyRequest);
                    if (response.IsSuccessful)
                    {
                        Console.WriteLine($"Reply successfully posted: {message.text}");
                    }
                    else
                    {
                        Console.WriteLine($"Error posting reply: {response.StatusCode} - {response.StatusDescription}");
                        Console.WriteLine("Response Details: " + response.Content);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("An error occurred while posting the reply: " + ex.Message);
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine("Inner exception: " + ex.InnerException.Message);
                    }
                }

                await Task.Delay(500); // Optional delay
            }
        }


        static string FindMessageSender(JObject obj, List<ViewModels.SimpleUser> slackUserList)
        {
            var user = (string)obj.SelectToken("user");
            if (!String.IsNullOrEmpty(user))
            {
                if (user != "USLACKBOT")
                {
                    var simpleUser = slackUserList.FirstOrDefault(w => w.userId == user);
                    if (simpleUser != null)
                    {
                        return simpleUser.name;
                    }

                }
                else
                {
                    return "SlackBot";
                }
            }
            else if (!(String.IsNullOrEmpty((string)obj.SelectToken("username"))))
            {
                return (string)obj.SelectToken("username");
            }
            else if (!(String.IsNullOrEmpty((string)obj.SelectToken("bot_id"))))
            {
                return (string)obj.SelectToken("bot_id");
            }

            return "";
        }
    }
}

