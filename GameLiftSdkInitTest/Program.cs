using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using Aws.GameLift.Server;
using Aws.GameLift.Server.Model;
using Amazon;
using Amazon.Runtime;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;


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
        StoredProfileAWSCredentials aws_credentials; // looks like this needs to be updated to a new type
        RegionEndpoint aws_region;
        string environment = "dev"; // programatically find this value
        string instance_identifier = "workstation"; // programatically set this to something meaningful to identify which server is generating the telemetry
        string cw_log_group_name;
        string cw_log_stream_name;
        string game_name = "gameoftheyear";
        string cw_logstream_sequence_token = "token"; // initial token must be longer than 0 chars


        public AwsGameLogic()
        {
            // Making use of profile credentials for workstation here https://docs.aws.amazon.com/sdk-for-net/v2/developer-guide/net-dg-config-creds.html
            aws_credentials = new StoredProfileAWSCredentials("lab");

            rand = new Random();
            Thread t = new Thread(new ThreadStart(PublishPlayerCountLoop));
            t.Start();
            aws_region = RegionEndpoint.USWest2;
            cw_log_group_name = $"/{environment}/{game_name}";
            Process current_process = Process.GetCurrentProcess();
            cw_log_stream_name = $"{instance_identifier}-{current_process.Id.ToString()}";
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

        void CwLog(string group_name, string stream_name, string body)
        {
            using (var cwl = new AmazonCloudWatchLogsClient(aws_credentials, aws_region))
            {
                int attempts = 0;
                do
                {
                    try
                    {
                        attempts++;
                        var request = new PutLogEventsRequest(group_name, stream_name, new List<InputLogEvent>{new InputLogEvent
                            {
                                Timestamp = DateTime.Now,
                                Message = body
                            }
                        });
                        request.SequenceToken = cw_logstream_sequence_token;

                        var ret = cwl.PutLogEvents(request);
                        // Update sequence token for next put
                        cw_logstream_sequence_token = ret.NextSequenceToken;
                        break; // success
                    }
                    catch (Amazon.CloudWatchLogs.Model.ResourceNotFoundException e)
                    {

                        Console.WriteLine($"type: {e.ErrorType} code: {e.ErrorCode} source: {e.Source} ");
                        Console.WriteLine(e.Data);

                        CreateCwLogGroup(cw_log_group_name);
                        CreateCwLogSream(cw_log_stream_name);

                        // Log group doesn't exist. Let's create. This only needs to be done once
                        Task.Delay(1000);
                    }
                    catch (Amazon.CloudWatchLogs.Model.InvalidSequenceTokenException e)
                    {
                        // Each log event must contain a sequence value, unless it's the first event
                        // https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/CloudWatchLogs/TPutLogEventsRequest.html
                        cw_logstream_sequence_token = e.ExpectedSequenceToken;
                        // Now that we have the sequence token, try one more time.
                        Console.WriteLine("Exception caught for unexpected squence token. It's now updated and will retry.");
                        Task.Delay(1000);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                        Console.WriteLine("This exception is not handled, will not retry :-( ");
                        break;
                    }
                    if(attempts > 2)
                    {
                        Console.WriteLine("Unable to send log event retries exhausted.");
                        break;
                    }
                } while (true);
                
            }
        }

        bool CreateCwLogGroup(string group_name)
        {
            using (var cwl = new AmazonCloudWatchLogsClient(aws_credentials, aws_region))
            {
                try
                {
                    Console.WriteLine($"Creating log group group_name.");
                    cwl.CreateLogGroup(new CreateLogGroupRequest(cw_log_group_name));
                    return true;
                }

                catch
                {
                    return false;
                }
            }
        }

        bool CreateCwLogSream(string stream_name)
        {
            using (var cwl = new AmazonCloudWatchLogsClient(aws_credentials, aws_region))
            {
                try
                {
                    Console.WriteLine($"Creating log stream {stream_name}.");
                    cwl.CreateLogStream(new CreateLogStreamRequest(cw_log_group_name, cw_log_stream_name));
                    return true;
                }

                catch
                {
                    return false;
                }
            }
        }


        // CloudWatch Metrics refernce guide
        // https://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/cloudwatch-getting-metrics-examples.html
        void PublishPlayerCountLoop()
        {
            int random_ccu = 0;
            while (true)
            {
                random_ccu = rand.Next(8);
                Console.WriteLine($"Publishing CCU count to CloudWatch Metrics within namespace '{game_name}' setting value to {random_ccu}");

                // Dimensions are optional
                // When crafting alarms that match this metric the dimensions need to match as well
                // In the case where you're writing something unique that changes programatically within the dimension and wish to set a threshold that
                // will always trace your workload you may choose to publish the metric in two ways, one without Dimension and at the same time with.
                var dimension = new Dimension
                {
                    Name = "fleet_id",
                    Value = instance_identifier
                };

                
                using (var cw = new AmazonCloudWatchClient(aws_credentials, aws_region))
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
                        Namespace = game_name
                    });
                }

                // Using this loop test the cloudwatch logging method
                CwLog(cw_log_group_name, cw_log_stream_name, $"Meaningful event happened. We now have {random_ccu} active users!");


                Thread.Sleep(60 * 1000);
            }
        }
    }
}
