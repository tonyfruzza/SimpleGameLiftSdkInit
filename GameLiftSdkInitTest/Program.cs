using System;
using System.Collections.Generic;
using Aws.GameLift.Server;
using Aws.GameLift.Server.Model;


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
    }
}
