using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using System.Reflection;
using static System.Formats.Asn1.AsnWriter;
using Microsoft.Identity.Client;
using System.Threading.Tasks;
using ArgyleMigrator.Utils;
using Newtonsoft.Json;
using ArgyleMigrator.ViewModels;
using System.Linq;

namespace ArgyleMigrator
{
    class Program
    {
        public static IConfigurationRoot Configuration { get; set; }
        static AuthenticationResult authenticationResult = null;

        static async Task Main(string[] args)
        {
            // Initialize logger
            Utils.Logger.Initialize();
            Utils.Logger.Information("Starting Argyle Migrator");

            string slackArchiveBasePath = "";
            string slackArchiveTempPath = "";
            string channelsPath = "";
            bool copyFileAttachments = false;

            try
            {
                if (args.Length == 0)
                {
                    Utils.Logger.Warning("No arguments provided");
                    Utils.Logger.Information("Usage:");
                    Utils.Logger.Information("Phase 1: ArgyleMigrator.exe <path_to_slack_archive_zip>");
                    Utils.Logger.Information("Phase 2: ArgyleMigrator.exe <path_to_slack_archive_zip> <path_to_users_json> <TeamMigrationName>");
                    Environment.Exit(1);
                }

                if (args.Length == 1)
                {
                    // Phase 1: Extract user information
                    var slackArchivePath = args[0];
                    if (!File.Exists(slackArchivePath))
                    {
                        Utils.Logger.Error("The file '{SlackArchivePath}' does not exist.", slackArchivePath);
                        Environment.Exit(1);
                    }

                    slackArchiveTempPath = Path.GetTempFileName();
                    slackArchiveBasePath = Utils.Files.DecompressSlackArchiveFile(slackArchivePath, slackArchiveTempPath);

                    Utils.Logger.Information("Scanning users in Slack archive");
                    var slackUserList = Utils.Users.ScanUsers(Path.Combine(slackArchiveBasePath, "users.json"));
                    Utils.Logger.Information("Found {UserCount} users in Slack archive", slackUserList.Count);

                    // Write users to a new JSON file for Phase 2 usage
                    var usersOutputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "slack_users.json");
                    File.WriteAllText(usersOutputFilePath, JsonConvert.SerializeObject(slackUserList, Formatting.Indented));
                    Utils.Logger.Information("User information has been saved to '{OutputPath}'", usersOutputFilePath);

                    // Cleanup temporary directories
                    Utils.Files.CleanUpTempDirectoriesAndFiles(slackArchiveTempPath);

                    Utils.Logger.Information("Phase 1 complete. Press any key to exit.");
                    Console.ReadKey();
                    Environment.Exit(0);
                }

                if (args.Length == 3)
                {
                    var slackArchivePath = args[0];
                    var usersJsonPath = args[1];
                    var migrationTeamName = args[2];

                    if (!File.Exists(slackArchivePath) || !File.Exists(usersJsonPath))
                    {
                        Utils.Logger.Error("The provided file(s) do not exist. Please check the paths and try again.");
                        Environment.Exit(1);
                    }

                    // Retrieve settings from appsettings.json
                    var builder = new ConfigurationBuilder()
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                        .AddEnvironmentVariables();
                    Configuration = builder.Build();

                    Utils.Logger.Information("Starting Phase 2 of Argyle Migrator");
                    Utils.Logger.Information("This tool will now proceed to migrate Slack channels and messages to Microsoft Teams.");

                    // Authenticate user
                    authenticationResult = await UserLogin();
                    var aadAccessToken = authenticationResult.AccessToken;

                    if (String.IsNullOrEmpty(authenticationResult.AccessToken))
                    {
                        Utils.Logger.Error("Authentication failed. Please try again!");
                        Environment.Exit(1);
                    }
                    else
                    {
                        Utils.Logger.Information("Authentication successful");
                    }

                    // Create a new Team for migration
                    Utils.Logger.Information("STEP #1: Creating Migration Team");
                    var createdTeam = await Teams.CreateTeamInMsTeams(aadAccessToken, migrationTeamName, "Team used for migration purposes.");
                    if (createdTeam != null)
                    {
                        Utils.Logger.Information("Created Team with ID: {TeamId}", createdTeam.Id);
                        Utils.MigrationStateManager.InitializeState(createdTeam.Id, migrationTeamName);
                    }

                    // Extract the Slack Archive
                    slackArchiveTempPath = Path.GetTempFileName();
                    slackArchiveBasePath = Utils.Files.DecompressSlackArchiveFile(slackArchivePath, slackArchiveTempPath);
                    channelsPath = Path.Combine(slackArchiveBasePath, "channels.json");

                    Utils.Logger.Information("Scanning channels.json");
                    var slackChannelsToMigrate = Utils.Channels.ScanSlackChannelsJson(channelsPath);
                    Utils.Logger.Information("Found {ChannelCount} channels to migrate", slackChannelsToMigrate.Count);

                    // Check for existing migration state
                    var existingState = Utils.MigrationStateManager.LoadState();
                    if (existingState != null && existingState.TeamId == createdTeam.Id)
                    {
                        Utils.Logger.Information("Found existing migration state. Resuming migration...");
                        // Filter out already migrated channels
                        slackChannelsToMigrate = slackChannelsToMigrate
                            .Where(c => !existingState.Channels.Any(ec => 
                                ec.SlackChannelId == c.channelId && ec.MigrationCompleted))
                            .ToList();
                        Utils.Logger.Information("Found {ChannelCount} channels remaining to migrate", slackChannelsToMigrate.Count);
                    }

                    if (slackChannelsToMigrate.Count > 0)
                    {
                        // Allow user to select which channels to migrate
                        slackChannelsToMigrate = Utils.Channels.SelectChannelsToMigrate(slackChannelsToMigrate);
                        Utils.Logger.Information("Selected {ChannelCount} channels for migration", slackChannelsToMigrate.Count);

                        Utils.Logger.Information("Creating channels in MS Teams");
                        var msTeamsChannelsWithSlackProps = await Utils.Channels.CreateChannelsInMsTeams(aadAccessToken, createdTeam.Id, slackChannelsToMigrate, slackArchiveTempPath);
                        Utils.Logger.Information("Created {ChannelCount} channels in MS Teams", msTeamsChannelsWithSlackProps.Count);

                        Utils.Logger.Information("STEP #2: Processing Slack Files");
                        Utils.Logger.Information("Copy files attached to Slack messages to Microsoft Teams? (y|n): ");
                        var copyFileAttachmentsResponse = Console.ReadLine();
                        if (copyFileAttachmentsResponse.StartsWith("y", StringComparison.CurrentCultureIgnoreCase))
                        {
                            copyFileAttachments = true;
                        }

                        // Read the user JSON file from Phase 1
                        Utils.Logger.Information("Reading users from user JSON file");
                        var slackUserList = JsonConvert.DeserializeObject<List<SimpleUser>>(File.ReadAllText(usersJsonPath));

                        Utils.Logger.Information("Starting message migration for {ChannelCount} channels", msTeamsChannelsWithSlackProps.Count);
                        await Utils.Messages.ScanMessagesByChannel(msTeamsChannelsWithSlackProps, slackArchiveTempPath, slackUserList, aadAccessToken, createdTeam.Id, copyFileAttachments);
                        Utils.Logger.Information("Message migration completed");

                        Utils.Logger.Information("Converting Teams and Channels to Complete Migration");
                        await Utils.Teams.CompleteTeamMigration(aadAccessToken, createdTeam.Id);
                        Utils.Logger.Information("Migration completion process finished");
                    }
                    else
                    {
                        Utils.Logger.Information("No channels remaining to migrate");
                    }

                    Utils.Logger.Information("Adding Owner To Migration Team");
                    await Utils.Teams.AddOwnerToTeam(aadAccessToken, createdTeam.Id, Configuration["AzureAd:OwnerId"]);
                    Utils.Logger.Information("Owner added to migration team");

                    Utils.Files.CleanUpTempDirectoriesAndFiles(slackArchiveTempPath);
                    Utils.MigrationStateManager.CompleteMigration();

                    Utils.Logger.Information("Migration completed successfully. Press any key to exit");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                Utils.Logger.Error(ex, "An unexpected error occurred during migration");
                throw;
            }
            finally
            {
                Utils.Logger.CloseAndFlush();
            }
        }

        static async Task<AuthenticationResult> UserLogin()
        {
            try
            {
                // Create a confidential client application
                string authority = String.Format(CultureInfo.InvariantCulture, Configuration["AzureAd:AadInstance"], Configuration["AzureAd:TenantId"]);
                IConfidentialClientApplication confidentialClientApp = ConfidentialClientApplicationBuilder.Create(Configuration["AzureAd:ClientId"])
                    .WithClientSecret(Configuration["AzureAd:ClientSecret"])
                    .WithAuthority(new Uri(authority))
                    .Build();

                // Acquire a token for the provided scopes
                string[] scopes = new string[] { Configuration["AzureAd:Scope"] };
                Utils.Logger.Debug("Attempting to acquire token with scope: {Scope}", Configuration["AzureAd:Scope"]);
                AuthenticationResult authResult = await confidentialClientApp.AcquireTokenForClient(scopes).ExecuteAsync();
                return authResult;
            }
            catch (Exception ex)
            {
                Utils.Logger.Error(ex, "Failed to authenticate user");
                throw;
            }
        }
    }
}
