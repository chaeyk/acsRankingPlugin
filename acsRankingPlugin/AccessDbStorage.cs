using NeoSmart.AsyncLock;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Threading.Tasks;

namespace acsRankingPlugin
{
    class AccessDbStorage : IStorage, IDisposable
    {
        private readonly OleDbConnection _conn;
        private AsyncLock _lock = new AsyncLock();

        public AccessDbStorage(string path, string name, bool reset = false)
        {
            var adbfile = $"{path}\\{name}-storage-v1.accdb";
            var connstr = $"Provider=Microsoft.Jet.OLEDB.4.0;Data Source={adbfile};Jet OLEDB:Engine Type=5";

            Directory.CreateDirectory(path);

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

            var timestampTable = new ADOX.Table();

            timestampTable.Name = "Timestam";
            timestampTable.Columns.Append("v");

            cat.Tables.Append(timestampTable);

            var conn = cat.ActiveConnection as ADODB.Connection;
            if (conn != null)
            {
                try
                {
                    object recordsAffected;
                    conn.Execute("insert into Track (name) values (' ')", out recordsAffected);
                    conn.Execute($"insert into Timestam (v) values ('{DateTime.Now}')", out recordsAffected);
                }
                finally
                {
                    conn.Close();
                }
            }
        }

        public async Task<DateTime> GetTimestampAsync()
        {
            using (await _lock.LockAsync())
            {
                var command = new OleDbCommand("select v from Timestam", _conn);
                return DateTime.Parse((string)await command.ExecuteScalarAsync());
            }
        }

        public async Task<string> GetTrackAsync()
        {
            using (await _lock.LockAsync())
            {
                var command = new OleDbCommand("select name from Track", _conn);
                return (string)await command.ExecuteScalarAsync();
            }
        }

        public async Task SetTrackAsync(string track)
        {
            using (await _lock.LockAsync())
            {
                var oldTrack = await GetTrackAsync();
                if (track == oldTrack)
                {
                    return;
                }

                Console.WriteLine($"Track changed [{oldTrack} -> {track}]. Reset Database.");

                var command = new OleDbCommand("update Track set name = @name", _conn);
                command.Parameters.AddWithValue("@name", track);
                if (await command.ExecuteNonQueryAsync() != 1)
                {
                    throw new Exception("Track update failed.");
                }

                await ResetAsync();
            }
        }

        public async Task InsertAsync(DriverLaptime driverLaptime)
        {
            using (await _lock.LockAsync())
            {
                var command = new OleDbCommand("insert into DriverLaptime (car, driver, laptime) values (@car, @driver, @laptime)", _conn);
                command.Parameters.AddWithValue("@car", driverLaptime.Car);
                command.Parameters.AddWithValue("@driver", driverLaptime.Driver);
                command.Parameters.AddWithValue("@laptime", driverLaptime.Laptime.TotalMilliseconds);
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task UpdateAsync(DriverLaptime driverLaptime)
        {
            using (await _lock.LockAsync())
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
        }

        public async Task<DriverLaptime> GetAsync(string car, string driver)
        {
            using (await _lock.LockAsync())
            {
                var command = new OleDbCommand("select car, driver, laptime from DriverLaptime where car = @car and driver = @driver", _conn);
                command.Parameters.AddWithValue("@car", car);
                command.Parameters.AddWithValue("@driver", driver);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (!await reader.ReadAsync())
                    {
                        return null;
                    }

                    return new DriverLaptime(
                        await reader.GetFieldValueAsync<string>(0),
                        await reader.GetFieldValueAsync<string>(1),
                        reader.GetInt32(2)
                    );
                }
            }
        }

        public async Task<List<DriverLaptime>> ListAsync()
        {
            var result = new List<DriverLaptime>();

            using (await _lock.LockAsync())
            {
                var command = new OleDbCommand("select car, driver, laptime from DriverLaptime order by laptime asc", _conn);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new DriverLaptime(
                            await reader.GetFieldValueAsync<string>(0),
                            await reader.GetFieldValueAsync<string>(1),
                            reader.GetInt32(2)
                        ));
                    }
                }
            }
            return result;
        }

        public async Task<int> GetRankAsync(TimeSpan laptime)
        {
            using (await _lock.LockAsync())
            {
                var command = new OleDbCommand("select count(*) from DriverLaptime where laptime < @laptime", _conn);
                command.Parameters.AddWithValue("@laptime", laptime.TotalMilliseconds);
                return (int)await command.ExecuteScalarAsync() + 1;
            }
        }

        public async Task ResetAsync()
        {
            using (await _lock.LockAsync())
            {
                var command = new OleDbCommand("delete from DriverLaptime", _conn);
                await command.ExecuteNonQueryAsync();

                var command2 = new OleDbCommand("update Timestam set v = @v", _conn);
                command2.Parameters.AddWithValue("@v", DateTime.Now.ToString());
                await command2.ExecuteNonQueryAsync();
            }
        }

        public void Dispose()
        {
            _conn.Close();
        }
    }
}
