using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace acsRankingPlugin
{

    class Program
    {
        public class Options
        {
            [Option("name", HelpText = "Instance Name (default is acsRankingPlugin)")]
            public string Name { get; set; } = "acsRankingPlugin";

            [Option("server-port", Required = true, HelpText = "Server's local UDP port.")]
            public int ServerPort { get; set; }

            [Option("plugin-port", Required = true, HelpText = "This plugin's listening UDP port.")]
            public int PluginPort { get; set; }

            [Option("reset", HelpText = "Reset data.")]
            public bool Reset { get; set; }
        }

        static Options options;
        static Leaderboard leaderboard;

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(o =>
                {
                    if (o.ServerPort < IPEndPoint.MinPort || o.ServerPort > IPEndPoint.MaxPort)
                    {
                        Console.WriteLine($"Invalid ServerPort: {o.ServerPort}");
                        Environment.Exit(1);
                    }
                    if (o.PluginPort < IPEndPoint.MinPort || o.PluginPort > IPEndPoint.MaxPort)
                    {
                        Console.WriteLine($"Invalid PluginPort: {o.PluginPort}");
                        Environment.Exit(1);
                    }
                    options = o;
                })
                .WithNotParsed<Options>(errors =>
                {
                    Environment.Exit(1);
                });

            var name = $"{options.Name}-{options.PluginPort}-{options.ServerPort}";
            var storagePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\acsRankingPlugin";
            leaderboard = new Leaderboard(name, storagePath, options.Reset);

            var acsClient = new ACSClient(options.PluginPort, options.ServerPort);

            acsClient.OnError += (packetId, message) => Console.WriteLine(message);
            acsClient.OnChat += (byte packetId, ref ChatEvent eventData) =>
            {
                if (eventData.Message == "?help")
                {
                    var exeVersion = Assembly.GetExecutingAssembly().GetName().Version;
                    var sb = new StringBuilder();
                    sb.AppendLine($"acsRankingPlugin {exeVersion} 명령어");
                    sb.AppendLine("=================================");
                    sb.AppendLine("?help : 도움말 표시");
                    sb.AppendLine("?rank : 리더보드 표시");
                    sb.AppendLine("?ballast [숫자] : 자기 차 무게 증가 (kg)");
                    sb.AppendLine("=================================");

                    acsClient.SendChat(eventData.CarId, sb.ToString());
                }
                else if (eventData.Message == "?rank")
                {
                    acsClient.SendChat(eventData.CarId, leaderboard.ToString());
                }
                else if (eventData.Message.StartsWith("?ballast "))
                {
                    var weightStr = eventData.Message.Substring(9).Trim();
                    int weight;
                    if (Int32.TryParse(weightStr, out weight) && weight >= 0 && weight <= 150)
                    {
                        acsClient.SendAdminMessage($"/ballast {eventData.CarId} {weight}");
                        acsClient.SendChat(eventData.CarId, $"?ballast 명령어를 실행했습니다.");
                    }
                    else
                    {
                        acsClient.SendChat(eventData.CarId, $"무게를 잘못 입력했습니다: {weightStr}");
                    }
                }
            };
            acsClient.OnClientLoaded += (packetId, carId) =>
            {
                Console.WriteLine($"CLIENT LOADED: {carId}");
                acsClient.SendChat(carId, "순위 보여주는 기능 테스트 중입니다.");
                acsClient.SendChat(carId, "도움말은 ?help");
                acsClient.SendChat(carId, leaderboard.ToString());
            };
            acsClient.OnVersion += (packetId, protocolVersion) => Console.WriteLine("PROTOCOL VERSION IS:" + (int)protocolVersion);
            acsClient.OnNewSession += (byte packetId, ref SessionInfoEvent eventData) =>
            {
                Console.WriteLine("New session started");

                acsClient.BroadcastChat(leaderboard.ToString());
                acsClient.OnSessionInfo?.Invoke(packetId, ref eventData);
            };
            acsClient.OnSessionInfo += (byte packetId, ref SessionInfoEvent eventData) =>
            {
                Console.WriteLine($"PROTOCOL: {eventData.Version}, SESSION {eventData.Name} {eventData.SessionIndex + 1}/{eventData.SessionCount}, TRACK: {eventData.Track}");

                leaderboard.Track = eventData.Track;
            };
            acsClient.OnEndSession += (packetId, reportFile) => Console.WriteLine($"ACSP_END_SESSION. REPORT JSON AVAILABLE AT: {reportFile}");
            acsClient.OnClientEvent += (byte packetId, ref ClientEventEvent eventData) =>
            {
                switch (eventData.EventType)
                {
                    case ACSProtocol.ACSP_CE_COLLISION_WITH_ENV:
                        Console.WriteLine($"COLLISION WITH ENV, CAR:{eventData.CarId} IMPACT SPEED:{eventData.Speed} WORLD_POS:{eventData.WorldPosition} REL_POS:{eventData.RelationalPosition}");
                        break;
                    case ACSProtocol.ACSP_CE_COLLISION_WITH_CAR:
                        Console.WriteLine($"COLLISION WITH CAR, CAR:{eventData.CarId} OTHER CAR:{eventData.OtherCarId} IMPACT SPEED:{eventData.Speed} WORLD_POS:{eventData.WorldPosition} REL_POS:{eventData.RelationalPosition}");
                        break;
                }
            };
            acsClient.OnCarInfo += (byte packetId, ref CarInfoEvent eventData) =>
            {
                Console.WriteLine($"CarInfo CAR:{eventData.CarId} {eventData.Model} [{eventData.Skin}] DRIVER:{eventData.DriverName} TEAM:{eventData.DriverTeam} GUID:{eventData.DriverGuid} CONNECTED:{eventData.IsConnected}");

                var newRecord = leaderboard.RegisterCar(eventData.CarId, eventData.Model, eventData.DriverName);
                if (newRecord != null)
                {
                    acsClient.BroadcastChat($"({eventData.CarId}) {newRecord.DriverName} 님이 새로운 {newRecord.Rank}위 기록({newRecord.Laptime.LaptimeFormat()})을 달성했습니다.");
                }
            };
            acsClient.OnCarUpdate += (byte packetId, ref CarUpdateEvent eventData) =>
                Console.Write($"CarUpdate CAR:{eventData.CarId} POS:{eventData.Position} VEL:{eventData.Velocity} GEAR:{eventData.Gear} RPM:{eventData.Rpm} NSP:{eventData.NormalizedSplinePosition}");
            acsClient.OnNewConnection += (byte packetId, ref ConnectionEvent eventData) =>
            {
                Console.WriteLine($"ACSP_NEW_CONNECTION {eventData.DriverName} ({eventData.DriverGuid}), {eventData.CarModel} [{eventData.CarSkin}] ({eventData.CarId})");

                leaderboard.RegisterCar(eventData.CarId, eventData.CarModel, eventData.DriverName);
            };
            acsClient.OnConnectionClosed += (byte packetId, ref ConnectionEvent eventData) =>
            {
                Console.WriteLine($"ACSP_CONNECTION_CLOSED {eventData.DriverName} ({eventData.DriverGuid}), {eventData.CarModel} [{eventData.CarSkin}] ({eventData.CarId})");

                leaderboard.UnregisterCar(eventData.CarId);
            };
            acsClient.OnLapCompleted += (byte packetId, ref LapCompletedEvent eventData) =>
            {
                Console.WriteLine($"ACSP_LAP_COMPLETED CAR:{eventData.CarId} LAP:{eventData.LapTime} CUTS:{eventData.Cuts}");

                if (eventData.Cuts <= 0)
                {
                    NewRecord newRecord;
                    if (!leaderboard.RegisterLaptime(eventData.CarId, eventData.LapTime, out newRecord))
                    {
                        acsClient.GetCarInfo(eventData.CarId);
                    }

                    if (newRecord != null)
                    {
                        acsClient.BroadcastChat($"{newRecord.DriverName} 님이 새로운 {newRecord.Rank}위 기록({newRecord.Laptime.LaptimeFormat()})을 달성했습니다.");
                    }
                }
            };

            acsClient.GetSessionInfo();
            acsClient.DispatchMessages();
        }
    }
}
