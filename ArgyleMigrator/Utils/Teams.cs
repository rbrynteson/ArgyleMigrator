using System;
using System.Threading.Tasks;
using RestSharp;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using ArgyleMigrator.Models;
using Newtonsoft.Json.Linq;

namespace ArgyleMigrator.Utils
{
    public class Teams
    {
        public static async Task<Models.MsTeams.Team> CreateTeamInMsTeams(string aadAccessToken, string teamName, string teamDescription)
        {
            try
            {
                Logger.Information("Creating new team with name: {TeamName}", teamName);

                var client = new RestClient("https://graph.microsoft.com/v1.0");
                var request = new RestRequest("teams", Method.Post);
                request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                request.AddHeader("Content-Type", "application/json");

                var teamCreationRequest = new Models.MsTeams.TeamCreationRequest
                {
                    Template = "https://graph.microsoft.com/v1.0/teamsTemplates('standard')",
                    DisplayName = teamName,
                    Description = teamDescription,
                    CreatedDateTime = DateTime.UtcNow.AddMonths(-6).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };

                var requestJson = JsonConvert.SerializeObject(teamCreationRequest);
                request.AddJsonBody(requestJson);

                Logger.Debug("Sending team creation request with payload: {RequestJson}", requestJson);
                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful)
                {
                    Logger.Information("Team creation request accepted");
                    
                    // Extract team ID from the Location header
                    var locationHeader = response.Headers?.FirstOrDefault(h => h.Name == "Location")?.Value?.ToString();
                    var teamId = ExtractTeamIdFromLocation(locationHeader);

                    if (!string.IsNullOrEmpty(teamId))
                    {
                        Logger.Information("Team created successfully with ID: {TeamId}", teamId);
                        return new Models.MsTeams.Team { Id = teamId, DisplayName = teamName, Description = teamDescription };
                    }
                    else
                    {
                        Logger.Error("Failed to extract team ID from response headers");
                        return null;
                    }
                }
                else
                {
                    Logger.Error("Failed to create team. Status: {StatusCode}, Response: {Response}", 
                        response.StatusCode, response.Content);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "An error occurred while creating the team");
                return null;
            }
        }

        private static string ExtractTeamIdFromLocation(string locationValue)
        {
            try
            {
                if (string.IsNullOrEmpty(locationValue))
                {
                    Logger.Warning("Location header is null or empty");
                    return null;
                }

                var startIndex = locationValue.IndexOf("teams('") + "teams('".Length;
                var endIndex = locationValue.IndexOf("')", startIndex);

                if (startIndex != -1 && endIndex != -1)
                {
                    var teamId = locationValue.Substring(startIndex, endIndex - startIndex);
                    Logger.Debug("Successfully extracted team ID: {TeamId}", teamId);
                    return teamId;
                }
                
                Logger.Warning("Could not find team ID in location header: {LocationHeader}", locationValue);
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error extracting team ID from location header: {LocationHeader}", locationValue);
                return null;
            }
        }

        public static async Task<bool> CompleteTeamMigration(string aadAccessToken, string teamId)
        {
            try
            {
                Logger.Information("Starting team migration completion process for team {TeamId}", teamId);

                var client = new RestClient("https://graph.microsoft.com/v1.0");

                // Step 1: Get the list of channels
                var getChannelsRequest = new RestRequest($"teams/{teamId}/channels", Method.Get);
                getChannelsRequest.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                getChannelsRequest.AddHeader("Content-Type", "application/json");

                Logger.Debug("Retrieving channels for team {TeamId}", teamId);
                var getChannelsResponse = await client.ExecuteAsync(getChannelsRequest);

                if (!getChannelsResponse.IsSuccessful)
                {
                    Logger.Error("Failed to retrieve channels. Status: {StatusCode}, Response: {Response}",
                        getChannelsResponse.StatusCode, getChannelsResponse.Content);
                    return false;
                }

                // Parse the channels response
                var responseContent = getChannelsResponse.Content;
                var channelsList = JObject.Parse(responseContent)["value"].ToObject<List<Models.MsTeams.Channel>>();

                // Step 2: Loop through channels and complete their migration
                foreach (var channel in channelsList)
                {
                    Logger.Information("Completing migration for channel {ChannelId}", channel.Id);
                    bool channelMigrationSuccessful = await Channels.CompleteChannelMigration(aadAccessToken, teamId, channel.Id);

                    if (!channelMigrationSuccessful)
                    {
                        Logger.Error("Failed to complete migration for Channel ID: {ChannelId}. Aborting team migration.", channel.Id);
                        return false;
                    }
                }

                // Step 3: Complete the team migration
                var completeTeamRequest = new RestRequest($"teams/{teamId}/completeMigration", Method.Post);
                completeTeamRequest.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                completeTeamRequest.AddHeader("Content-Type", "application/json");

                Logger.Debug("Sending complete migration request for team {TeamId}", teamId);
                var completeTeamResponse = await client.ExecuteAsync(completeTeamRequest);

                if (completeTeamResponse.IsSuccessful)
                {
                    Logger.Information("Team migration completed successfully for team {TeamId}", teamId);
                    return true;
                }
                else
                {
                    Logger.Error("Failed to complete team migration. Status: {StatusCode}, Response: {Response}",
                        completeTeamResponse.StatusCode, completeTeamResponse.Content);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "An error occurred while completing team migration for team {TeamId}", teamId);
                return false;
            }
        }

        public static async Task<bool> AddOwnerToTeam(string aadAccessToken, string teamId, string userId)
        {
            try
            {
                Logger.Information("Adding owner {UserId} to team {TeamId}", userId, teamId);

                var client = new RestClient("https://graph.microsoft.com");
                var ownerPayload = new Dictionary<string, string>
                {
                    { "@odata.id", $"https://graph.microsoft.com/v1.0/users/{userId}" }
                };

                var requestJson = JsonConvert.SerializeObject(ownerPayload);
                var request = new RestRequest($"v1.0/groups/{teamId}/owners/$ref", Method.Post);
                request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                request.AddHeader("Content-Type", "application/json");
                request.AddJsonBody(requestJson);

                Logger.Debug("Sending add owner request with payload: {RequestJson}", requestJson);
                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful)
                {
                    Logger.Information("Successfully added user {UserId} as owner to team {TeamId}", userId, teamId);
                    return true;
                }
                else
                {
                    Logger.Error("Failed to add owner to team. Status: {StatusCode}, Response: {Response}",
                        response.StatusCode, response.Content);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "An error occurred while adding owner {UserId} to team {TeamId}", userId, teamId);
                return false;
            }
        }
    }
}
