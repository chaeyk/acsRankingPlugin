using NeoSmart.AsyncLock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace acsRankingPlugin
{
    class Car
    {
        public string CarName { get; private set; }
        public string DriverName { get; private set; }

        public Car(string carName, string driverName)
        {
            CarName = carName;
            DriverName = driverName;
        }
    }

    class NewRecord
    {
        public string DriverName { get; }
        public int Rank { get; }
        public TimeSpan Laptime { get; }

        public NewRecord(string driverName, int rank, TimeSpan laptime)
        {
            DriverName = driverName;
            Rank = rank;
            Laptime = laptime;
        }
    }

    class Leaderboard
    {
        private IStorage _storage;

        private Dictionary<int, Car> _cars = new Dictionary<int, Car>();
        private Dictionary<int, TimeSpan> _laptimes = new Dictionary<int, TimeSpan>(); // car 정보가 없어서 저장하지 못한 랩타임

        private AsyncLock _lock = new AsyncLock();

        public Leaderboard(string name, string storagePath, bool reset = false)
        {
            //_storage = new AccessDbStorage(storagePath, name, reset);
            _storage = new JsonStorage(storagePath, name, reset);
        }

        public async Task<string> GetTrackAsync()
        {
            return await _storage.GetTrackAsync();
        }

        public async Task SetTrackAsync(string track)
        {
            await _storage.SetTrackAsync(track);
        }

        // 차와 드라이버의 정보를 _cars에 저장하는데
        // 전에 _cars에 정보가 없어 저장하지 못한 랩타임 등록도 여기서 한다.
        // 랩타임이 새 기록이면 NewRecord가 리턴된다.
        public async Task<NewRecord> RegisterCarAsync(int carId, string carName, string driverName)
        {
            var lk = await _lock.LockAsync();
            var hasLock = true;
            try
            {
                var car = _cars.TryGetValue(carId);
                if (car == null)
                {
                    car = new Car(carName, driverName);
                    _cars.Add(carId, new Car(carName, driverName));

                    var laptime = _laptimes.Pop(carId);
                    if (laptime != TimeSpan.Zero)
                    {
                        // RegisterLaptime()을 호출할 때 Lock을 유지해야 할 이유가 없다.
                        hasLock = false;
                        lk.Dispose();
                        return await RegisterLaptimeAsync(car.CarName, car.DriverName, laptime);
                    }
                }
                else
                {
                    // 서버가 재시작 되면 이런 일이 발생한다.
                    // UDP socket 이라서 따로 감지할 수 있는 방법이 없다.
                    var changed = false;
                    if (car.CarName != carName)
                    {
                        Console.WriteLine($"carId[{carId}]'s model is changed: {car.CarName} -> {carName}");
                        changed = true;
                    }
                    if (car.DriverName != driverName)
                    {
                        Console.WriteLine($"carId[{carId}]'s driver is changed: {car.DriverName} -> {driverName}");
                        changed = true;
                    }

                    if (changed)
                    {
                        _cars.Remove(carId);
                        _cars.Add(carId, new Car(carName, driverName));
                    }
                }
                return null;
            }
            finally
            {
                if (hasLock)
                {
                    lk.Dispose();
                }
            }
        }

        public async Task UnregisterCarAsync(int carId)
        {
            using (await _lock.LockAsync())
            {
                _cars.Remove(carId);
                _laptimes.Remove(carId); // _laptimes를 삭제하지 않으면, 나중에 다른 사람이 들어올 때 그 사람의 기록이 된다.
            }
        }

        // 새로운 기록일 경우 NewRecord가 리턴된다.
        public async Task<NewRecord> RegisterLaptimeAsync(string carName, string driverName, TimeSpan laptime)
        {
            var record = await _storage.GetAsync(carName, driverName);
            if (record == null)
            {
                record = new DriverLaptime(carName, driverName, laptime);

                // 이거 사실 엄격하게 하려면 Get과 Insert는 atomic하게 묶어야 하는데
                // 같은 사람의 랩타임 데이터가 그렇게 빠르게 들어올 일은 거의 없으므로
                // 일단 무시해보자.
                await _storage.InsertAsync(record);
            }
            else
            {
                if (record.Laptime < laptime)
                {
                    // 신기록이 아니므로 그냥 종료
                    return null;
                }
                record.Laptime = laptime;
                await _storage.UpdateAsync(record);
            }

            var rank = await _storage.GetRankAsync(laptime);
            return new NewRecord(driverName, rank, laptime);
        }

        // carId 만으로 랩타임을 등록하려면, carname, drivername을 이미 가지고 있어야 한다.
        // 없으면 _laptimes에 랩타임만 보관해놓고 나중에 RegisterCar()를 통해 랩타임을 등록해야 한다.
        // 리턴값은 carId에 해당하는 car, driver 정보를 가지고 있었을 경우 true이다.
        // false가 리턴되면 호출한 쪽에서는 RegisterCar()를 호출해 차량 정보를 제공해야 한다.
        public async Task<(bool, NewRecord)> RegisterLaptimeAsync(int carId, TimeSpan laptime)
        {
            Car car = null;
            using (await _lock.LockAsync())
            {
                car = _cars.TryGetValue(carId);
                if (car == null)
                {
                    _laptimes.Add(carId, laptime);
                    return (false, null);
                }
            }

            var newRecord = await RegisterLaptimeAsync(car.CarName, car.DriverName, laptime);
            return (true, newRecord);
        }

        public async Task<string> GenerateRankTableAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Leader Board ({await _storage.GetTimestampAsync()} ~ Now)");
            sb.AppendLine("=================================");
            sb.AppendLine("순위   시간        이름");
            sb.AppendLine("=================================");

            var records = await _storage.ListAsync();
            int rank = 1;
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (i > 0 && record.Laptime != records[i - 1].Laptime)
                {
                    rank = i + 1;
                }
                sb.AppendLine(string.Format("{0,4}   {1,-9}  {2}", rank, record.Laptime.LaptimeFormat(), record.Driver));
            }
            sb.AppendLine("=================================");

            return sb.ToString();
        }
    }
}
