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

namespace ArgyleMigrator.Utils
{
    public class Channels
    {
        public static List<Slack.Channels> ScanSlackChannelsJson(string combinedPath)
        {
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

                        // don't force use of the Slack channel id field in a channels.json only creation operation
                        // i.e. we're not importing from a Slack archive but simply bulk creating new channels
                        // this means we must check if "id" is null, otherwise we get an exception

                        var channelId = (string)obj.SelectToken("id");
                        if (channelId == null) {
                            channelId = "";
                        }

                        slackChannels.Add(new Models.Slack.Channels()
                        {
                            channelId = channelId,
                            channelName = obj["name"].ToString(),
                            channelDescription = obj["purpose"]["value"].ToString()
                        });

                        // artificially limit the number of channels scanned as to make testing go faster
                        if (slackChannels.Count > 10)
                        {
                            return slackChannels;
                        }
                    }
                }
            }
            return slackChannels;
        }

        public static async Task<List<Combined.ChannelsMapping>> CreateChannelsInMsTeams(string aadAccessToken, string teamId, List<Slack.Channels> slackChannels, string basePath)
        {
            List<Combined.ChannelsMapping> combinedChannelsMapping = new List<Combined.ChannelsMapping>();

            using (HttpClient httpClient = new HttpClient())
            {
                // Set up HttpClient with the access token
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aadAccessToken);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                foreach (var slackChannel in slackChannels)
                {
                    // Assuming the folder is automatically created by Teams/SharePoint
                    Console.WriteLine("Creating Teams Channel " + slackChannel.channelName + " with this Description " + slackChannel.channelDescription);

                    try
                    {
                        // Override Channel Name if it is "General" as it is a reserved name in MS Teams
                        string channelName = slackChannel.channelName;
                        if (channelName.ToLower() == "general")
                        {
                            channelName = "general" + new Random().Next(1000, 9999);
                        }

                        // Set up variables for retry logic
                        bool channelCreated = false;
                        int retryCount = 0;
                        const int maxRetries = 3;

                        while (!channelCreated && retryCount < maxRetries)
                        {
                            // Construct the channel creation request payload
                            var slackChannelAsMsChannelObject = new MsTeams.ChannelCreationRequest
                            {
                                DisplayName = channelName,
                                Description = slackChannel.channelDescription,
                                MembershipType = "standard",
                                CreatedDateTime = DateTime.UtcNow.AddMonths(-6).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                            };

                            var createTeamsChannelPostData = JsonConvert.SerializeObject(slackChannelAsMsChannelObject);

                            // Log request data for debugging purposes
                            Console.WriteLine($"Attempt {retryCount + 1}: Request Payload: " + createTeamsChannelPostData);

                            // Create the RestSharp client
                            var client = new RestClient("https://graph.microsoft.com/v1.0");

                            // Create the request
                            var request = new RestRequest($"teams/{teamId}/channels", Method.Post);
                            request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                            request.AddHeader("Content-Type", "application/json");
                            request.AddJsonBody(createTeamsChannelPostData);

                            // Execute the request
                            var response = await client.ExecuteAsync(request);

                            // Check response status and log it
                            if (response.IsSuccessful)
                            {
                                Console.WriteLine("Channel created successfully.");

                                // Parse the response to get the created channel details
                                var createdMsTeamsChannel = JsonConvert.DeserializeObject<MsTeams.Channel>(response.Content);

                                // Add mapping for created channel (FolderId can be omitted if not required)
                                combinedChannelsMapping.Add(new Combined.ChannelsMapping()
                                {
                                    Id = createdMsTeamsChannel.Id,
                                    DisplayName = createdMsTeamsChannel.DisplayName,
                                    Description = createdMsTeamsChannel.Description,
                                    slackChannelId = slackChannel.channelId,
                                    slackChannelName = slackChannel.channelName,
                                });

                                // Mark the channel as created and exit the loop
                                channelCreated = true;

                                // Wait for a while to avoid throttling
                                await Task.Delay(2000);
                            }
                            else
                            {
                                Console.WriteLine($"Error creating channel: {response.StatusCode} - {response.StatusDescription}");
                                Console.WriteLine("Response Details: " + response.Content);

                                // Parse the response to see if the error is related to a duplicate name
                                if (response.Content != null)
                                {
                                    var responseJson = JObject.Parse(response.Content);
                                    var errorCode = responseJson["error"]?["innerError"]?["code"]?.ToString();

                                    if (errorCode == "NameAlreadyExists")
                                    {
                                        // Append a random number to the channel name to attempt creating a unique one
                                        channelName = slackChannel.channelName + new Random().Next(1000, 9999);
                                        Console.WriteLine($"Duplicate channel name detected. Retrying with new name: {channelName}");
                                    }
                                    else
                                    {
                                        // If it's a different kind of error, log it and break
                                        Console.WriteLine("Unrecoverable error occurred. Exiting retry loop.");
                                        break;
                                    }
                                }

                                // Increment the retry count
                                retryCount++;
                            }
                        }

                        // If the maximum number of retries has been reached, log a failure message
                        if (!channelCreated)
                        {
                            Console.WriteLine("Failed to create the channel after multiple attempts.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("An error occurred: " + ex.Message);
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine("Inner exception: " + ex.InnerException.Message);
                        }
                    }
                }

                return combinedChannelsMapping;
            }
        }

        public static async Task<string> GetFilesFolderInfo(string aadAccessToken, string teamId, string channelId)
        {
            // Create RestSharp client
            var client = new RestClient("https://graph.microsoft.com");

            // Create the RestSharp request
            var request = new RestRequest($"v1.0/teams/{teamId}/channels/{channelId}/filesFolder", Method.Get);
            request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
            request.AddHeader("Accept", "application/json");

            try
            {
                // Execute the request
                var response = await client.ExecuteAsync(request);

                // Check the response status
                if (response.IsSuccessful)
                {
                    Console.WriteLine("Successfully retrieved files folder information.");
                    Console.WriteLine("Response Content: " + response.Content);

                    // Deserialize the JSON response (if necessary)
                    var responseData = JsonConvert.DeserializeObject<ChannelFileFolderResponse>(response.Content);

                    // Return the webUrl
                    return responseData?.webUrl;
                }
                else
                {
                    Console.WriteLine($"Error retrieving files folder info: {response.StatusCode} - {response.StatusDescription}");
                    Console.WriteLine("Response Details: " + response.Content);
                    return null; // Handle error appropriately
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while retrieving files folder info: " + ex.Message);
                if (ex.InnerException != null)
                {
                    Console.WriteLine("Inner exception: " + ex.InnerException.Message);
                }
                return null; // Handle errors appropriately
            }
        }

        public static async Task<bool> CompleteChannelMigration(string aadAccessToken, string teamId, string channelId)
        {
            // Create RestSharp client
            var client = new RestClient("https://graph.microsoft.com/v1.0");

            // Create the RestSharp request
            var request = new RestRequest($"teams/{teamId}/channels/{channelId}/completeMigration", Method.Post);
            request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
            request.AddHeader("Content-Type", "application/json");

            try
            {
                // Execute the request
                var response = await client.ExecuteAsync(request);

                // Check response status and log it
                if (response.IsSuccessful)
                {
                    Console.WriteLine($"Channel migration completed successfully for Channel ID: {channelId}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Error completing channel migration: {response.StatusCode} - {response.StatusDescription}");
                    Console.WriteLine("Response Details: " + response.Content);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while completing channel migration: " + ex.Message);
                if (ex.InnerException != null)
                {
                    Console.WriteLine("Inner exception: " + ex.InnerException.Message);
                }
                return false;
            }
        }
    }
}
