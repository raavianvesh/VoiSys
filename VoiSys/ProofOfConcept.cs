using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Speech.V1;
using Google.Cloud.Storage.V1;
using NAudio;
using NAudio.Wave;
using System.Configuration;


namespace VoiSys
{
    class ProofOfConcept
    {
        private static WasapiLoopbackCapture capture;
        private static WaveFileWriter writer;
        static ProofOfConcept()
        {
            capture = new WasapiLoopbackCapture();
            writer = new WaveFileWriter(ConfigurationManager.AppSettings.Get("OutputPath").ToString(), capture.WaveFormat);
        }

        public static void Main(string[] args)
        {
            ProofOfConcept concept = new ProofOfConcept();
            AuthImplicit(ConfigurationManager.AppSettings.Get("GoogleProjectName"), ConfigurationManager.AppSettings.Get("KeyLocation"));
            Console.WriteLine("Listening to Microphone, Please Talk");
            concept.StartMicroPhoneRecording();
            Console.ReadLine();
            concept.CreateAudioFile();
            Console.WriteLine("Created Audio File");
            concept.StopMicroPhoneRecording();
            Console.WriteLine("Stopped Recording");
            StreamingRecognizeAsync(ConfigurationManager.AppSettings.Get("Transcript")).Wait();
        }

        static object AuthImplicit(string projectId, string jsonPath)
        {
            // If you don't specify credentials when constructing the client, the
            // client library will look for credentials in the environment.
            Console.WriteLine("Authenticating..");
            GoogleCredential credential = GoogleCredential.FromFile(jsonPath);
            Console.WriteLine("Storing Credential");
            StorageClient storage = StorageClient.Create(credential);
            // Make an authenticated API request.
            var buckets = storage.ListBuckets(projectId);
            foreach (var bucket in buckets)
            {
                Console.WriteLine("Bucket Name: " + bucket.Name);
            }
            Console.ReadLine();
            return null;
        }

        void StartMicroPhoneRecording()
        {
            Console.Write("Starting Capture");
            capture.StartRecording();
            //while (capture.CaptureState != NAudio.CoreAudioApi.CaptureState.Stopped)
            //{
                //Thread.Sleep(2000);
            //}
        }

        void StopMicroPhoneRecording()
        {
            Console.Write("Stopping Capture");
            capture.StopRecording();
            capture.RecordingStopped += (s, a) =>
            {
                writer.Dispose();
                writer = null;
                capture.Dispose();
            };
        }

        void CreateAudioFile()
        {
            Console.WriteLine("Creating Audio file");
            capture.DataAvailable += (s, a) =>
            {
                writer.Write(a.Buffer, 0, a.BytesRecorded);
            };
        }

        static async Task<object> StreamingRecognizeAsync(string filePath)
        {
            Console.WriteLine("Creating Speech Client");
            var speech = SpeechClient.Create();
            Console.WriteLine("Creating Speech Stream");
            var streamingCall = speech.StreamingRecognize();
            // Write the initial request with the config.
            Console.WriteLine("Streaming Recognition Configuration");
            await streamingCall.WriteAsync(
                new StreamingRecognizeRequest()
                {
                    StreamingConfig = new StreamingRecognitionConfig()
                    {
                        Config = new RecognitionConfig()
                        {
                            Encoding =
                            RecognitionConfig.Types.AudioEncoding.Linear16,
                            SampleRateHertz = 16000,
                            LanguageCode = "en",
                        },
                        InterimResults = true,
                    }
                });
            // Print responses as they arrive.
            Console.WriteLine("Printing responses as they arrive");
            Task printResponses = Task.Run(async () =>
            {
                while (await streamingCall.ResponseStream.MoveNext(
                    default(CancellationToken)))
                {
                    foreach (var result in streamingCall.ResponseStream
                        .Current.Results)
                    {
                        foreach (var alternative in result.Alternatives)
                        {
                            Console.WriteLine("Transcript of your speech:" + alternative.Transcript);
                        }
                    }
                }
            });
            // Stream the file content to the API.  Write 2 32kb chunks per
            // second.
            Console.WriteLine("Creating File at " + filePath);
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
            {
                var buffer = new byte[32 * 1024];
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(
                    buffer, 0, buffer.Length)) > 0)
                {
                    await streamingCall.WriteAsync(
                        new StreamingRecognizeRequest()
                        {
                            AudioContent = Google.Protobuf.ByteString
                            .CopyFrom(buffer, 0, bytesRead),
                        });
                    await Task.Delay(500);
                };
            }
            await streamingCall.WriteCompleteAsync();
            await printResponses;
            Console.ReadLine();
            return 0;
        }
    }
}
