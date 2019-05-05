using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace acsRankingPlugin
{
    class DriverLaptime : IComparable<DriverLaptime>
    {
        public string Car { get; set; }
        public string Driver { get; set; }
        public TimeSpan Laptime { get; set; }

        // for JSON.NET
        private DriverLaptime()
        {
        }

        public DriverLaptime(string car, string driver, TimeSpan laptime)
        {
            Car = car;
            Driver = driver;
            Laptime = laptime;
        }

        public DriverLaptime(string car, string driver, int laptime) : this(car, driver, TimeSpan.FromMilliseconds(laptime))
        {
        }

        public override string ToString()
        {
            return $"car:{Car}, driver:{Driver}, laptime:{Laptime}";
        }

        public int CompareTo(DriverLaptime other)
        {
            return Laptime.CompareTo(other.Laptime);
        }
    }

    interface IStorage
    {
        Task<DateTime> GetTimestampAsync();

        Task<string> GetTrackAsync();
        Task SetTrackAsync(string track);

        Task InsertAsync(DriverLaptime driverLaptime);
        Task UpdateAsync(DriverLaptime driverLaptime);
        Task<DriverLaptime> GetAsync(string car, string driver);
        Task<List<DriverLaptime>> ListAsync();
        Task<int> GetRankAsync(TimeSpan laptime);
        Task ResetAsync(); // 랩타임 데이터를 모두 삭제한다.
    }
}
