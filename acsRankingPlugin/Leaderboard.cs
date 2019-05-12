﻿using NeoSmart.AsyncLock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace acsRankingPlugin
{
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

    class Record
    {
        public int Rank { get; }
        public DriverLaptime DriverLaptime { get; }

        public Record(int rank, DriverLaptime driverLaptime)
        {
            Rank = rank;
            DriverLaptime = driverLaptime;
        }
    }

    class Leaderboard
    {
        // 채팅창의 한 화면에 출력할 수 있는 레코드 수
        private const int CHAT_WINDOW_RECORD_COUNT = 14;

        private IStorage _storage;

        public Leaderboard(IStorage storage)
        {
            _storage = storage;
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

        /**
         * DriverLaptime List에 ranK를 붙여서 Record List를 만든다.
         */
        protected List<Record> MakeRecords(List<DriverLaptime> driverLaptimes)
        {
            var result = new List<Record>();
            int rank = 1;
            for (var i = 0; i < driverLaptimes.Count; i++)
            {
                var driverLaptime = driverLaptimes[i];
                if (i > 0 && driverLaptime.Laptime != driverLaptimes[i - 1].Laptime)
                {
                    rank = i + 1;
                }
                result.Add(new Record(rank, driverLaptime));
            }
            return result;
        }

        /**
         * 상위 maxRow위까지 보여주는 리더보드 출력
         */
        public async Task<string> GenerateRankTableAsync(int maxRow = int.MaxValue)
        {
            var sb = new StringBuilder();
            PrintRankTableHeader(sb, await _storage.GetTimestampAsync());

            var records = MakeRecords(await _storage.ListAsync());
            if (records.Count > 0)
            {
                for (var i = 0; i < records.Count; i++)
                {
                    if (i == maxRow - 1 && i != records.Count - 1)
                    {
                        sb.AppendLine($"   ...  {records.Count - i}명이 더 있습니다 ...");
                        break;
                    }
                    var record = records[i];
                    PrintRankTableRecord(sb, record);
                }
            }
            else
            {
                sb.AppendLine($"   기록이 없습니다.");
            }
            PrintRankTableBar(sb);

            return sb.ToString();
        }

        public Task<string> GenerateTopRankTableAsync()
        {
            return GenerateRankTableAsync(CHAT_WINDOW_RECORD_COUNT);
        }

        /**
         * 내 순위를 가운데 두는 리더보드를 출력한다.
         * 출력하는 레코드 갯수가 maxRow가 되도록 위, 아래를 자른다.
         * 잘리더라도 1위와 꼴찌는 항상 보이도록 처리한다.
         */
        public async Task<string> GenerateMyRankTableAsync(string car, string driver, int maxRow = CHAT_WINDOW_RECORD_COUNT)
        {
            var driverLaptime = await _storage.GetAsync(car, driver);
            if (driverLaptime == null || driverLaptime.Laptime == TimeSpan.Zero)
            {
                return await GenerateTopRankTableAsync();
            }

            var myRank = await _storage.GetRankAsync(driverLaptime.Laptime);

            var sb = new StringBuilder();
            PrintRankTableHeader(sb, await _storage.GetTimestampAsync());

            var records = MakeRecords(
                (await _storage.ListAsync()).FindAll(v => v.Laptime != TimeSpan.Zero));

            if (records.Count > 0)
            {
                // 순위 표시할 때 다음과 같이 하기위해 위,아래를 잘라내는 로직이 필요하다
                // ------------   -- records.Count = 14, maxRow = 7, cutCount = 7
                // 1. aaa         -- cutTop = 2
                // ...
                // 5. bbb         -- startIndex = 4
                // 6. ccc         -- 내 위치는 가운데 (myRank = 6, myPrintOrder = 4)
                // 7. ddd         -- endIndex = 7
                // ...
                // 14. eee        -- cutBottom = 5
                var cutCount = Math.Max(0, records.Count - maxRow);
                var myPrintOrder = (maxRow + 1) / 2; // 내 순위가 출력될 줄번호
                var cutTop = Math.Max(0, myRank - myPrintOrder);
                var cutBottom = cutCount - cutTop;

                // 본인 순위가 바닥권이면 cutBottom이 마이너스가 나오고 상위가 많이 잘린다.
                if (cutBottom < 0)
                {
                    cutTop += cutBottom;
                    cutBottom = 0;
                }

                //Console.WriteLine($"cutCount:{cutCount}, myPrintOrder:{myPrintOrder}, cutTop:{cutTop}, cutBottom:{cutBottom}");

                if (cutTop > 0)
                {
                    PrintRankTableRecord(sb, records[0]);
                    sb.AppendLine("   ...");
                }

                var startIndex = cutTop > 0 ? cutTop + 2 : 0;
                var endIndex = cutBottom > 0 ? records.Count - cutBottom - 2 : records.Count;
                //Console.WriteLine($"startIndex:{startIndex}, endIndex:{endIndex}");
                for (var i = startIndex; i < endIndex; i++)
                {
                    PrintRankTableRecord(sb, records[i]);
                }

                if (cutBottom > 0)
                {
                    sb.AppendLine("   ...");
                    PrintRankTableRecord(sb, records.Last());
                }
            }
            else
            {
                sb.AppendLine("   기록이 없습니다.");
            }
            PrintRankTableBar(sb);

            return sb.ToString();
        }

        protected void PrintRankTableHeader(StringBuilder sb, DateTime timestamp)
        {
            sb.AppendLine($"Leader Board ({timestamp} ~ Now)");
            PrintRankTableBar(sb);
            sb.AppendLine("순위   시간        이름");
            PrintRankTableBar(sb);
        }

        protected void PrintRankTableBar(StringBuilder sb)
        {
            sb.AppendLine("=================================");
        }

        protected void PrintRankTableRecord(StringBuilder sb, Record record)
        {
            sb.AppendLine(string.Format("{0,4}   {1,-9}  {2}",
                record.Rank, record.DriverLaptime.Laptime.LaptimeFormat(), record.DriverLaptime.Driver));
        }
    }
}
