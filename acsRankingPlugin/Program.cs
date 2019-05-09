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

        static void Main(string[] args)
        {
            Options options = null;
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
            var leaderboard = new Leaderboard(name, storagePath, options.Reset);

            var acsClient = new ACSClient(options.PluginPort, options.ServerPort);
            var carInfos = new CarInfos(acsClient);

            acsClient.OnError += (packetId, message) => Console.WriteLine(message);
            acsClient.OnChat += async (byte packetId, ChatEvent eventData) =>
            {
                if (eventData.Message == "?help")
                {
                    var exeVersion = Assembly.GetExecutingAssembly().GetName().Version;
                    var sb = new StringBuilder();
                    sb.AppendLine($"tobot's acsRankingPlugin {exeVersion} 명령어");
                    sb.AppendLine("=================================");
                    sb.AppendLine("?help : 도움말 표시");
                    sb.AppendLine("?rank : 내 순위 근처의 리더보드");
                    sb.AppendLine("?toprank : 상위권 리더보드");
                    sb.AppendLine("?fullrank : 전체 리더보드");
                    sb.AppendLine("?ballast [숫자] : 자기 차 무게 증가 (kg)");
                    sb.AppendLine("=================================");

                    await acsClient.SendChatAsync(eventData.CarId, sb.ToString());
                }
                else if (eventData.Message == "?rank")
                {
                    var car = await carInfos.GetAsync(eventData.CarId);
                    await acsClient.SendChatAsync(eventData.CarId, await leaderboard.GenerateMyRankTableAsync(car.CarName, car.DriverName));
                }
                else if (eventData.Message == "?toprank")
                {
                    await acsClient.SendChatAsync(eventData.CarId, await leaderboard.GenerateTopRankTableAsync());
                }
                else if (eventData.Message == "?fullrank")
                {
                    await acsClient.SendChatAsync(eventData.CarId, await leaderboard.GenerateRankTableAsync());
                }
                else if (eventData.Message.StartsWith("?ballast "))
                {
                    var weightStr = eventData.Message.Substring(9).Trim();
                    int weight;
                    if (Int32.TryParse(weightStr, out weight) && weight >= 0 && weight <= 150)
                    {
                        await acsClient.SendAdminMessageAsync($"/ballast {eventData.CarId} {weight}");
                        await acsClient.SendChatAsync(eventData.CarId, $"?ballast 명령어를 실행했습니다.");
                    }
                    else
                    {
                        await acsClient.SendChatAsync(eventData.CarId, $"무게를 잘못 입력했습니다: {weightStr}");
                    }
                }
            };
            acsClient.OnClientLoaded += async (packetId, carId) =>
            {
                Console.WriteLine($"CLIENT LOADED: {carId}");
                await acsClient.SendChatAsync(carId, "순위 보여주는 기능 테스트 중입니다.");
                await acsClient.SendChatAsync(carId, "도움말은 ?help");

                var car = await carInfos.GetAsync(carId);
                await acsClient.SendChatAsync(carId, await leaderboard.GenerateMyRankTableAsync(car.CarName, car.DriverName));
            };
            acsClient.OnVersion += (packetId, protocolVersion) => Console.WriteLine("PROTOCOL VERSION IS:" + (int)protocolVersion);
            acsClient.OnNewSession += async (byte packetId, SessionInfoEvent eventData) =>
            {
                Console.WriteLine("New session started");
                Console.WriteLine($"PROTOCOL: {eventData.Version}, SESSION {eventData.Name} {eventData.SessionIndex + 1}/{eventData.SessionCount}, TRACK: {eventData.Track}");

                await leaderboard.SetTrackAsync(eventData.Track);
                await acsClient.BroadcastChatAsync(await leaderboard.GenerateTopRankTableAsync());
            };
            acsClient.OnSessionInfo += async (byte packetId, SessionInfoEvent eventData) =>
            {
                Console.WriteLine($"PROTOCOL: {eventData.Version}, SESSION {eventData.Name} {eventData.SessionIndex + 1}/{eventData.SessionCount}, TRACK: {eventData.Track}");

                await leaderboard.SetTrackAsync(eventData.Track);
            };
            acsClient.OnEndSession += (packetId, reportFile) => Console.WriteLine($"ACSP_END_SESSION. REPORT JSON AVAILABLE AT: {reportFile}");
            acsClient.OnClientEvent += (byte packetId, ClientEventEvent eventData) =>
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
            acsClient.OnCarInfo += (byte packetId, CarInfoEvent eventData) =>
                Console.WriteLine($"CarInfo CAR:{eventData.CarId} {eventData.Model} [{eventData.Skin}] DRIVER:{eventData.DriverName} TEAM:{eventData.DriverTeam} GUID:{eventData.DriverGuid} CONNECTED:{eventData.IsConnected}");
            acsClient.OnCarUpdate += (byte packetId, CarUpdateEvent eventData) =>
                Console.Write($"CarUpdate CAR:{eventData.CarId} POS:{eventData.Position} VEL:{eventData.Velocity} GEAR:{eventData.Gear} RPM:{eventData.Rpm} NSP:{eventData.NormalizedSplinePosition}");
            acsClient.OnNewConnection += (byte packetId, ConnectionEvent eventData) =>
                Console.WriteLine($"ACSP_NEW_CONNECTION {eventData.DriverName} ({eventData.DriverGuid}), {eventData.CarModel} [{eventData.CarSkin}] ({eventData.CarId})");
            acsClient.OnConnectionClosed += (byte packetId, ConnectionEvent eventData) =>
                Console.WriteLine($"ACSP_CONNECTION_CLOSED {eventData.DriverName} ({eventData.DriverGuid}), {eventData.CarModel} [{eventData.CarSkin}] ({eventData.CarId})");
            acsClient.OnLapCompleted += async (byte packetId, LapCompletedEvent eventData) =>
            {
                Console.WriteLine($"ACSP_LAP_COMPLETED CAR:{eventData.CarId} LAP:{eventData.LapTime} CUTS:{eventData.Cuts}");

                if (eventData.Cuts <= 0)
                {
                    try
                    {
                        var car = await carInfos.GetAsync(eventData.CarId);
                        var newRecord = await leaderboard.RegisterLaptimeAsync(car.CarName, car.DriverName, eventData.LapTime);
                        if (newRecord != null)
                        {
                            await acsClient.BroadcastChatAsync($"{newRecord.DriverName} 님이 새로운 {newRecord.Rank}위 기록({newRecord.Laptime.LaptimeFormat()})을 달성했습니다.");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Unable to get car({eventData.CarId}). dropping laptime {eventData.LapTime}");
                        Console.WriteLine(e);
                    }
                }
            };

            acsClient.GetSessionInfoAsync().Wait();
            acsClient.DispatchMessagesAsync().Wait();
        }
    }
}
