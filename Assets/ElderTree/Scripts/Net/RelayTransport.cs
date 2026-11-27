using System;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Transporting;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using UtpConnection = Unity.Networking.Transport.NetworkConnection;

namespace ElderTree.Net
{
    /// <summary>
    /// A FishNet transport over Unity Transport, routed through Unity Relay (no port forwarding, no exposed IPs).
    /// The session layer hands it the Relay allocation (<see cref="ConfigureHost"/> / <see cref="ConfigureClient"/>)
    /// before FishNet starts it. The host's own client never touches the wire: it is a loopback inside this transport.
    /// Two channels: reliable (ReliableSequenced pipeline) and unreliable. Payloads stay under <see cref="Mtu"/>;
    /// FishNet splits anything larger itself.
    /// </summary>
    [DisallowMultipleComponent]
    public class RelayTransport : Transport
    {
        /// <summary>Largest payload handed to Unity Transport (Relay + DTLS + pipeline headers fit under its 1400).</summary>
        public const int Mtu = 1100;

        [Tooltip("Players that may be connected at once (the host's own client included).")]
        public int maximumClients = 4;
        [Tooltip("Seconds without a word from the other side before a connection is dropped.")]
        public int disconnectTimeoutSeconds = 20;

        RelayServerData serverData, clientData;
        bool hasServerData, hasClientData;

        NetworkDriver serverDriver, clientDriver;
        NetworkPipeline sReliable, sUnreliable, cReliable, cUnreliable;
        LocalConnectionState serverState = LocalConnectionState.Stopped, clientState = LocalConnectionState.Stopped;
        bool listening;
        UtpConnection toServer;

        readonly Dictionary<int, UtpConnection> conns = new();
        readonly Dictionary<UtpConnection, int> ids = new();
        int nextId = 1;

        // the host's own client: a loopback, no driver
        bool localClient;
        int localId = -1;
        bool localConnectPending;
        readonly Queue<Packet> toLocalClient = new(), fromLocalClient = new();

        // reliable sends the pipeline had no room for this tick (its window is full): retried next tick
        readonly Queue<Pending> serverBacklog = new(), clientBacklog = new();

        readonly byte[] receiveBuffer = new byte[64 * 1024];

        struct Packet { public byte channel; public byte[] data; public int length; }
        struct Pending { public int id; public byte channel; public byte[] data; public int length; }

        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;
        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;

        // ================================================================== configuration
        /// <summary>The Relay allocation this machine hosts on (and its own client data, unused: the host client is a loopback).</summary>
        public void ConfigureHost(RelayServerData server)
        {
            serverData = server;
            hasServerData = true;
        }

        /// <summary>The Relay allocation this machine joins through.</summary>
        public void ConfigureClient(RelayServerData client)
        {
            clientData = client;
            hasClientData = true;
        }

        public override void Initialize(NetworkManager networkManager, int transportIndex) => base.Initialize(networkManager, transportIndex);

        void OnDestroy() => Shutdown();

        // ================================================================== states
        public override string GetConnectionAddress(int connectionId) => connectionId == localId ? "local" : "relay";
        public override LocalConnectionState GetConnectionState(bool server) => server ? serverState : clientState;

        public override RemoteConnectionState GetConnectionState(int connectionId) =>
            connectionId == localId && localClient ? RemoteConnectionState.Started
            : conns.ContainsKey(connectionId) ? RemoteConnectionState.Started : RemoteConnectionState.Stopped;

        public override void HandleClientConnectionState(ClientConnectionStateArgs args) => OnClientConnectionState?.Invoke(args);
        public override void HandleServerConnectionState(ServerConnectionStateArgs args) => OnServerConnectionState?.Invoke(args);
        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs args) => OnRemoteConnectionState?.Invoke(args);
        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs args) => OnClientReceivedData?.Invoke(args);
        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs args) => OnServerReceivedData?.Invoke(args);

        void SetServerState(LocalConnectionState s)
        {
            if (serverState == s) return;
            serverState = s;
            HandleServerConnectionState(new ServerConnectionStateArgs(s, Index));
        }

        void SetClientState(LocalConnectionState s)
        {
            if (clientState == s) return;
            clientState = s;
            HandleClientConnectionState(new ClientConnectionStateArgs(s, Index));
        }

        void RemoteState(RemoteConnectionState s, int id) => HandleRemoteConnectionState(new RemoteConnectionStateArgs(s, id, Index));

        public override bool IsLocalTransport(int connectionId) => connectionId == localId;
        public override int GetMaximumClients() => maximumClients;
        public override void SetMaximumClients(int value) => maximumClients = value;
        public override float GetTimeout(bool asServer) => disconnectTimeoutSeconds;
        public override int GetMTU(byte channel) => Mtu;

        // ================================================================== start / stop
        public override bool StartConnection(bool server) => server ? StartServer() : StartClient();

        public override bool StopConnection(bool server) => server ? StopServer() : StopClient();

        public override bool StopConnection(int connectionId, bool immediately)
        {
            if (connectionId == localId && localClient) { StopClient(); return true; }
            if (!conns.TryGetValue(connectionId, out var c)) return false;
            if (serverDriver.IsCreated) serverDriver.Disconnect(c);
            conns.Remove(connectionId);
            ids.Remove(c);
            RemoteState(RemoteConnectionState.Stopped, connectionId);
            return true;
        }

        public override void Shutdown()
        {
            StopClient();
            StopServer();
        }

        NetworkSettings Settings(ref RelayServerData relay)
        {
            var s = new NetworkSettings(Allocator.Temp);
            s.WithRelayParameters(ref relay);
            s.WithNetworkConfigParameters(connectTimeoutMS: 1000, maxConnectAttempts: 30,
                disconnectTimeoutMS: disconnectTimeoutSeconds * 1000, heartbeatTimeoutMS: 500,
                sendQueueCapacity: 1024, receiveQueueCapacity: 1024);
            s.WithReliableStageParameters(windowSize: 64);
            return s;
        }

        static NetworkDriver CreateDriver(ref RelayServerData relay, NetworkSettings settings) =>
            relay.IsWebSocket != 0
                ? NetworkDriver.Create(new WebSocketNetworkInterface(), settings)
                : NetworkDriver.Create(new UDPNetworkInterface(), settings);

        bool StartServer()
        {
            if (serverState != LocalConnectionState.Stopped) return false;
            if (!hasServerData) { Debug.LogError("[Coop] RelayTransport: no Relay allocation to host on."); return false; }
            SetServerState(LocalConnectionState.Starting);
            try
            {
                var settings = Settings(ref serverData);
                serverDriver = CreateDriver(ref serverData, settings);
                sReliable = serverDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                sUnreliable = NetworkPipeline.Null;
                if (serverDriver.Bind(NetworkEndpoint.AnyIpv4) != 0) throw new InvalidOperationException("could not bind to the Relay server");
            }
            catch (Exception e)
            {
                Debug.LogError("[Coop] RelayTransport: the host could not start: " + e.Message);
                DisposeServer();
                SetServerState(LocalConnectionState.Stopped);
                return false;
            }
            listening = false;
            return true;
        }

        bool StopServer()
        {
            if (serverState == LocalConnectionState.Stopped) return false;
            SetServerState(LocalConnectionState.Stopping);
            foreach (var kv in conns)
            {
                if (serverDriver.IsCreated) serverDriver.Disconnect(kv.Value);
                RemoteState(RemoteConnectionState.Stopped, kv.Key);
            }
            conns.Clear();
            ids.Clear();
            serverBacklog.Clear();
            if (localClient)
            {
                RemoteState(RemoteConnectionState.Stopped, localId);
                localClient = false;
                toLocalClient.Clear();
                fromLocalClient.Clear();
                SetClientState(LocalConnectionState.Stopped);
            }
            if (serverDriver.IsCreated) serverDriver.ScheduleFlushSend(default).Complete();
            DisposeServer();
            SetServerState(LocalConnectionState.Stopped);
            return true;
        }

        void DisposeServer()
        {
            if (serverDriver.IsCreated) serverDriver.Dispose();
            serverDriver = default;
            listening = false;
        }

        bool StartClient()
        {
            if (clientState != LocalConnectionState.Stopped) return false;
            // the host's own client: a loopback into the server, connected on the next server tick
            if (serverState == LocalConnectionState.Started || serverState == LocalConnectionState.Starting)
            {
                localClient = true;
                localId = nextId++;
                localConnectPending = true;
                SetClientState(LocalConnectionState.Starting);
                return true;
            }
            if (!hasClientData) { Debug.LogError("[Coop] RelayTransport: no Relay allocation to join through."); return false; }
            SetClientState(LocalConnectionState.Starting);
            try
            {
                var settings = Settings(ref clientData);
                clientDriver = CreateDriver(ref clientData, settings);
                cReliable = clientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                cUnreliable = NetworkPipeline.Null;
                if (clientDriver.Bind(NetworkEndpoint.AnyIpv4) != 0) throw new InvalidOperationException("could not bind");
                toServer = clientDriver.Connect(clientData.Endpoint);
            }
            catch (Exception e)
            {
                Debug.LogError("[Coop] RelayTransport: the client could not start: " + e.Message);
                DisposeClient();
                SetClientState(LocalConnectionState.Stopped);
                return false;
            }
            return true;
        }

        bool StopClient()
        {
            if (clientState == LocalConnectionState.Stopped) return false;
            SetClientState(LocalConnectionState.Stopping);
            if (localClient)
            {
                localClient = false;
                localConnectPending = false;
                toLocalClient.Clear();
                fromLocalClient.Clear();
                if (serverState == LocalConnectionState.Started) RemoteState(RemoteConnectionState.Stopped, localId);
                localId = -1;
            }
            else if (clientDriver.IsCreated)
            {
                if (toServer != default) clientDriver.Disconnect(toServer);
                clientDriver.ScheduleFlushSend(default).Complete();
            }
            clientBacklog.Clear();
            DisposeClient();
            SetClientState(LocalConnectionState.Stopped);
            return true;
        }

        void DisposeClient()
        {
            if (clientDriver.IsCreated) clientDriver.Dispose();
            clientDriver = default;
            toServer = default;
        }

        // ================================================================== sending
        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (clientState != LocalConnectionState.Started) return;
            if (localClient)
            {
                fromLocalClient.Enqueue(Copy(channelId, segment));
                return;
            }
            Send(clientDriver, channelId == (byte)Channel.Reliable ? cReliable : cUnreliable, toServer, channelId, segment, clientBacklog, 0);
        }

        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (serverState != LocalConnectionState.Started) return;
            if (connectionId == localId && localClient)
            {
                toLocalClient.Enqueue(Copy(channelId, segment));
                return;
            }
            if (!conns.TryGetValue(connectionId, out var c)) return;
            Send(serverDriver, channelId == (byte)Channel.Reliable ? sReliable : sUnreliable, c, channelId, segment, serverBacklog, connectionId);
        }

        static Packet Copy(byte channel, ArraySegment<byte> seg)
        {
            var data = new byte[seg.Count];
            Buffer.BlockCopy(seg.Array, seg.Offset, data, 0, seg.Count);
            return new Packet { channel = channel, data = data, length = seg.Count };
        }

        /// <summary>Hand a payload to the driver. A reliable payload the pipeline can't take right now waits in the backlog.</summary>
        static void Send(NetworkDriver driver, NetworkPipeline pipe, UtpConnection to, byte channel, ArraySegment<byte> seg, Queue<Pending> backlog, int id)
        {
            if (!driver.IsCreated) return;
            bool reliable = channel == (byte)Channel.Reliable;
            if (reliable && backlog.Count > 0)
            {
                // keep reliable order: behind whatever is already waiting
                var copy = Copy(channel, seg);
                backlog.Enqueue(new Pending { id = id, channel = channel, data = copy.data, length = copy.length });
                return;
            }
            if (!TrySend(driver, pipe, to, seg.Array, seg.Offset, seg.Count) && reliable)
            {
                var copy = Copy(channel, seg);
                backlog.Enqueue(new Pending { id = id, channel = channel, data = copy.data, length = copy.length });
            }
        }

        static bool TrySend(NetworkDriver driver, NetworkPipeline pipe, UtpConnection to, byte[] data, int offset, int length)
        {
            int r = driver.BeginSend(pipe, to, out var writer);
            if (r < 0) return false;
            writer.WriteBytes(new Span<byte>(data, offset, length));
            if (writer.HasFailedWrites) { driver.AbortSend(writer); return false; }
            return driver.EndSend(writer) >= 0;
        }

        void FlushBacklog(NetworkDriver driver, NetworkPipeline pipe, Queue<Pending> backlog, bool asServer)
        {
            while (backlog.Count > 0)
            {
                var p = backlog.Peek();
                UtpConnection to;
                if (asServer) { if (!conns.TryGetValue(p.id, out to)) { backlog.Dequeue(); continue; } }
                else to = toServer;
                if (!TrySend(driver, pipe, to, p.data, 0, p.length)) break;
                backlog.Dequeue();
            }
        }

        // ================================================================== iterating
        public override void IterateIncoming(bool asServer)
        {
            if (asServer) IterateServerIncoming();
            else IterateClientIncoming();
        }

        void IterateServerIncoming()
        {
            if (serverState == LocalConnectionState.Stopped || !serverDriver.IsCreated) return;
            serverDriver.ScheduleUpdate().Complete();

            if (!listening)
            {
                if (!serverDriver.Bound) return;
                if (serverDriver.Listen() != 0)
                {
                    Debug.LogError("[Coop] RelayTransport: the Relay server refused to listen.");
                    StopServer();
                    return;
                }
                listening = true;
                SetServerState(LocalConnectionState.Started);
            }

            if (localConnectPending && localClient)
            {
                localConnectPending = false;
                RemoteState(RemoteConnectionState.Started, localId);
                SetClientState(LocalConnectionState.Started);
            }

            UtpConnection c;
            while ((c = serverDriver.Accept()) != default)
            {
                if (conns.Count + (localClient ? 1 : 0) >= maximumClients) { serverDriver.Disconnect(c); continue; }
                int id = nextId++;
                conns[id] = c;
                ids[c] = id;
                RemoteState(RemoteConnectionState.Started, id);
            }

            NetworkEvent.Type ev;
            while ((ev = serverDriver.PopEvent(out c, out var reader, out var pipe)) != NetworkEvent.Type.Empty)
            {
                if (!ids.TryGetValue(c, out int id)) continue;
                switch (ev)
                {
                    case NetworkEvent.Type.Data:
                    {
                        int n = reader.Length;
                        if (n > receiveBuffer.Length) continue;
                        reader.ReadBytes(new Span<byte>(receiveBuffer, 0, n));
                        var ch = pipe == sReliable ? Channel.Reliable : Channel.Unreliable;
                        HandleServerReceivedDataArgs(new ServerReceivedDataArgs(new ArraySegment<byte>(receiveBuffer, 0, n), ch, id, Index));
                        break;
                    }
                    case NetworkEvent.Type.Disconnect:
                        conns.Remove(id);
                        ids.Remove(c);
                        RemoteState(RemoteConnectionState.Stopped, id);
                        break;
                }
            }

            // what the host's own client sent this tick
            while (fromLocalClient.Count > 0 && localClient)
            {
                var p = fromLocalClient.Dequeue();
                HandleServerReceivedDataArgs(new ServerReceivedDataArgs(new ArraySegment<byte>(p.data, 0, p.length), (Channel)p.channel, localId, Index));
            }
        }

        void IterateClientIncoming()
        {
            if (clientState == LocalConnectionState.Stopped) return;
            if (localClient)
            {
                while (toLocalClient.Count > 0)
                {
                    var p = toLocalClient.Dequeue();
                    HandleClientReceivedDataArgs(new ClientReceivedDataArgs(new ArraySegment<byte>(p.data, 0, p.length), (Channel)p.channel, Index));
                }
                return;
            }
            if (!clientDriver.IsCreated) return;
            clientDriver.ScheduleUpdate().Complete();
            NetworkEvent.Type ev;
            while ((ev = clientDriver.PopEvent(out var c, out var reader, out var pipe)) != NetworkEvent.Type.Empty)
            {
                switch (ev)
                {
                    case NetworkEvent.Type.Connect:
                        SetClientState(LocalConnectionState.Started);
                        break;
                    case NetworkEvent.Type.Data:
                    {
                        int n = reader.Length;
                        if (n > receiveBuffer.Length) continue;
                        reader.ReadBytes(new Span<byte>(receiveBuffer, 0, n));
                        var ch = pipe == cReliable ? Channel.Reliable : Channel.Unreliable;
                        HandleClientReceivedDataArgs(new ClientReceivedDataArgs(new ArraySegment<byte>(receiveBuffer, 0, n), ch, Index));
                        break;
                    }
                    case NetworkEvent.Type.Disconnect:
                        toServer = default;
                        StopClient();
                        return;
                }
            }
        }

        public override void IterateOutgoing(bool asServer)
        {
            if (asServer)
            {
                if (!serverDriver.IsCreated) return;
                FlushBacklog(serverDriver, sReliable, serverBacklog, true);
                serverDriver.ScheduleFlushSend(default).Complete();
            }
            else
            {
                if (localClient || !clientDriver.IsCreated) return;
                FlushBacklog(clientDriver, cReliable, clientBacklog, false);
                clientDriver.ScheduleFlushSend(default).Complete();
            }
        }
    }
}
