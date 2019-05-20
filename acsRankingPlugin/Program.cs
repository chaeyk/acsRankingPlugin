using CommandLine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
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

            [Option("car-name", Required = false, Separator = ',',
                HelpText = "Mapping long car name to short 3 chr name for leaderboard. ex> --car-name ks_ferrari_sf70h=70h,ks_ferrari_sf15t=15t")]
            public IEnumerable<string> CarNames
            {
                set
                {
                    var nameMap = new Dictionary<string, string>();
                    foreach (var opt in value)
                    {
                        string[] parts = opt.Split('=');
                        if (parts.Length != 2 || parts[0].Length <= 0)
                        {
                            throw new Exception($"Invalid --car-name option: {opt}");
                        }
                        if (parts[1].Length <= 0 || parts[1].Length > 3)
                        {
                            throw new Exception($"Invalid short name [{parts[1]}] of {parts[0]}.");
                        }
                        if (nameMap.ContainsKey(parts[0]))
                        {
                            throw new Exception($"--car-name option already has {parts[0]}: {opt}");
                        }
                        if (nameMap.ContainsValue(parts[1]))
                        {
                            throw new Exception($"--car-name option already has short name {parts[1]}: {opt}");
                        }

                        nameMap.Add(parts[0], parts[1]);
                        Console.WriteLine($"Car name mapping: {parts[0]} -> {parts[1]}");
                    }
                    CarShortNameMap = new ReadOnlyDictionary<string, string>(nameMap);
                }
            }

            public IReadOnlyDictionary<string, string> CarShortNameMap { get; private set; } =
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

            [Option("reset", HelpText = "Reset data.")]
            public bool Reset { get; set; }
        }

        private readonly Options _options;
        private readonly IStorage _storage;
        private readonly Leaderboard _leaderboard;
        private readonly ACSClient _acsClient;
        private readonly CarInfos _carInfos;

        public Program(Options options)
        {
            var name = $"{options.Name}-{options.PluginPort}-{options.ServerPort}";
            var storagePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\acsRankingPlugin";

            _options = options;
            //IStorage storage = new AccessDbStorage(storagePath, name, reset);
            _storage = new JsonStorage(storagePath, name, options.Reset);
            _leaderboard = new Leaderboard(_storage);
            _acsClient = new ACSClient(options.PluginPort, options.ServerPort);
            _carInfos = new CarInfos(_acsClient);
        }

        protected async void OnChat(byte packetId, ChatEvent eventData)
        {
            var cmds = eventData.Message.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (cmds.Length == 1 && cmds[0] == "?help")
            {
                var exeVersion = Assembly.GetExecutingAssembly().GetName().Version;
                var sb = new StringBuilder();
                sb.AppendLine($"tobot's acsRankingPlugin {exeVersion} 명령어");
                sb.AppendLine("=================================");
                sb.AppendLine("?help : 도움말 표시");
                sb.AppendLine("?rank [top|full]: 종합 리더보드");
                sb.AppendLine("   top: 상위권만 출력, full: 전체 출력");
                sb.AppendLine("?carrank [top|full]: 동일차량 리더보드");
                sb.AppendLine("?driverrank [top|full]: 유저별 최고기록 리더보드");
                sb.AppendLine("?carname : 차량 이름 변환표");
                sb.AppendLine("?ballast [숫자] : 자기 차 무게 증가 (kg)");
                sb.AppendLine("=================================");

                await _acsClient.SendChatAsync(eventData.CarId, sb.ToString());
            }
            else if (cmds[0] == "?rank")
            {
                var car = await _carInfos.GetAsync(eventData.CarId);
                await _acsClient.SendChatAsync(eventData.CarId, await GenerateMyRankAsync(car.CarName, car.DriverName, cmds));
            }
            else if (cmds[0] == "?carrank")
            {
                var car = await _carInfos.GetAsync(eventData.CarId);
                await _acsClient.SendChatAsync(eventData.CarId, await GenerateCarrankAsync(car.CarName, car.DriverName, cmds));
            }
            else if (cmds[0] == "?driverrank")
            {
                var car = await _carInfos.GetAsync(eventData.CarId);
                await _acsClient.SendChatAsync(eventData.CarId, await GenerateDriverrankAsync(car.CarName, car.DriverName, cmds));
            }
            else if (cmds.Length == 1 && cmds[0] == "?carname")
            {
                var records = new Records(await _storage.ListAsync(), _options.CarShortNameMap);
                await _acsClient.SendChatAsync(eventData.CarId, records.GenerateCarShortNameMapTable());

            }
            else if (cmds.Length == 2 && cmds[0] == "?ballast")
            {
                int weight;
                if (Int32.TryParse(cmds[1], out weight) && weight >= 0 && weight <= 150)
                {
                    await _acsClient.SendAdminMessageAsync($"/ballast {eventData.CarId} {weight}");
                    await _acsClient.SendChatAsync(eventData.CarId, $"?ballast 명령어를 실행했습니다.");
                }
                else
                {
                    await _acsClient.SendChatAsync(eventData.CarId, $"무게를 잘못 입력했습니다: {cmds[1]}");
                }
            }
            else if (eventData.Message.StartsWith("?"))
            {
                await _acsClient.SendChatAsync(eventData.CarId, $"플러그인 명령어를 잘못 입력했습니다: {eventData.Message}");
            }
        }

        protected async Task<string> GenerateMyRankAsync(string car, string driver, string[] cmds)
        {
            var laptimes = await _storage.ListAsync();
            return await GenerateRankAsync(laptimes, car, driver, cmds);
        }

        protected async Task<string> GenerateCarrankAsync(string car, string driver, string[] cmds)
        {
            var laptimes = (await _storage.ListAsync()).FindAll(v => v.Car == car);
            return await GenerateRankAsync(laptimes, car, driver, cmds);
        }

        protected async Task<string> GenerateDriverrankAsync(string car, string driver, string[] cmds)
        {
            var drivers = new HashSet<string>();
            var laptimes = (await _storage.ListAsync()).FindAll(v =>
            {
                if (drivers.Contains(v.Driver))
                {
                    return false;
                }
                drivers.Add(v.Driver);
                return true;
            });
            return await GenerateRankAsync(laptimes, car, driver, cmds);
        }

        protected async Task<string> GenerateRankAsync(List<DriverLaptime> laptimes, string car, string driver, string[] cmds)
        {
            string message = null;

            if (cmds.Length == 1)
            {
                message = await _leaderboard.GenerateMyRankTableAsync(laptimes, _options.CarShortNameMap, car, driver);
            }
            else if (cmds.Length == 2)
            {
                if (cmds[1] == "full")
                {
                    message = await _leaderboard.GenerateRankTableAsync(laptimes, _options.CarShortNameMap);
                }
                else if (cmds[1] == "top")
                {
                    message = await _leaderboard.GenerateTopRankTableAsync(laptimes, _options.CarShortNameMap);
                }
            }
            return message ?? $"명령어를 잘못 입력했습니다: {string.Join(" ", cmds)}";
        }

        public void run()
        {
            _acsClient.OnError += (packetId, message) => Console.WriteLine(message);
            _acsClient.OnChat += OnChat;
            _acsClient.OnClientLoaded += async (packetId, carId) =>
            {
                Console.WriteLine($"CLIENT LOADED: {carId}");
                await _acsClient.SendChatAsync(carId, "순위 보여주는 기능 테스트 중입니다.");
                await _acsClient.SendChatAsync(carId, "도움말은 ?help");

                var car = await _carInfos.GetAsync(carId);
                var table = await _leaderboard.GenerateMyRankTableAsync(await _storage.ListAsync(), _options.CarShortNameMap, car.CarName, car.DriverName);
                await _acsClient.SendChatAsync(carId, table);
            };
            _acsClient.OnVersion += (packetId, protocolVersion) => Console.WriteLine("PROTOCOL VERSION IS:" + (int)protocolVersion);
            _acsClient.OnNewSession += async (byte packetId, SessionInfoEvent eventData) =>
            {
                Console.WriteLine("New session started");
                Console.WriteLine($"PROTOCOL: {eventData.Version}, SESSION {eventData.Name} {eventData.SessionIndex + 1}/{eventData.SessionCount}, TRACK: {eventData.Track}");

                await _storage.SetTrackAsync(eventData.Track);
                await _acsClient.BroadcastChatAsync(await _leaderboard.GenerateTopRankTableAsync(await _storage.ListAsync(), _options.CarShortNameMap));
            };
            _acsClient.OnSessionInfo += async (byte packetId, SessionInfoEvent eventData) =>
            {
                Console.WriteLine($"PROTOCOL: {eventData.Version}, SESSION {eventData.Name} {eventData.SessionIndex + 1}/{eventData.SessionCount}, TRACK: {eventData.Track}");

                await _storage.SetTrackAsync(eventData.Track);
            };
            _acsClient.OnEndSession += (packetId, reportFile) => Console.WriteLine($"ACSP_END_SESSION. REPORT JSON AVAILABLE AT: {reportFile}");
            _acsClient.OnClientEvent += (byte packetId, ClientEventEvent eventData) =>
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
            _acsClient.OnCarInfo += (byte packetId, CarInfoEvent eventData) =>
                Console.WriteLine($"CarInfo CAR:{eventData.CarId} {eventData.Model} [{eventData.Skin}] DRIVER:{eventData.DriverName} TEAM:{eventData.DriverTeam} GUID:{eventData.DriverGuid} CONNECTED:{eventData.IsConnected}");
            _acsClient.OnCarUpdate += (byte packetId, CarUpdateEvent eventData) =>
                Console.Write($"CarUpdate CAR:{eventData.CarId} POS:{eventData.Position} VEL:{eventData.Velocity} GEAR:{eventData.Gear} RPM:{eventData.Rpm} NSP:{eventData.NormalizedSplinePosition}");
            _acsClient.OnNewConnection += (byte packetId, ConnectionEvent eventData) =>
                Console.WriteLine($"ACSP_NEW_CONNECTION {eventData.DriverName} ({eventData.DriverGuid}), {eventData.CarModel} [{eventData.CarSkin}] ({eventData.CarId})");
            _acsClient.OnConnectionClosed += (byte packetId, ConnectionEvent eventData) =>
                Console.WriteLine($"ACSP_CONNECTION_CLOSED {eventData.DriverName} ({eventData.DriverGuid}), {eventData.CarModel} [{eventData.CarSkin}] ({eventData.CarId})");
            _acsClient.OnLapCompleted += async (byte packetId, LapCompletedEvent eventData) =>
            {
                Console.WriteLine($"ACSP_LAP_COMPLETED CAR:{eventData.CarId} LAP:{eventData.LapTime} CUTS:{eventData.Cuts}");

                if (eventData.Cuts <= 0)
                {
                    try
                    {
                        var car = await _carInfos.GetAsync(eventData.CarId);
                        var newRecord = await _leaderboard.RegisterLaptimeAsync(car.CarName, car.DriverName, eventData.LapTime);
                        if (newRecord != null)
                        {
                            await _acsClient.BroadcastChatAsync($"{newRecord.DriverName} 님이 새로운 {newRecord.Rank}위 기록({newRecord.Laptime.LaptimeFormat()})을 달성했습니다.");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Unable to get car({eventData.CarId}). dropping laptime {eventData.LapTime}");
                        Console.WriteLine(e);
                    }
                }
            };

            _acsClient.GetSessionInfoAsync().Wait();
            _acsClient.DispatchMessagesAsync().Wait();
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

            var program = new Program(options);
            program.run();
        }
    }
}
