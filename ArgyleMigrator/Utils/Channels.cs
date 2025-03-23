using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using ArgyleMigrator.Models;
using System.Threading.Tasks;
using RestSharp;
using static ArgyleMigrator.Models.MsTeams;
using System.Linq;

namespace ArgyleMigrator.Utils
{
    public class Channels
    {
        public static List<Slack.Channels> ScanSlackChannelsJson(string combinedPath)
        {
            try
            {
                Logger.Information("Starting to scan Slack channels from {Path}", combinedPath);
                List<Slack.Channels> slackChannels = new List<Slack.Channels>();

                using (FileStream fs = new FileStream(combinedPath, FileMode.Open, FileAccess.Read))
                using (StreamReader sr = new StreamReader(fs))
                using (JsonTextReader reader = new JsonTextReader(sr))
                {
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.StartObject)
                        {
                            JObject obj = JObject.Load(reader);

                            var channelId = (string)obj.SelectToken("id") ?? "";
                            var channelName = obj["name"].ToString();
                            var channelDescription = obj["purpose"]["value"].ToString();

                            slackChannels.Add(new Models.Slack.Channels()
                            {
                                channelId = channelId,
                                channelName = channelName,
                                channelDescription = channelDescription
                            });

                            Logger.Debug("Found channel: {ChannelName} ({ChannelId})", channelName, channelId);
                        }
                    }
                }

                Logger.Information("Completed scanning Slack channels. Found {Count} channels", slackChannels.Count);
                return slackChannels;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error scanning Slack channels from {Path}", combinedPath);
                throw;
            }
        }

        public static List<Slack.Channels> SelectChannelsToMigrate(List<Slack.Channels> allChannels)
        {
            var selectedChannels = new List<Slack.Channels>();
            
            Console.WriteLine("\nAvailable channels to migrate:");
            for (int i = 0; i < allChannels.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {allChannels[i].channelName} - {allChannels[i].channelDescription}");
            }

            Console.WriteLine("\nEnter the numbers of channels to migrate (comma-separated) or 'all' to migrate everything:");
            var input = Console.ReadLine();

            if (input.ToLower() == "all")
            {
                return allChannels;
            }

            var selectedIndices = input.Split(',')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => int.TryParse(x, out int index) ? index - 1 : -1)
                .Where(x => x >= 0 && x < allChannels.Count)
                .Distinct();

            foreach (var index in selectedIndices)
            {
                selectedChannels.Add(allChannels[index]);
                Logger.Information("Selected channel for migration: {ChannelName}", allChannels[index].channelName);
            }

            return selectedChannels;
        }

        public static async Task<List<Combined.ChannelsMapping>> CreateChannelsInMsTeams(string aadAccessToken, string teamId, List<Slack.Channels> slackChannels, string basePath)
        {
            List<Combined.ChannelsMapping> combinedChannelsMapping = new List<Combined.ChannelsMapping>();

            using (HttpClient httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aadAccessToken);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                foreach (var slackChannel in slackChannels)
                {
                    Logger.Information("Creating Teams channel {ChannelName}", slackChannel.channelName);

                    try
                    {
                        string channelName = slackChannel.channelName;
                        if (channelName.ToLower() == "general")
                        {
                            channelName = "general" + new Random().Next(1000, 9999);
                            Logger.Warning("Channel name 'general' is reserved. Using {NewName} instead", channelName);
                        }

                        bool channelCreated = false;
                        int retryCount = 0;
                        const int maxRetries = 3;

                        while (!channelCreated && retryCount < maxRetries)
                        {
                            var channelRequest = new MsTeams.ChannelCreationRequest
                            {
                                DisplayName = channelName,
                                Description = slackChannel.channelDescription,
                                MembershipType = "standard",
                                CreatedDateTime = DateTime.UtcNow.AddMonths(-6).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                            };

                            var requestJson = JsonConvert.SerializeObject(channelRequest);
                            Logger.Debug("Attempt {RetryCount}: Creating channel with payload: {Payload}", retryCount + 1, requestJson);

                            var client = new RestClient("https://graph.microsoft.com/v1.0");
                            var request = new RestRequest($"teams/{teamId}/channels", Method.Post);
                            request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                            request.AddHeader("Content-Type", "application/json");
                            request.AddJsonBody(requestJson);

                            var response = await client.ExecuteAsync(request);

                            if (response.IsSuccessful)
                            {
                                Logger.Information("Successfully created channel {ChannelName}", channelName);

                                var createdChannel = JsonConvert.DeserializeObject<MsTeams.Channel>(response.Content);
                                var channelMapping = new Combined.ChannelsMapping()
                                {
                                    Id = createdChannel.Id,
                                    DisplayName = createdChannel.DisplayName,
                                    Description = createdChannel.Description,
                                    slackChannelId = slackChannel.channelId,
                                    slackChannelName = slackChannel.channelName,
                                };
                                combinedChannelsMapping.Add(channelMapping);

                                // Update migration state
                                MigrationStateManager.UpdateChannelState(createdChannel.Id, state =>
                                {
                                    state.ChannelName = channelName;
                                    state.SlackChannelId = slackChannel.channelId;
                                    state.ChannelCreated = true;
                                });

                                channelCreated = true;
                                await Task.Delay(2000); // Throttling delay
                            }
                            else
                            {
                                Logger.Error("Failed to create channel. Status: {StatusCode}, Response: {Response}",
                                    response.StatusCode, response.Content);

                                if (response.Content != null)
                                {
                                    var responseJson = JObject.Parse(response.Content);
                                    var errorCode = responseJson["error"]?["innerError"]?["code"]?.ToString();

                                    if (errorCode == "NameAlreadyExists")
                                    {
                                        channelName = slackChannel.channelName + new Random().Next(1000, 9999);
                                        Logger.Warning("Channel name already exists. Retrying with {NewName}", channelName);
                                    }
                                    else
                                    {
                                        Logger.Error("Unrecoverable error occurred. Error code: {ErrorCode}", errorCode);
                                        break;
                                    }
                                }

                                retryCount++;
                            }
                        }

                        if (!channelCreated)
                        {
                            Logger.Error("Failed to create channel {ChannelName} after {MaxRetries} attempts", 
                                slackChannel.channelName, maxRetries);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error creating channel {ChannelName}", slackChannel.channelName);
                    }
                }

                return combinedChannelsMapping;
            }
        }

        public static async Task<string> GetFilesFolderInfo(string aadAccessToken, string teamId, string channelId)
        {
            var client = new RestClient("https://graph.microsoft.com");
            var request = new RestRequest($"v1.0/teams/{teamId}/channels/{channelId}/filesFolder", Method.Get);
            request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
            request.AddHeader("Accept", "application/json");

            try
            {
                Logger.Debug("Retrieving files folder information for channel {ChannelId}", channelId);
                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful)
                {
                    Logger.Information("Successfully retrieved files folder information");
                    var responseData = JsonConvert.DeserializeObject<ChannelFileFolderResponse>(response.Content);
                    return responseData?.webUrl;
                }
                else
                {
                    Logger.Error("Failed to retrieve files folder info. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, response.Content);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error retrieving files folder information for channel {ChannelId}", channelId);
                return null;
            }
        }

        public static async Task<bool> CompleteChannelMigration(string aadAccessToken, string teamId, string channelId)
        {
            try
            {
                Logger.Information("Completing migration for channel {ChannelId} in team {TeamId}", channelId, teamId);
                
                var client = new RestClient("https://graph.microsoft.com/v1.0");
                var request = new RestRequest($"teams/{teamId}/channels/{channelId}/completeMigration", Method.Post);
                request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                request.AddHeader("Content-Type", "application/json");

                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful)
                {
                    Logger.Information("Successfully completed channel migration");
                    return true;
                }
                else
                {
                    Logger.Error("Failed to complete channel migration. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, response.Content);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error completing channel migration for channel {ChannelId} in team {TeamId}", 
                    channelId, teamId);
                return false;
            }
        }
    }
}
