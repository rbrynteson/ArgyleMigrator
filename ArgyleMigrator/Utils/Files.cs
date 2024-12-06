using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;

namespace ArgyleMigrator.Utils
{
    public class Files
    {
        // Function to download file from URL
        public static string DownloadFileFromUrl(string fileUrl, string originalName)
        {
            // Split the URL into the base part and the remainder
            var uri = new Uri(fileUrl);
            var client = new RestClient($"{uri.Scheme}://{uri.Host}");

            var request = new RestRequest(uri.PathAndQuery, Method.Get);
            string tempFilePath = Path.Combine(Path.GetTempPath(), originalName);

            using (var response = client.DownloadStream(request))
            {
                using (var fileStream = File.Create(tempFilePath))
                {
                    response.CopyTo(fileStream);
                }
            }

            return tempFilePath;
        }

        public static async Task<string> GetChannelFolderId(string aadAccessToken, string groupId, string channelName)
        {
            var client = new RestClient("https://graph.microsoft.com");
            var request = new RestRequest($"v1.0/groups/{groupId}/drive/root/children", Method.Get);
            request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
            request.AddHeader("Accept", "application/json");

            var response = await client.ExecuteAsync(request);
            if (response.IsSuccessful)
            {
                dynamic jsonResponse = JsonConvert.DeserializeObject(response.Content);
                foreach (var item in jsonResponse.value)
                {
                    if (item.name == channelName)
                    {
                        return item.id; // This is the drive item ID (parent-id) for the channel folder
                    }
                }
            }
            else
            {
                Console.WriteLine($"Error getting channel folder ID: {response.StatusCode} - {response.StatusDescription}");
            }
            return null;
        }

        public static async Task<string> GetRootDriveId(string aadAccessToken, string groupId)
        {
            var client = new RestClient("https://graph.microsoft.com");
            var request = new RestRequest($"v1.0/groups/{groupId}/drive/root", Method.Get);
            request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
            request.AddHeader("Accept", "application/json");

            var response = await client.ExecuteAsync(request);
            if (response.IsSuccessful)
            {
                dynamic jsonResponse = JsonConvert.DeserializeObject(response.Content);
                return jsonResponse.id; // This is the root drive item ID (parent-id)
            }
            else
            {
                Console.WriteLine($"Error getting root drive ID: {response.StatusCode} - {response.StatusDescription}");
                return null;
            }
        }


        // Function to upload file to Teams Channel
        public static async Task<bool> UploadFileToChannelFolder(string aadAccessToken, string groupId, string parentId, string tempFilePath, string originalName)
        {
            try
            {
                // Construct the API endpoint for uploading the file
                var client = new RestClient("https://graph.microsoft.com");
                var request = new RestRequest($"v1.0/groups/{groupId}/drive/items/{parentId}:/{originalName}:/content", Method.Put);
                request.AddHeader("Authorization", $"Bearer {aadAccessToken}");
                request.AddHeader("Content-Type", "application/octet-stream");
                request.AddFile("file", tempFilePath);

                var response = await client.ExecuteAsync(request);

                if (response.IsSuccessful)
                {
                    Console.WriteLine("File successfully uploaded to the channel folder.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"File upload failed: {response.StatusCode} - {response.StatusDescription}");
                    Console.WriteLine("Response Details: " + response.Content);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                return false;
            }
        }


        public static string DecompressSlackArchiveFile(string zipFilePath, string tempPath)
        {

            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
                Console.WriteLine("Deleting pre-existing temp directory");
            }

            Directory.CreateDirectory(tempPath);
            Console.WriteLine("Creating temp directory for Slack archive decompression");
            Console.WriteLine("Temp path is " + tempPath);
            ZipFile.ExtractToDirectory(zipFilePath, tempPath);
            Console.WriteLine("Slack archive decompression done");

            return tempPath;
        }

        public static void CleanUpTempDirectoriesAndFiles(string tempPath)
        {
            Console.WriteLine("\n");
            Console.WriteLine("Cleaning up Slack archive temp directories and files");
            Directory.Delete(tempPath, true);
            File.Delete(tempPath);
            Console.WriteLine("Deleted " + tempPath + " and subdirectories");
        }
    }
}
