using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace acsRankingPlugin
{
    class ACSClient
    {
        private IPEndPoint _localEndpoint;
        private IPEndPoint _remoteEndpoint;
        private UdpClient _udpClient;

        public ACSClient(int localPort, int remotePort)
        {
            _localEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), localPort);
            _remoteEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), remotePort);

            _udpClient = new UdpClient(_localEndpoint);
        }

        protected byte[] Receive()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            return _udpClient.Receive(ref ep);
        }

        protected int Send(ACSProtocolWriter writer)
        {

            return _udpClient.Send(writer.Buffer, (int) writer.Length, _remoteEndpoint);
        }

        public void SetSessionInfo(byte sessionIndex, string sessionName, byte type, uint laps, TimeSpan time, TimeSpan waitTime)
        {
            var writer = new ACSProtocolWriter();

            writer.Write(ACSProtocol.ACSP_SET_SESSION_INFO);
            writer.Write(sessionIndex);
            writer.WriteStringW(sessionName);
            writer.Write(type);
            writer.Write(laps);
            writer.Write((uint)time.TotalSeconds);
            writer.Write((uint)waitTime.TotalSeconds);

            Send(writer);
        }

        public void GetSessionInfo(Int16 sessionIndex = -1 /* current index */)
        {
            var writer = new ACSProtocolWriter();

            writer.Write(ACSProtocol.ACSP_GET_SESSION_INFO);
            writer.Write(sessionIndex);

            Send(writer);
        }

        public void GetCarInfo(byte carId)
        {
            var writer = new ACSProtocolWriter();

            writer.Write(ACSProtocol.ACSP_GET_CAR_INFO);
            writer.Write(carId);

            Send(writer);
        }

        public void EnableRealtimeReport(TimeSpan interval)
        {
            var writer = new ACSProtocolWriter();

            writer.Write(ACSProtocol.ACSP_REALTIMEPOS_INTERVAL);
            writer.Write((UInt16)interval.TotalMilliseconds);

            Send(writer);
        }

        // 서버로 보낼 수 있는 메시지 길이는 한계가 있으므로 잘라서 보내야 한다.
        private List<string> SplitMessage(string message, int limit)
        {
            var lines = message.Split('\n', '\r');
            var result = new List<string>();
            foreach (var line in lines)
            {
                if (line.Length <= 0)
                {
                    continue;
                }

                if (line.Length > limit)
                {
                    result.Add(line.Substring(0, limit));
                }
                else
                {
                    result.Add(line);
                }
            }
            return result;
        }

        // 여러 줄로 된 메시지는 줄 단위로 쪼개서 보낸다
        public void SendChat(byte carId, string message)
        {
            var lines = SplitMessage(message, 62);
            foreach (var line in lines)
            {
                var writer = new ACSProtocolWriter();

                writer.Write(ACSProtocol.ACSP_SEND_CHAT);
                writer.Write(carId);
                writer.WriteStringW(line);

                Send(writer);
            }
        }

        public void BroadcastChat(string message)
        {
            var lines = SplitMessage(message, 62);
            foreach (var line in lines)
            {
                var writer = new ACSProtocolWriter();

                writer.Write(ACSProtocol.ACSP_BROADCAST_CHAT);
                writer.WriteStringW(line);

                Send(writer);
            }
        }

        public void Kick(byte userId)
        {
            var writer = new ACSProtocolWriter();

            writer.Write(ACSProtocol.ACSP_KICK_USER);
            writer.Write(userId);

            Send(writer);
        }

        public void SendAdminMessage(string message)
        {
            var writer = new ACSProtocolWriter();

            writer.Write(ACSProtocol.ACSP_ADMIN_COMMAND);
            writer.WriteStringW(message);

            Send(writer);
        }

        public delegate void OnErrorDelegate(byte packetId, string message);
        public OnErrorDelegate OnError;

        public delegate void OnChatDelegate(byte packetId, ref ChatEvent eventData);
        public OnChatDelegate OnChat;

        public delegate void OnClientLoadedDelegate(byte packetId, byte carId);
        public OnClientLoadedDelegate OnClientLoaded;

        public delegate void OnVersionDelegate(byte packetId, byte protocolVersion);
        public OnVersionDelegate OnVersion;

        public delegate void OnNewSessionDelegate(byte packetId, ref SessionInfoEvent eventData);
        public OnNewSessionDelegate OnNewSession;

        public delegate void OnSessionInfoDelegate(byte packetId, ref SessionInfoEvent eventData);
        public OnSessionInfoDelegate OnSessionInfo;

        public delegate void OnEndSessionDelegate(byte packetId, string reportFile);
        public OnEndSessionDelegate OnEndSession;

        public delegate void OnClientEventDelegate(byte packetId, ref ClientEventEvent eventData);
        public OnClientEventDelegate OnClientEvent;

        public delegate void OnCarInfoDelegate(byte packetId, ref CarInfoEvent eventData);
        public OnCarInfoDelegate OnCarInfo;

        public delegate void OnCarUpdateDelegate(byte packetId, ref CarUpdateEvent eventData);
        public OnCarUpdateDelegate OnCarUpdate;

        public delegate void OnConnectionEventDelegate(byte packetId, ref ConnectionEvent eventData);
        public OnConnectionEventDelegate OnNewConnection;
        public OnConnectionEventDelegate OnConnectionClosed;

        public delegate void OnLapCompletedDelegate(byte packetId, ref LapCompletedEvent eventData);
        public OnLapCompletedDelegate OnLapCompleted;

        public void DispatchMessages()
        {
            while (true)
            {
                var acsReader = new ACSProtocolReader(Receive());
                var packetId = acsReader.ReadByte();

                switch (packetId)
                {
                    case ACSProtocol.ACSP_ERROR:
                        {
                            var message = acsReader.ReadStringW();
                            OnError?.Invoke(packetId, message);
                        }
                        break;
                    case ACSProtocol.ACSP_CHAT:
                        {
                            var eventData = new ChatEvent(acsReader);
                            OnChat?.Invoke(packetId, ref eventData);
                        }
                        break;
                    case ACSProtocol.ACSP_CLIENT_LOADED:
                        {
                            var carId = acsReader.ReadByte();
                            OnClientLoaded?.Invoke(packetId, carId);
                        }
                        break;

                    case ACSProtocol.ACSP_VERSION:
                        {
                            var protocolVersion = acsReader.ReadByte();
                            OnVersion?.Invoke(packetId, protocolVersion);
                        }
                        break;
                    case ACSProtocol.ACSP_NEW_SESSION:
                        {
                            var eventData = new SessionInfoEvent(acsReader);
                            OnNewSession?.Invoke(packetId, ref eventData);
                        }
                        break;
                    case ACSProtocol.ACSP_SESSION_INFO:
                        {
                            var eventData = new SessionInfoEvent(acsReader);
                            OnSessionInfo?.Invoke(packetId, ref eventData);
                        }
                        break;
                    case ACSProtocol.ACSP_END_SESSION:
                        {
                            var reportFile = acsReader.ReadStringW();
                            OnEndSession?.Invoke(packetId, reportFile);
                        }
                        break;
                    case ACSProtocol.ACSP_CLIENT_EVENT:
                        {
                            var eventData = new ClientEventEvent(acsReader);
                            OnClientEvent?.Invoke(packetId, ref eventData);
                        }
                        break;
                    case ACSProtocol.ACSP_CAR_INFO:
                        {
                            var eventData = new CarInfoEvent(acsReader);
                            OnCarInfo?.Invoke(packetId, ref eventData);
                        }
                        break;
                    case ACSProtocol.ACSP_CAR_UPDATE:
                        {
                            var eventData = new CarUpdateEvent(acsReader);
                            OnCarUpdate?.Invoke(packetId, ref eventData);
                        }
                        break;
                    case ACSProtocol.ACSP_NEW_CONNECTION:
                        {
                            var eventData = new ConnectionEvent(acsReader);
                            OnNewConnection?.Invoke(packetId, ref eventData);
                        }
                        break;
                    case ACSProtocol.ACSP_CONNECTION_CLOSED:
                        {
                            var eventData = new ConnectionEvent(acsReader);
                            OnConnectionClosed?.Invoke(packetId, ref eventData);
                        }
                        break;
                    case ACSProtocol.ACSP_LAP_COMPLETED:
                        {
                            var eventData = new LapCompletedEvent(acsReader);
                            OnLapCompleted?.Invoke(packetId, ref eventData);
                        }
                        break;

                }
            }
        }
    }
}
