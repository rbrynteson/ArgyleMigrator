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
            try
            {
                Logger.Information("Starting to archive file attachments for team {TeamId}", selectedTeamId);
                var tasks = new List<Task>();

                // semaphore, allow to run maxDLs (default 10) tasks in parallel
                SemaphoreSlim semaphore = new SemaphoreSlim(maxDls);

                foreach (var attachment in combinedAttachmentsMapping)
                {
                    // await here until there is a room for this task
                    await semaphore.WaitAsync();
                    Logger.Debug("Processing attachment: {AttachmentId}", attachment.attachmentId);
                    //tasks.Add(GetAndUploadFileToTeamsChannel(aadAccessToken, selectedTeamId, semaphore, v, channelSubFolder));                
                }

                // await for the rest of tasks to complete
                await Task.WhenAll(tasks);
                Logger.Information("Completed archiving file attachments");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error archiving file attachments");
                throw;
            }
        }

        public static async Task<Tuple<string, string>> UploadFileToTeamsChannel(string aadAccessToken, string selectedTeamId, string filePath, string pathToItem)
        {
            try
            {
                Logger.Information("Uploading file {FileName} to team {TeamId}", Path.GetFileName(filePath), selectedTeamId);

                using (HttpClient httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aadAccessToken);
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    // Step 1: Check if File Already Exists in Teams Channel
                    Logger.Debug("Checking if file already exists: {PathToItem}", pathToItem);
                    var existingFileResponse = await httpClient.GetAsync($"https://graph.microsoft.com/v1.0/teams/{selectedTeamId}/drive/root:/{pathToItem}");
                    if (existingFileResponse.IsSuccessStatusCode)
                    {
                        var existingFileContent = await existingFileResponse.Content.ReadAsStringAsync();
                        var existingFile = JsonConvert.DeserializeObject<OneDrive.DriveItemResponse>(existingFileContent);
                        Logger.Information("File already exists, returning existing file information");
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
                    Logger.Debug("Creating upload session for file");
                    var createUploadSessionResponse = await httpClient.PostAsync(
                        $"https://graph.microsoft.com/v1.0/teams/{selectedTeamId}/drive/root:/{pathToItem}:/createUploadSession",
                        new StringContent(uploadSessionJson, System.Text.Encoding.UTF8, "application/json")
                    );

                    if (!createUploadSessionResponse.IsSuccessStatusCode)
                    {
                        Logger.Error("Failed to create upload session. Status: {StatusCode}, Response: {Response}",
                            createUploadSessionResponse.StatusCode, await createUploadSessionResponse.Content.ReadAsStringAsync());
                        return new Tuple<string, string>("", "");
                    }

                    var uploadSessionContent = await createUploadSessionResponse.Content.ReadAsStringAsync();
                    var uploadSession = JsonConvert.DeserializeObject<OneDrive.UploadSessionResponse>(uploadSessionContent);

                    // Step 3: Upload File in Chunks
                    Logger.Information("Starting chunked upload for file: {FilePath}", filePath);
                    using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        const int maxChunkSize = 320 * 1024; // 320 KB per chunk
                        long fileSize = fs.Length;
                        long totalBytesRead = 0;
                        byte[] buffer = new byte[maxChunkSize];

                        while (totalBytesRead < fileSize)
                        {
                            int bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length);
                            var chunk = new byte[bytesRead];
                            Array.Copy(buffer, 0, chunk, 0, bytesRead);

                            var startRange = totalBytesRead;
                            var endRange = totalBytesRead + bytesRead - 1;
                            var contentRange = $"bytes {startRange}-{endRange}/{fileSize}";

                            Logger.Debug("Uploading chunk: {ContentRange}", contentRange);
                            var chunkRequest = new HttpRequestMessage(HttpMethod.Put, uploadSession.UploadUrl)
                            {
                                Content = new ByteArrayContent(chunk)
                            };
                            chunkRequest.Content.Headers.ContentRange = new ContentRangeHeaderValue(startRange, endRange, fileSize);

                            var chunkResponse = await httpClient.SendAsync(chunkRequest);
                            if (!chunkResponse.IsSuccessStatusCode)
                            {
                                Logger.Error("Failed to upload chunk. Status: {StatusCode}, Response: {Response}",
                                    chunkResponse.StatusCode, await chunkResponse.Content.ReadAsStringAsync());
                                return new Tuple<string, string>("", "");
                            }

                            totalBytesRead += bytesRead;
                            Logger.Debug("Progress: {Progress}%", (totalBytesRead * 100 / fileSize));
                        }

                        Logger.Information("Successfully uploaded file to Teams: {PathToItem}", pathToItem);

                        var uploadedItem = JsonConvert.DeserializeObject<OneDrive.DriveItemResponse>(uploadSessionContent);
                        return new Tuple<string, string>(uploadedItem.Id, uploadedItem.WebUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error uploading file {FileName} to team {TeamId}", Path.GetFileName(filePath), selectedTeamId);
                return new Tuple<string, string>("", "");
            }
        }
    }
}
