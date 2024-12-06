using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Net.Http;
using ArgyleMigrator.Models;
using System.Net.Http.Headers;

namespace ArgyleMigrator.Utils
{
    public class FileAttachments
    {

        public static async Task ArchiveMessageFileAttachments(String aadAccessToken, String selectedTeamId, List<Combined.AttachmentsMapping> combinedAttachmentsMapping, string channelSubFolder, int maxDls = 10)
        {
            var tasks = new List<Task>();

            // semaphore, allow to run maxDLs (default 10) tasks in parallel
            SemaphoreSlim semaphore = new SemaphoreSlim(maxDls);

            foreach (var v in combinedAttachmentsMapping)
            {
                // await here until there is a room for this task
                await semaphore.WaitAsync(); 
                //tasks.Add(GetAndUploadFileToTeamsChannel(aadAccessToken, selectedTeamId, semaphore, v, channelSubFolder));                
            }

            // await for the rest of tasks to complete
            await Task.WhenAll(tasks);
        }

        public static async Task<Tuple<string, string>> UploadFileToTeamsChannel(string aadAccessToken, string selectedTeamId, string filePath, string pathToItem)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                // Set up HttpClient with the access token
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aadAccessToken);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Step 1: Check if File Already Exists in Teams Channel
                var existingFileResponse = await httpClient.GetAsync($"https://graph.microsoft.com/v1.0/teams/{selectedTeamId}/drive/root:/{pathToItem}");
                if (existingFileResponse.IsSuccessStatusCode)
                {
                    var existingFileContent = await existingFileResponse.Content.ReadAsStringAsync();
                    var existingFile = JsonConvert.DeserializeObject<OneDrive.DriveItemResponse>(existingFileContent);

                    // If file exists, return the existing file ID and URL
                    return new Tuple<string, string>(existingFile.Id, existingFile.WebUrl);
                }

                // Step 2: Create Upload Session for the File
                var uploadSessionRequestBody = new JObject
                {
                    ["item"] = new JObject
                    {
                        ["@microsoft.graph.conflictBehavior"] = "replace",
                        ["name"] = Path.GetFileName(filePath)
                    }
                };

                var uploadSessionJson = uploadSessionRequestBody.ToString();
                var createUploadSessionResponse = await httpClient.PostAsync(
                    $"https://graph.microsoft.com/v1.0/teams/{selectedTeamId}/drive/root:/{pathToItem}:/createUploadSession",
                    new StringContent(uploadSessionJson, System.Text.Encoding.UTF8, "application/json")
                );

                if (!createUploadSessionResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("ERROR: Unable to create upload session.");
                    Console.WriteLine("REASON: " + await createUploadSessionResponse.Content.ReadAsStringAsync());
                    return new Tuple<string, string>("", "");
                }

                var uploadSessionContent = await createUploadSessionResponse.Content.ReadAsStringAsync();
                var uploadSession = JsonConvert.DeserializeObject<OneDrive.UploadSessionResponse>(uploadSessionContent);

                // Step 3: Upload File in Chunks
                try
                {
                    Console.WriteLine("Trying to upload file to MS Teams SPo Folder: " + filePath);
                    using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        const int maxChunkSize = 320 * 1024; // 320 KB per chunk
                        long fileSize = fs.Length;
                        long totalBytesRead = 0;
                        byte[] buffer = new byte[maxChunkSize];

                        while (totalBytesRead < fileSize)
                        {
                            // Determine size of the current chunk
                            int bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length);
                            var chunk = new byte[bytesRead];
                            Array.Copy(buffer, 0, chunk, 0, bytesRead);

                            // Create content range header for the current chunk
                            var startRange = totalBytesRead;
                            var endRange = totalBytesRead + bytesRead - 1;
                            var contentRange = $"bytes {startRange}-{endRange}/{fileSize}";

                            // Create a PUT request for the current chunk
                            var chunkRequest = new HttpRequestMessage(HttpMethod.Put, uploadSession.UploadUrl)
                            {
                                Content = new ByteArrayContent(chunk)
                            };
                            chunkRequest.Content.Headers.ContentRange = new ContentRangeHeaderValue(startRange, endRange, fileSize);

                            // Send the chunk
                            var chunkResponse = await httpClient.SendAsync(chunkRequest);
                            if (!chunkResponse.IsSuccessStatusCode)
                            {
                                Console.WriteLine("ERROR: Uploading chunk failed.");
                                Console.WriteLine("REASON: " + await chunkResponse.Content.ReadAsStringAsync());
                                return new Tuple<string, string>("", "");
                            }

                            // Update total bytes read
                            totalBytesRead += bytesRead;
                        }

                        Console.WriteLine("Upload of attachment to MS Teams completed: " + pathToItem);

                        // Parse the final response and return file ID and URL
                        var uploadedItem = JsonConvert.DeserializeObject<OneDrive.DriveItemResponse>(uploadSessionContent);
                        return new Tuple<string, string>(uploadedItem.Id, uploadedItem.WebUrl);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: attachment could not be uploaded: " + ex.Message);
                }
            }

            return new Tuple<string, string>("", "");
        }
    }
}
