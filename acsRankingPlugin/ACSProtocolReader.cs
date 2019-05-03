using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace acsRankingPlugin
{
    struct Vector3f
    {
        public readonly float X, Y, Z;

        public Vector3f(ACSProtocolReader reader)
        {
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
            Z = reader.ReadSingle();
        }

        public override string ToString()
        {
            return "[" + X.ToString() + " , " + Y.ToString() + " , " + Z.ToString() + "]";
        }
    }

    struct ChatEvent
    {
        public readonly byte CarId;
        public readonly string Message;

        public ChatEvent(ACSProtocolReader reader)
        {
            CarId = reader.ReadByte();
            Message = reader.ReadStringW();
        }
    }

    struct SessionInfoEvent
    {
        public readonly byte Version; // UDP Plugin protocol version, in case you miss the first ACSP_VERSION message sent by the server at startup
        public readonly byte SessionIndex; // The index of the session in the message
        public readonly byte CurrentSessionIndex; // The index of the current session in the server
        public readonly byte SessionCount; // The number of sessions in the server

        public readonly string ServerName;
        public readonly string Track;
        public readonly string TrackConfig;
        public readonly string Name;
        public readonly byte Type;
        public readonly TimeSpan Time;
        public readonly ushort Laps;
        public readonly TimeSpan WaitTime;
        public readonly byte AmbientTemp;
        public readonly byte RoadTemp;
        public readonly string WeatherGraphics;
        public readonly TimeSpan Elapsed; // Elapsed time from the start(this might be negative for races with WaitTime)

        public SessionInfoEvent(ACSProtocolReader reader)
        {
            Version = reader.ReadByte();
            SessionIndex = reader.ReadByte();
            CurrentSessionIndex = reader.ReadByte();
            SessionCount = reader.ReadByte();

            ServerName = reader.ReadStringW();
            Track = reader.ReadString();
            TrackConfig = reader.ReadString();
            Name = reader.ReadString();
            Type = reader.ReadByte();
            Time = TimeSpan.FromSeconds(reader.ReadUInt16());
            Laps = reader.ReadUInt16();
            WaitTime = TimeSpan.FromSeconds(reader.ReadUInt16());
            AmbientTemp = reader.ReadByte();
            RoadTemp = reader.ReadByte();
            WeatherGraphics = reader.ReadString();
            Elapsed = TimeSpan.FromMilliseconds(reader.ReadInt32());
        }
    }

    struct ClientEventEvent
    {
        public readonly byte EventType;
        public readonly byte CarId;
        public readonly byte OtherCarId; // ACSP_CE_COLLISION_WIT_CAR only
        public readonly float Speed; // Impact speed
        public readonly Vector3f WorldPosition;
        public readonly Vector3f RelationalPosition;

        public ClientEventEvent(ACSProtocolReader reader)
        {
            EventType = reader.ReadByte();
            CarId = reader.ReadByte();
            if (EventType == ACSProtocol.ACSP_CE_COLLISION_WITH_CAR)
            {
                OtherCarId = reader.ReadByte();
            }
            else
            {
                OtherCarId = 255;
            }
            Speed = reader.ReadSingle();
            WorldPosition = new Vector3f(reader);
            RelationalPosition = new Vector3f(reader);
        }
    }

    struct CarInfoEvent
    {
        public readonly byte CarId;
        public readonly bool IsConnected;
        public readonly string Model;
        public readonly string Skin;
        public readonly string DriverName;
        public readonly string DriverTeam;
        public readonly string DriverGuid;

        public CarInfoEvent(ACSProtocolReader reader)
        {
            CarId = reader.ReadByte();
            IsConnected = reader.ReadByte() != 0;
            Model = reader.ReadStringW();
            Skin = reader.ReadStringW();
            DriverName = reader.ReadStringW();
            DriverTeam = reader.ReadStringW();
            DriverGuid = reader.ReadStringW();
        }
    }

    struct CarUpdateEvent
    {
        public readonly byte CarId;
        public readonly Vector3f Position;
        public readonly Vector3f Velocity;
        public readonly byte Gear;
        public readonly ushort Rpm;
        public readonly float NormalizedSplinePosition;

        public CarUpdateEvent(ACSProtocolReader reader)
        {
            CarId = reader.ReadByte();
            Position = new Vector3f(reader);
            Velocity = new Vector3f(reader);
            Gear = reader.ReadByte();
            Rpm = reader.ReadUInt16();
            NormalizedSplinePosition = reader.ReadSingle();
        }
    }

    struct ConnectionEvent
    {
        public readonly string DriverName;
        public readonly string DriverGuid;
        public readonly byte CarId;
        public readonly string CarModel;
        public readonly string CarSkin;

        public ConnectionEvent(ACSProtocolReader reader)
        {
            DriverName = reader.ReadStringW();
            DriverGuid = reader.ReadStringW();
            CarId = reader.ReadByte();
            CarModel = reader.ReadString();
            CarSkin = reader.ReadString();
        }
    }

    
    struct LapCompletedEvent
    {
        public readonly byte CarId;
        public readonly TimeSpan LapTime;
        public readonly byte Cuts;
        public readonly byte CarsCount;
        public readonly List<LeaderboardEntry> Leaderboard;
        public readonly float GripLevel;

        public LapCompletedEvent(ACSProtocolReader reader)
        {
            CarId = reader.ReadByte();
            LapTime = TimeSpan.FromMilliseconds(reader.ReadUInt32());
            Cuts = reader.ReadByte();
            CarsCount = reader.ReadByte();
            Leaderboard = new List<LeaderboardEntry>();
            for (int i = 0; i < CarsCount; i++)
            {
                var entry = new LeaderboardEntry(reader);
                Leaderboard.Add(entry);
            }
            GripLevel = reader.ReadSingle();
        }

        public struct LeaderboardEntry
        {
            public readonly byte CarId;
            public readonly TimeSpan Time;
            public readonly ushort Laps;
            public readonly bool HasCompleted;

            public LeaderboardEntry(ACSProtocolReader reader)
            {
                CarId = reader.ReadByte();
                Time = TimeSpan.FromMilliseconds(reader.ReadUInt32());
                Laps = reader.ReadUInt16();
                HasCompleted = reader.ReadByte() != 0;
            }
        }
    }

    class ACSProtocolReader
    {
        private BinaryReader _binaryReader;

        delegate void test();

        public ACSProtocolReader(byte[] bytes)
        {
            _binaryReader = new BinaryReader(new MemoryStream(bytes));
        }

        public byte ReadByte()
        {
            return _binaryReader.ReadByte();
        }

        public ushort ReadUInt16()
        {
            return _binaryReader.ReadUInt16();
        }

        public int ReadInt32()
        {
            return _binaryReader.ReadInt32();
        }

        public uint ReadUInt32()
        {
            return _binaryReader.ReadUInt32();
        }

        public float ReadSingle()
        {
            return _binaryReader.ReadSingle();
        }

        public string ReadString()
        {
            var length = _binaryReader.ReadByte();
            return new string(_binaryReader.ReadChars(length));

        }

        public string ReadStringW()
        {
            var length = _binaryReader.ReadByte();
            return Encoding.UTF32.GetString(_binaryReader.ReadBytes(length * 4));

        }
    }
}
