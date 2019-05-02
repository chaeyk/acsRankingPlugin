using JsonNet.PrivateSettersContractResolvers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace acsRankingPlugin
{
    class LeaderBoard
    {
        public string Name { get; private set; }
        public string Track { get; private set; }
        public List<Driver> Drivers { get; private set; }
        public DateTime StartTime { get; private set; }


        private static JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ContractResolver = new PrivateSetterContractResolver()
        };

        // 파일에서 로드해보고 없으면 디폴트 객체를 생성한다.
        public static LeaderBoard Load(string name)
        {
            var path = GetStoragePath();
            var filepath = $"{path}\\{name}.json";

            try
            {
                var leaderBoard = JsonConvert.DeserializeObject<LeaderBoard>(File.ReadAllText(filepath), _jsonSettings);
                Console.WriteLine($"LeaderBoard({name}) loaded: {JsonConvert.SerializeObject(leaderBoard, _jsonSettings)}");
                leaderBoard.SortDrivers();
                return leaderBoard;
            }
            catch (IOException)
            {
                return new LeaderBoard(name, "");
            }
        }

        private static string GetStoragePath()
        {
            return $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\acsRankingPlugin";
        }

        // for JSON.NET
        public LeaderBoard() : this("", "")
        {
        }
           
        public LeaderBoard(string name, string track)
        {
            Name = name;
            Track = track;
            Drivers = new List<Driver>();
            StartTime = DateTime.Now;
        }

        public void Save()
        {
            var json = JsonConvert.SerializeObject(this, _jsonSettings);

            var path = GetStoragePath();
            Directory.CreateDirectory(path);

            var filepath = $"{path}\\{Name}.json";
            File.WriteAllText(filepath, json);
        }

        private Driver FindDriver(int carId)
        {
            return Drivers.Find(d => d.CarId == carId);
        }

        private Driver FindDriver(string name)
        {
            return Drivers.Find(d => d.Name == name);
        }

        // 드라이버를 리더보드에 등록. 중복 호출해도 문제 없도록 구현되었다.
        public Driver RegisterDriver(int carId, string name, string guid)
        {
            try
            {
                var driverById = FindDriver(carId);
                var driverByName = FindDriver(name);

                // carid 로 발견한 경우 다음 3가지 케이스가 나온다
                // - name이 일치하면 그냥 넘어가면 된다
                // - name이 unknown이면 driver 정보가 없을 때 time만 임시로 등록해 놓은 것
                // - name이 다른 사람이면 멀티 서버가 재시작되어 여기 플러그인에 쓰레기가 남은 것
                if (driverById != null)
                {
                    if (driverById.IsUnknownDriver)
                    {
                        if (driverByName == null)
                        {
                            // time만 임시로 등록되어 있으므로 이름등의 정보를 업데이트한다.
                            driverById.Name = name;
                            driverById.Guid = guid;
                            return driverById;
                        }
                        else
                        {
                            // driverById 에 있는 것은 time만 임시로 등록된 것이고
                            // driverByName 에 있는 것은 storage에서 로드된 드라이버 정보
                            // 둘을 비교해 타임이 빠른 것을 등록해야 한다.
                            if (driverById.Time < driverByName.Time)
                            {
                                driverByName.Time = driverById.Time;
                                driverByName.Sent = driverById.Sent;
                            }
                        }
                    }
                    else if (driverById.Name != name)
                    {
                        Console.WriteLine($"Duplicated CarId: {carId} ({driverById.Name}) ({name}). {driverById.Name} is regarded as disconnected.");
                        driverById.CarId = -1;
                        driverById = null;
                    }
                }

                if (driverByName != null)
                {
                    driverByName.CarId = carId;
                    if (driverByName.Guid != guid)
                    {
                        Console.WriteLine($"{name}'s guid changed from {driverByName.Guid} to {guid}");
                        driverByName.Guid = guid;
                    }
                }
                else
                {
                    driverByName = new Driver(carId, name, guid);
                    Drivers.Add(driverByName);

                    Console.WriteLine($"Driver {name} ({carId}) registered.");
                }
                return driverByName;
            }
            finally
            {
                SortDrivers();
                Save();
            }
        }

        public Driver ReportDriver(int carId, TimeSpan time)
        {
            try
            {
                var driver = FindDriver(carId);
                if (driver != null)
                {
                    if (driver.Time > time)
                    {
                        driver.Time = time;
                    }
                }
                else
                {
                    // 일단 타임만 임시로 등록해 놓고, main loop에서 드라이버 정보를 얻어 그 때 다시 업데이트 한다.
                    Console.WriteLine($"Unknown Driver registered: {carId}");
                    driver = new Driver(carId);
                    driver.Time = time;
                }

                SortDrivers();
                return driver;
            }
            finally
            {
                Save();
            }
        }

        public void LeaveDriver(int carId)
        {
            var driver = FindDriver(carId);
            if (driver != null)
            {
                Console.WriteLine($"{driver.Name} ({driver.CarId}) leaved.");
                driver.CarId = -1;
            }
            else
            {
                Console.WriteLine($"Unknown Driver: {carId}");
            }
        }

        public override string ToString()
        {
            if (Drivers.Count == 0)
            {
                return "";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Leader Board ({StartTime} ~ Now)");
            sb.AppendLine("=================================");
            sb.AppendLine("순위   시간       이름");
            sb.AppendLine("=================================");
            foreach (var driver in Drivers)
            {
                sb.AppendLine(string.Format("{0,4}   {1,-9}  {2}", driver.Rank, driver.FormattedTime, driver.Name));
            }
            sb.AppendLine("=================================");
            return sb.ToString();
        }

        private void SortDrivers()
        {
            Drivers.Sort((d1, d2) =>
            {
                if (d1.Time != d2.Time)
                {
                    return d1.Time.CompareTo(d2.Time);
                }
                return d1.Name.CompareTo(d2.Name);
            });
            for (var i = 0; i < Drivers.Count; i++)
            {
                if (i > 0 && Drivers[i-1].Time == Drivers[i].Time) // 동률
                {
                    Drivers[i].Rank = Drivers[i - 1].Rank;
                }
                else
                {
                    Drivers[i].Rank = (UInt32)i + 1;
                }
            }
        }
    }

    class Driver
    {
        public const string UNKNOWN_DRIVER = "unknown";

        [JsonIgnore]
        public int CarId { get; set; }

        public string Name { get; set; }
        public string Guid { get; set; }
        public TimeSpan Time { get; set; }

        [JsonIgnore]
        public string FormattedTime
        {
            get
            {
                return (Time == TimeSpan.MaxValue) ? "oo:oo.ooo" : Time.ToString("mm\\:ss\\.FFF");
            }
        }

        [JsonIgnore]
        public UInt32 Rank { get; set; }

        // 기록 경신을 하면 내 기록을 chat으로 보내주는데 아직 unknown이면 car 정보를 받은 후에 보내야 한다.
        // 이걸로 chat을 보냈는지 여부를 임시 기록한다.
        [JsonIgnore]
        public bool Sent { get; set; }

        [JsonIgnore]
        public bool IsUnknownDriver
        {
            get { return Name == UNKNOWN_DRIVER; }
        }

        // For JSON.NET
        private Driver() : this(-1, UNKNOWN_DRIVER, "")
        {
        }

        public Driver(int carId) : this(carId, UNKNOWN_DRIVER, "")
        {
        }

        public Driver(int carId, string name, string guid)
        {
            CarId = carId;
            Name = name;
            Guid = guid;
            Time = TimeSpan.MaxValue;
            Rank = UInt32.MaxValue;
            Sent = true;
        }
    }
}
