using System;
using System.Threading;
using System.Collections.Generic;
using Aws.GameLift.Server;
using Aws.GameLift.Server.Model;
using Amazon;
using Amazon.Runtime;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;


namespace GameLiftSdkInitTest
{
    class Program
    {
        static void Main(string[] args)
        {
            AwsGameLogic AGL = new AwsGameLogic();
            if (!AGL.InitGameLift())
            {
                Console.WriteLine("Did not init GameLift SDK Exiting.");
                return;
            }
            Console.WriteLine("SDK initialized sitting in game loop");
            // Endless game loop
            while (true){}
        }


    }

    class AwsGameLogic
    {
        const int listening_port = 8080;
        Random rand;

        public AwsGameLogic()
        {
            rand = new Random();
            Thread t = new Thread(new ThreadStart(PublishPlayerCountLoop));
            t.Start();
        }


        public bool InitGameLift()
        {
            var initSDKOutcome = GameLiftServerAPI.InitSDK();
            if (initSDKOutcome.Success)
            {
                ProcessParameters m_processParameters = new ProcessParameters(
                    OnActiveGameSessionRequest,
                    OnTerminateProcessRequest,
                    OnHealthCheckRequest,
                    listening_port,
                    new LogParameters(new List<string>()
                            {
                                //Here, the game server tells GameLift what set of files to upload when the game session ends.
                                //GameLift uploads everything specified here for the developers to fetch later.
                                "/local/game/logs/myserver.log"
                            }
                        )
                    );
                var processReadyOutcome = GameLiftServerAPI.ProcessReady(m_processParameters);
                if (processReadyOutcome.Success)
                {
                    Console.WriteLine("ProcessReady success.");
                }
                else
                {
                    Console.WriteLine("ProcessReady failure : " + processReadyOutcome.Error.ToString());
                }
            }
            else
            {
                Console.WriteLine("InitSDK failure : " + initSDKOutcome.Error.ToString());
                return false;
            }

            return true;
        }

        void OnActiveGameSessionRequest(GameSession gameSession)
        {
            Console.WriteLine("OnActiveGameSessionRequest called");
            GameLiftServerAPI.ActivateGameSession();
        }

        void OnTerminateProcessRequest()
        {
            GameLiftServerAPI.ProcessEnding();
        }

        bool OnHealthCheckRequest()
        {
            Console.WriteLine("Received health check request.");
            return true;
        }

        // CloudWatch Metrics refernce guide
        // https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/cloudwatch-getting-metrics-examples.html
        void PublishPlayerCountLoop()
        {
            var credentials = new StoredProfileAWSCredentials("lab");
            string ns = "dev-gameoftheyear";
            int random_ccu = 0;
            while (true)
            {
                random_ccu = rand.Next(8);
                Console.WriteLine($"Publishing CCU count to CloudWatch Metrics within namespace '{ns}' setting value to {random_ccu}");
                var dimension = new Dimension
                {
                    Name = "fleet_id",
                    Value = "fleet-1234"
                };

                
                using (var cw = new AmazonCloudWatchClient(credentials, RegionEndpoint.USWest2))
                {
                    // Docs on metric info
                    // https://docs.aws.amazon.com/AmazonCloudWatch/latest/APIReference/API_MetricDatum.html
                    PutMetricDataResponse ret = cw.PutMetricData(new PutMetricDataRequest
                    {
                        MetricData = new List<MetricDatum>{new MetricDatum
                        {
                            MetricName = "CCU",
                            Dimensions = new List<Dimension>{dimension},
                            Unit = "Count",
                            Value = random_ccu
                        }},
                        Namespace = ns
                    });
                }
                Thread.Sleep(60 * 1000);
            }
        }
    }
}
