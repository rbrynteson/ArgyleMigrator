using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using ArgyleMigrator.Models;
using System.Threading.Tasks;
using RestSharp;
using static ArgyleMigrator.Models.MsTeams;
using System.Linq;

namespace ArgyleMigrator.Utils
{
    public class Teams
    {
        public static async Task<MsTeams.Team> CreateTeamInMsTeams(string aadAccessToken, string displayName, string description)
        {
            // Prepare the request payload
            var teamCreationRequest = new TeamCreationRequest
            {
                DisplayName = displayName,
                Description = description,
                CreatedDateTime = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };

            // Serialize the request payload to JSON
            var requestJson = JsonConvert.SerializeObject(teamCreationRequest);

            // Create RestSharp client
            var client = new RestClient("https://graph.microsoft.com/v1.0");

            // Create the RestSharp request
            var request = new RestRequest("teams", Method.Post);
            request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
            request.AddHeader("Content-Type", "application/json");
            request.AddJsonBody(requestJson);

            try
            {
                // Execute the request
                var response = await client.ExecuteAsync(request);

                // Check response and extract team ID
                if (response.IsSuccessful || response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    Console.WriteLine("Team creation request accepted.");

                    // Extract the 'Location' header
                    if (response.Headers != null)
                    {
                        var locationHeader = response.Headers
                            .FirstOrDefault(h => h.Name.Equals("Location", StringComparison.OrdinalIgnoreCase));

                        if (locationHeader != null)
                        {
                            var locationValue = locationHeader.Value.ToString();
                            Console.WriteLine($"Location Header: {locationValue}");

                            // Extract the Team ID from the Location header
                            var teamId = ExtractTeamIdFromLocation(locationValue);

                            if (!string.IsNullOrEmpty(teamId))
                            {
                                var createdTeam = new MsTeams.Team
                                {
                                    Id = teamId,
                                    DisplayName = displayName,
                                    Description = description
                                };
                                return createdTeam;
                            }
                        }
                    }

                    Console.WriteLine("Failed to retrieve Team ID from response headers.");
                    return null; // Consider better error handling here
                }
                else
                {
                    Console.WriteLine($"Error creating team: {response.StatusCode} - {response.StatusDescription}");
                    Console.WriteLine("Response Details: " + response.Content);
                    return null; // Handle error case appropriately
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
                if (ex.InnerException != null)
                {
                    Console.WriteLine("Inner exception: " + ex.InnerException.Message);
                }
                return null; // Handle errors appropriately
            }
        }

        private static string ExtractTeamIdFromLocation(string locationValue)
        {
            try
            {
                var startIndex = locationValue.IndexOf("teams('") + "teams('".Length;
                var endIndex = locationValue.IndexOf("')", startIndex);

                if (startIndex != -1 && endIndex != -1)
                {
                    return locationValue.Substring(startIndex, endIndex - startIndex);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error extracting team ID from location: " + ex.Message);
            }

            return null;
        }

        public static async Task<bool> CompleteTeamMigration(string aadAccessToken, string teamId)
        {
            // Create RestSharp client
            var client = new RestClient("https://graph.microsoft.com/v1.0");

            // Step 1: Get the list of channels
            var getChannelsRequest = new RestRequest($"teams/{teamId}/channels", Method.Get);
            getChannelsRequest.AddHeader("Authorization", $"Bearer {aadAccessToken}");
            getChannelsRequest.AddHeader("Content-Type", "application/json");

            try
            {
                // Execute the request to get channels
                var getChannelsResponse = await client.ExecuteAsync(getChannelsRequest);

                if (!getChannelsResponse.IsSuccessful)
                {
                    Console.WriteLine($"Error retrieving channels: {getChannelsResponse.StatusCode} - {getChannelsResponse.StatusDescription}");
                    Console.WriteLine("Response Details: " + getChannelsResponse.Content);
                    return false;
                }

                // Parse the channels response
                var responseContent = getChannelsResponse.Content;
                var channelsList = JObject.Parse(responseContent)["value"].ToObject<List<Channel>>();

                // Step 2: Loop through channels and complete their migration
                foreach (var channel in channelsList)
                {
                    bool channelMigrationSuccessful = await Utils.Channels.CompleteChannelMigration(aadAccessToken, teamId, channel.Id);

                    if (!channelMigrationSuccessful)
                    {
                        Console.WriteLine($"Failed to complete migration for Channel ID: {channel.Id}. Aborting team migration.");
                        return false;
                    }
                }

                // Step 3: Complete team migration
                var completeTeamMigrationRequest = new RestRequest($"teams/{teamId}/completeMigration", Method.Post);
                completeTeamMigrationRequest.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                completeTeamMigrationRequest.AddHeader("Content-Type", "application/json");

                var completeTeamResponse = await client.ExecuteAsync(completeTeamMigrationRequest);

                if (completeTeamResponse.IsSuccessful)
                {
                    Console.WriteLine($"Team migration completed successfully for Team ID: {teamId}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Error completing team migration: {completeTeamResponse.StatusCode} - {completeTeamResponse.StatusDescription}");
                    Console.WriteLine("Response Details: " + completeTeamResponse.Content);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while completing team migration: " + ex.Message);
                if (ex.InnerException != null)
                {
                    Console.WriteLine("Inner exception: " + ex.InnerException.Message);
                }
                return false;
            }
        }

        public static async Task<bool> AddOwnerToTeam(string aadAccessToken, string teamId, string userId)
        {
            // Create RestSharp client
            var client = new RestClient("https://graph.microsoft.com");

            // Construct the request payload
            var ownerPayload = new Dictionary<string, string>
            {
                { "@odata.id", $"https://graph.microsoft.com/v1.0/users/{userId}" }
            };

            // Serialize the payload to JSON
            var requestJson = JsonConvert.SerializeObject(ownerPayload);

            // Create RestSharp request
            var request = new RestRequest($"v1.0/groups/{teamId}/owners/$ref", Method.Post);
            request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
            request.AddHeader("Content-Type", "application/json");
            request.AddJsonBody(requestJson);

            try
            {
                // Execute the request
                var response = await client.ExecuteAsync(request);

                // Check response status and return true if successful
                if (response.IsSuccessful)
                {
                    Console.WriteLine($"User with ID '{userId}' successfully added as an owner to team '{teamId}'.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Error adding owner to team: {response.StatusCode} - {response.StatusDescription}");
                    Console.WriteLine("Response Details: " + response.Content);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while adding an owner to the team: " + ex.Message);
                if (ex.InnerException != null)
                {
                    Console.WriteLine("Inner exception: " + ex.InnerException.Message);
                }
                return false;
            }
        }

    }
}
