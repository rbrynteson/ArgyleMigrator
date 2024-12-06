# ArgyleMigrator for Slack to Microsoft Teams

Moving to Microsoft Teams from Slack?  Here is what the ArgyleMigrator does at a high level:

* Reads the contents of your Slack export file
* Creates a new Microsoft Team
* Creates the channels from your Slack export file in the new Microsoft Team
* Creates messages for each channel in the new Microsoft Team based on the Slack export file
* Uploads files (based on the shared Slack urls) to the Channel folder

Note: This is a fork (rewrite) of the ChannelSurf project.  The original project can be found here: https://github.com/tamhinsf/ChannelSurf

## Migration Strategy

To create this project we started loosely with what was in the ChannelSurf project and quickly expanded beyond. This uses the Microsoft Graph API to create a new Team and Channels.  It also uses the Slack export file to create the messages and upload the files.  We based this migration on this article:

https://devblogs.microsoft.com/microsoft365dev/migrate-messages-from-other-chat-platforms-to-microsoft-teams/

In this article it details the migration steps are:

1. Create a new team in migration mode
2. Create channels in the new team, also in migration mode
3. Upload, backdated, messages into the channels
4. Mark the channels as completed

## Using the Application

At this point in time, the application is done is two phases and is done completely via the command line.  There will be additional logging and error handlers added in the future.

### Get Slack Export

The migration is based on the Slack Export file.  This has been tested with exports from the Free and Pro plans.

1. Go to https://my.slack.com/admin/settings
2. Click on the Import/Export Data
3. Choose the export tab
4. Click on the Start Export button based on the time range you wanted.

### Create App Registration in Azure AD

This tool uses the Microsoft Graph API to create the new Team and Channels.  You will need to create an App Registration in Azure AD to get the Client ID and Tenant ID.
          
1. Go to https://portal.azure.com.
2. Click on Entry Id.
3. Under manage, choose App Registrations.
4. Click on New Registration.
5. Fill in the Name and Supported Account Types.  You can choose Accounts in this organization directory only.  Recommended name is ArgyleMigrator.  Click Create.
6. Once created, under Certificates & Secrets.  Click on Client Secrets. Click on New Client Secret.  Fill in the description and choose the expiration.  Click Add.  Make sure to note the client secret.
7. Go to API Permissions. Click on Add a permission.  Choose Microsoft Graph.  Choose Application Permissions.  Choose the following permissions: Channel.Create, Files.Read.Add, Files.ReadWrite.All, Group.ReadWrite.All, Team.Create, Team.ReadBasic.All, Teamwork.Migrate.All, User.Read, User.Read.All.  Click Add permissions.
8. Take note of the following: Application (client) ID, Directory (tenant) ID, and the Client Secret.

### Clone And Use The App

As of right now, you need to use Visual Studio 2022 to build the application and run it.  This will be updated in the near future to allow you to pass in all needed parameters.

### Phase 1

- Clone the repository
- Open the solution in Visual Studio 2022
- Update the appsettings.json file with the following information: ClientId, ClientSecret, TenantId (in domain.onmicrosoft.com format), and OwnerId (this is the O365 App ID of the person who will own the newly created team.).
- Open program.cs and update the Team Name you want to create.  By deafult it is migration001.  (NOTE: This will change soon to a parameter.)
- Copy the zip file from the Slack export to the program folder.  It should be at the same level as program.cs file.  Rename the file to slack.zip.
- Ensure it is set to debug and set the debug properties to slack.zip.
- Run the program.

This will create a file named slack_users.json.  Copy the file to the program folder as it will be used in Phase 2.  Update the file with the matching O365 Object ID.

### Phase 2

- Modify the debug properties and add the path to your slack_users.json file.
- Run the program
- The program will create the team and channels first. It will then ask if you want to upload the files.

- The program will then upload the files into the correct channel folders.
- Once it is complete, the program will convert the channels and teams to standard mode.

This code is provided as is and is not supported by Microsoft.  It is provided as a starting point for your migration.  It is recommended to test this in a non-production environment first.

Distributed under the GNU General Public License 3.0.