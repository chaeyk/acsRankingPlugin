using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;

namespace acsRankingPlugin
{
    class DriverLaptime
    {
        public string Car { get; set; }
        public string Driver { get; set; }
        public TimeSpan Laptime { get; set; }

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
    }

    class AccessDbStorage : IDisposable
    {
        private readonly OleDbConnection _conn;

        public DateTime Timestamp;

        public AccessDbStorage(string path, string name, bool reset = false)
        {
            var adbfile = $"{path}\\{name}-storage-v1.accdb";
            var connstr = $"Provider=Microsoft.Jet.OLEDB.4.0;Data Source={adbfile};Jet OLEDB:Engine Type=5";

            if (reset)
            {
                File.Delete(adbfile);
            }
            if (!File.Exists(adbfile))
            {
                try
                {
                    CreateNewAccessDatabase(connstr);
                }
                catch (Exception)
                {
                    File.Delete(adbfile);
                    throw;
                }
            }

            Timestamp = File.GetCreationTime(adbfile);

            _conn = new OleDbConnection(connstr);
            _conn.Open();
        }

        protected void CreateNewAccessDatabase(string connstr)
        {
            var cat = new ADOX.Catalog();
            cat.Create(connstr);

            var laptimeTable = new ADOX.Table();

            laptimeTable.Name = "DriverLaptime";
            laptimeTable.Columns.Append("car");
            laptimeTable.Columns.Append("driver");
            laptimeTable.Columns.Append("laptime", ADOX.DataTypeEnum.adInteger);

            var pkIdx = new ADOX.Index();
            pkIdx.PrimaryKey = true;
            pkIdx.Name = "PK_DriverLaptime";
            pkIdx.Columns.Append("car");
            pkIdx.Columns.Append("driver");
            laptimeTable.Indexes.Append(pkIdx);

            var laptimeIdx = new ADOX.Index();
            laptimeIdx.PrimaryKey = false;
            laptimeIdx.Name = "LaptimeIndex_DriverLaptime";
            laptimeIdx.Columns.Append("car");
            laptimeIdx.Columns.Append("laptime", ADOX.DataTypeEnum.adInteger);
            laptimeTable.Indexes.Append(laptimeIdx);

            cat.Tables.Append(laptimeTable);

            // track이 바뀌면 데이터를 리셋해야 하므로 track 이름을 저장할 곳이 필요하다.
            var trackTable = new ADOX.Table();

            trackTable.Name = "Track";
            trackTable.Columns.Append("name");

            cat.Tables.Append(trackTable);

            var conn = cat.ActiveConnection as ADODB.Connection;
            if (conn != null)
            {
                try
                {
                    object recordsAffected;
                    conn.Execute("insert into Track (name) values (' ')", out recordsAffected);
                }
                finally
                {
                    conn.Close();
                }
            }
        }

        public string GetTrack()
        {
            var command = new OleDbCommand("select name from Track", _conn);
            return (string)command.ExecuteScalar();

        }

        public void SetTrack(string track)
        {
            var oldTrack = GetTrack();
            if (track == oldTrack)
            {
                return;
            }

            Console.WriteLine($"Track changed [{oldTrack} -> {track}]. Reset Database.");

            var command1 = new OleDbCommand("delete from DriverLaptime", _conn);
            command1.ExecuteNonQuery();

            var command2 = new OleDbCommand("update Track set name = @name", _conn);
            command2.Parameters.AddWithValue("@name", track);
            if (command2.ExecuteNonQuery() != 1)
            {
                throw new Exception("Track update failed.");
            }
        }


        public void Insert(DriverLaptime driverLaptime)
        {
            var command = new OleDbCommand("insert into DriverLaptime (car, driver, laptime) values (@car, @driver, @laptime)", _conn);
            command.Parameters.AddWithValue("@car", driverLaptime.Car);
            command.Parameters.AddWithValue("@driver", driverLaptime.Driver);
            command.Parameters.AddWithValue("@laptime", driverLaptime.Laptime.TotalMilliseconds);
            command.ExecuteNonQuery();
        }

        public void Update(DriverLaptime driverLaptime)
        {
            var command = new OleDbCommand($"update DriverLaptime set laptime = @laptime where car = @car and driver = @driver", _conn);
            command.Parameters.AddWithValue("@laptime", driverLaptime.Laptime.TotalMilliseconds);
            command.Parameters.AddWithValue("@car", driverLaptime.Car);
            command.Parameters.AddWithValue("@driver", driverLaptime.Driver);
            var updated = command.ExecuteNonQuery();
            if (updated == 0)
            {
                throw new Exception($"SQL update statement failed: {driverLaptime}");
            }
        }

        public DriverLaptime Get(string car, string driver)
        {
            var command = new OleDbCommand("select car, driver, laptime from DriverLaptime where car = @car and driver = @driver", _conn);
            command.Parameters.AddWithValue("@car", car);
            command.Parameters.AddWithValue("@driver", driver);
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    return null;
                }

                return new DriverLaptime(
                    reader.GetFieldValue<string>(0),
                    reader.GetFieldValue<string>(1),
                    reader.GetInt32(2)
                );
            }
        }

        public List<DriverLaptime> List()
        {
            var result = new List<DriverLaptime>();

            var command = new OleDbCommand("select car, driver, laptime from DriverLaptime order by laptime asc", _conn);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add(new DriverLaptime(
                        reader.GetFieldValue<string>(0),
                        reader.GetFieldValue<string>(1),
                        reader.GetInt32(2)
                    ));
                }
            }
            return result;
        }

        public int GetRank(TimeSpan laptime)
        {
            var command = new OleDbCommand("select count(*) from DriverLaptime where laptime < @laptime", _conn);
            command.Parameters.AddWithValue("@laptime", laptime.TotalMilliseconds);
            return (int)command.ExecuteScalar() + 1;
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
