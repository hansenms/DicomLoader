using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Dicom;
using Microsoft.Health.Dicom.Client;
using Polly;

namespace DicomLoader
{

    class Program
    {
        static void Main(
            Uri blobContainerUrl,
            Uri dicomServerUrl,
            string blobPrefix = null,
            int maxDegreeOfParallelism = 8,
            int refreshInterval = 5)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(dicomServerUrl.AbsoluteUri);
            DicomWebClient client = new DicomWebClient(httpClient);
            
            var metrics = new MetricsCollector();

            var randomGenerator = new Random();
            var pollyDelays =
                    new[]
                    {
                            TimeSpan.FromMilliseconds(2000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(3000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(5000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(8000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(12000 + randomGenerator.Next(50)),
                            TimeSpan.FromMilliseconds(16000 + randomGenerator.Next(50)),
                    };

            BlobContainerClient containerClient = new BlobContainerClient(blobContainerUrl, new DefaultAzureCredential());
            var actionBlock = new ActionBlock<string>(async blobName =>
            {

                Thread.Sleep(TimeSpan.FromMilliseconds(randomGenerator.Next(50)));

                var blobClient = containerClient.GetBlobClient(blobName);

                BlobDownloadInfo download = await blobClient.DownloadAsync();
                byte[] fileContents;
                using (var br = new BinaryReader(download.Content))
                {
                    fileContents = br.ReadBytes((int)download.ContentLength);
                }


                DicomFile dicomFile = DicomFile.Open(new MemoryStream(fileContents));

                DicomWebResponse uploadResult = await Policy
                    .HandleResult<DicomWebResponse>(response => !IsSuccessStatusCode(response.StatusCode))
                    .WaitAndRetryAsync(pollyDelays, (result, timeSpan, retryCount, context) =>
                    {
                        if (retryCount > 3)
                        {
                            Console.WriteLine($"Request failed with {result.Result.StatusCode}. Waiting {timeSpan} before next retry. Retry attempt {retryCount}");
                        }
                    })
                    .ExecuteAsync(async () =>
                    {
                        try
                        {
                            return await client.StoreAsync(new[] { dicomFile });
                        }
                        catch (DicomWebException e)
                        {
                            if (e.StatusCode == System.Net.HttpStatusCode.Conflict)
                            {
                                Console.WriteLine($"Conflict: {blobName}, skipping");
                                return new DicomWebResponse(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
                            }
                            else
                            {
                                throw;
                            }
                        }
                    });

                if (!IsSuccessStatusCode(uploadResult.StatusCode))
                {
                    string resultContent = await uploadResult.Content.ReadAsStringAsync();
                    Console.WriteLine(resultContent);
                    throw new Exception($"Unable to upload to server. Error code {uploadResult.StatusCode}");
                }

                metrics.Collect(DateTime.Now);
            },
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism
                }
            );

            // Start output on timer
            var t = new Task( () => {
                while (true)
                {
                    Thread.Sleep(1000 * refreshInterval);
                    Console.WriteLine($"Images per second: {metrics.EventsPerSecond}");
                }
            });
            t.Start();

            string continuationToken = null;
            try
            {
                do
                {
                    var resultSegment = containerClient.GetBlobs(prefix: blobPrefix).AsPages(continuationToken);

                    foreach (Azure.Page<BlobItem> blobPage in resultSegment)
                    {
                        foreach (BlobItem blobItem in blobPage.Values)
                        {
                            actionBlock.Post(blobItem.Name);
                        }

                        continuationToken = blobPage.ContinuationToken;
                    }

                } while (continuationToken != "");

            }
            catch (RequestFailedException e)
            {
                Console.WriteLine(e.Message);
                Console.ReadLine();
                throw;
            }

            actionBlock.Complete();
            actionBlock.Completion.Wait();
        }

        static bool IsSuccessStatusCode(System.Net.HttpStatusCode statusCode)
        {
            return ((int)statusCode >= 200) && ((int)statusCode <= 299);
        }
    }
}
