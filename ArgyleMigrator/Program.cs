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

namespace ArgyleMigrator
{

    class Program
    {
        public static IConfigurationRoot Configuration { get; set; }
        static AuthenticationResult authenticationResult = null;

        static async Task Main(string[] args)
        {
            string slackArchiveBasePath = "";
            string slackArchiveTempPath = "";
            string channelsPath = "";
            bool copyFileAttachments = false;

            if (args.Length == 0)
            {
                Console.WriteLine("Please provide arguments to run this application.");
                Console.WriteLine("Usage:");
                Console.WriteLine("Phase 1: ArgyleMigrator.exe <path_to_slack_archive_zip>");
                Console.WriteLine("Phase 2: ArgyleMigrator.exe <path_to_slack_archive_zip> <path_to_users_json> <TeamMigrationName>");
                Environment.Exit(1);
            }

            if (args.Length == 1)
            {
                // Phase 1: Extract user information
                var slackArchivePath = args[0];
                if (!File.Exists(slackArchivePath))
                {
                    Console.WriteLine($"The file '{slackArchivePath}' does not exist.");
                    Environment.Exit(1);
                }

                slackArchiveTempPath = Path.GetTempFileName();
                slackArchiveBasePath = Utils.Files.DecompressSlackArchiveFile(slackArchivePath, slackArchiveTempPath);

                Console.WriteLine("Scanning users in Slack archive");
                var slackUserList = Utils.Users.ScanUsers(Path.Combine(slackArchiveBasePath, "users.json"));
                Console.WriteLine("Scanning users in Slack archive - Done");

                // Write users to a new JSON file for Phase 2 usage
                var usersOutputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "slack_users.json");
                File.WriteAllText(usersOutputFilePath, JsonConvert.SerializeObject(slackUserList, Formatting.Indented));
                Console.WriteLine($"User information has been saved to '{usersOutputFilePath}'");

                // Cleanup temporary directories
                Utils.Files.CleanUpTempDirectoriesAndFiles(slackArchiveTempPath);

                Console.WriteLine("Phase 1 complete. Press any key to exit.");
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
                    Console.WriteLine("The provided file(s) do not exist. Please check the paths and try again.");
                    Environment.Exit(1);
                }

                // Retrieve settings from appsettings.json instead of hard coding them here
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddEnvironmentVariables();
                Configuration = builder.Build();

                Console.WriteLine("");
                Console.WriteLine("****************************************************************************************************");
                Console.WriteLine("*                       Welcome to Argyle Migrator - Phase 2!                                      *");
                Console.WriteLine("*      This tool will now proceed to migrate Slack channels and messages to Microsoft Teams.       *");
                Console.WriteLine("****************************************************************************************************");
                Console.WriteLine("");

                // Authenticate user
                authenticationResult = await UserLogin();
                var aadAccessToken = authenticationResult.AccessToken;

                if (String.IsNullOrEmpty(authenticationResult.AccessToken))
                {
                    Console.WriteLine("Something went wrong. Please try again!");
                    Environment.Exit(1);
                }
                else
                {
                    Console.WriteLine("You've successfully signed in.");
                }

                // Create a new Team for migration
                Console.WriteLine("****************************************************************************************************");
                Console.WriteLine("STEP #1: Create Migration Team:");
                var createdTeam = await Teams.CreateTeamInMsTeams(aadAccessToken, migrationTeamName, "Team used for migration purposes.");
                if (createdTeam != null)
                {
                    Console.WriteLine($"Created Team ID: {createdTeam.Id}");
                }

                // Extract the Slack Archive
                slackArchiveTempPath = Path.GetTempFileName();
                slackArchiveBasePath = Utils.Files.DecompressSlackArchiveFile(slackArchivePath, slackArchiveTempPath);
                channelsPath = Path.Combine(slackArchiveBasePath, "channels.json");

                Console.WriteLine("Scanning channels.json");
                var slackChannelsToMigrate = Utils.Channels.ScanSlackChannelsJson(channelsPath);
                Console.WriteLine("Scanning channels.json - done");

                Console.WriteLine("Creating channels in MS Teams");
                var msTeamsChannelsWithSlackProps = await Utils.Channels.CreateChannelsInMsTeams(aadAccessToken, createdTeam.Id, slackChannelsToMigrate, slackArchiveTempPath);
                Console.WriteLine("Creating channels in MS Teams - done");

                Console.WriteLine("****************************************************************************************************");
                Console.Write("STEP #2: Going to parse the Slack Files Now!");
                Console.Write("Copy files attached to Slack messages to Microsoft Teams? (y|n): ");
                var copyFileAttachmentsResponse = Console.ReadLine();
                if (copyFileAttachmentsResponse.StartsWith("y", StringComparison.CurrentCultureIgnoreCase))
                {
                    copyFileAttachments = true;
                }

                // Read the user JSON file from Phase 1
                Console.WriteLine("Reading users from user JSON file.");
                var slackUserList = JsonConvert.DeserializeObject<List<SimpleUser>>(File.ReadAllText(usersJsonPath));

                Console.WriteLine("Scanning messages in Slack channels");
                await Utils.Messages.ScanMessagesByChannel(msTeamsChannelsWithSlackProps, slackArchiveTempPath, slackUserList, aadAccessToken, createdTeam.Id, copyFileAttachments);
                Console.WriteLine("Scanning messages in Slack channels - Done");

                Console.WriteLine("Convert Teams and Channels to Complete Migration");
                await Utils.Teams.CompleteTeamMigration(aadAccessToken, createdTeam.Id);
                Console.WriteLine("Convert Teams and Channels to Complete Migration - Done");

                Console.WriteLine("Add Owner To Migration Team");
                await Utils.Teams.AddOwnerToTeam(aadAccessToken, createdTeam.Id, Configuration["AzureAd:OwnerId"]);
                Console.WriteLine("Add Owner To Migration Team - Done");

                Utils.Files.CleanUpTempDirectoriesAndFiles(slackArchiveTempPath);

                Console.WriteLine("Tasks complete. Press any key to exit");
                Console.ReadKey();
                Console.WriteLine("****************************************************************************************************");
            }
        }

        static async Task<AuthenticationResult> UserLogin()
        {
            // Create a confidential client application
            string authority = String.Format(CultureInfo.InvariantCulture, Configuration["AzureAd:AadInstance"], Configuration["AzureAd:TenantId"]);
            IConfidentialClientApplication confidentialClientApp = ConfidentialClientApplicationBuilder.Create(Configuration["AzureAd:ClientId"])
                .WithClientSecret(Configuration["AzureAd:ClientSecret"])
                .WithAuthority(new Uri(authority))
                .Build();

            // Acquire a token for the provided scopes
            string[] scopes = new string[] { Configuration["AzureAd:Scope"] };
            AuthenticationResult authResult = await confidentialClientApp.AcquireTokenForClient(scopes).ExecuteAsync();

            return authResult;
        }
    }
}
