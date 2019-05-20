using NeoSmart.AsyncLock;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace acsRankingPlugin
{
    class JsonData
    {
        public DateTime Timestamp { get; private set; }
        public string Track { get; private set; }
        public List<DriverLaptime> Drivers { get; private set; }

        public JsonData(DateTime timestamp, string track, List<DriverLaptime> drivers)
        {
            Timestamp = timestamp;
            Track = track;
            Drivers = drivers;
        }
    }

    class JsonStorage : IStorage
    {
        private DateTime _timestamp;
        private string _track;
        private List<DriverLaptime> _drivers;

        private JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor
        };

        private string _jsonfile;

        private AsyncLock _lock = new AsyncLock();

        public JsonStorage(string path, string name, bool reset = false)
        {
            _jsonfile = $"{path}\\{name}.json";

            Directory.CreateDirectory(path);

            _timestamp = DateTime.Now;
            _track = "";
            _drivers = new List<DriverLaptime>();

            if (!reset)
            {
                try
                {
                    var jsonData = JsonConvert.DeserializeObject<JsonData>(File.ReadAllText(_jsonfile), _jsonSettings);
                    Console.WriteLine($"Leaderboard loaded: {JsonConvert.SerializeObject(jsonData, _jsonSettings)}");

                    _timestamp = jsonData.Timestamp;
                    _track = jsonData.Track;
                    _drivers = jsonData.Drivers;
                    _drivers.Sort();
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine($"New leaderboard created.");
                }
            }
        }

        public Task<DateTime> GetTimestampAsync()
        {
            return Task.FromResult(_timestamp);
        }

        public Task<string> GetTrackAsync()
        {
            return Task.FromResult(_track);
        }

        public async Task SetTrackAsync(string track)
        {
            if (_track != track)
            {
                Console.WriteLine($"Track changed [{_track} -> {track}]. Reset Database.");
                _track = track;
                await ResetAsync();
            }
        }

        protected async Task SaveAsync()
        {
            using (await _lock.LockAsync())
            {
                var json = JsonConvert.SerializeObject(new JsonData(_timestamp, _track, _drivers), _jsonSettings);
                var buffer = Encoding.UTF8.GetBytes(json);

                using (FileStream fs = new FileStream(_jsonfile, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    await fs.WriteAsync(buffer, 0, buffer.Length);
                }
            }
        }

        public async Task InsertAsync(DriverLaptime driverLaptime)
        {
            using (await _lock.LockAsync())
            {
                if (await GetAsync(driverLaptime.Car, driverLaptime.Driver) != null)
                {
                    throw new Exception($"Duplicated driver [{driverLaptime.Driver} ({driverLaptime.Car})]");
                }
                _drivers.Add(driverLaptime);
                _drivers.Sort();
                await SaveAsync();
            }
        }

        public async Task UpdateAsync(DriverLaptime driverLaptime)
        {
            using (await _lock.LockAsync())
            {
                // driverLaptime이 이미 list에 있는 것만 확인하면 된다.
                if (_drivers.Find(v => v == driverLaptime) == null)
                {
                    throw new Exception($"{driverLaptime} is not in Drivers.");
                }
                _drivers.Sort();
                await SaveAsync();
            }
        }

        public async Task<DriverLaptime> GetAsync(string car, string driver)
        {
            using (await _lock.LockAsync())
            {
                return _drivers.Find(v => v.Car == car && v.Driver == driver);
            }
        }

        public async Task<List<DriverLaptime>> ListAsync()
        {
            using (await _lock.LockAsync())
            {
                // 밖에서 list를 쓰려면 copy해야 안전하다.
                return new List<DriverLaptime>(_drivers);
            }
        }

        public async Task ResetAsync()
        {
            using (await _lock.LockAsync())
            {
                _timestamp = DateTime.Now;
                _drivers.Clear();
                await SaveAsync();
            }
        }
    }
}
