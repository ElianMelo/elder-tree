using System;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Transporting;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace ElderTree.Net
{
    /// <summary>
    /// Plugs FishNet into a Multiplayer Services session: when the session brings its network up, the Relay
    /// allocation goes to the <see cref="RelayTransport"/> and FishNet starts as host (server + local client) or client;
    /// when the session leaves, FishNet stops.
    /// </summary>
    public sealed class CoopNetworkHandler : INetworkHandler
    {
        readonly NetworkManager nm;
        readonly RelayTransport transport;
        NetworkRole role;

        public CoopNetworkHandler(NetworkManager nm, RelayTransport transport)
        {
            this.nm = nm;
            this.transport = transport;
        }

        public Task StartAsync(NetworkConfiguration configuration) => StartAsync(configuration, CancellationToken.None);

        public async Task StartAsync(NetworkConfiguration configuration, CancellationToken cancellationToken)
        {
            role = configuration.Role;
            if (configuration.Type != NetworkType.Relay)
                throw new InvalidOperationException("Co-op runs over Relay only.");

            if (role == NetworkRole.Client)
            {
                // a client's own Relay allocation arrives in RelayServerData (RelayClientData is only filled for Netcode for Entities)
                transport.ConfigureClient(configuration.RelayServerData);
                if (!nm.ClientManager.StartConnection()) throw new InvalidOperationException("The client could not start.");
            }
            else
            {
                transport.ConfigureHost(configuration.RelayServerData);
                if (!nm.ServerManager.StartConnection()) throw new InvalidOperationException("The host could not start.");
                if (!nm.ClientManager.StartConnection()) throw new InvalidOperationException("The host's client could not start.");
            }

            // the session considers the network up once this returns: wait for the connection itself
            float deadline = Time.realtimeSinceStartup + 20f;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cs = transport.GetConnectionState(false);
                if (cs == LocalConnectionState.Started) break;
                if (cs == LocalConnectionState.Stopped) throw new InvalidOperationException("The connection through Relay failed.");
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Timed out connecting through Relay.");
                await Task.Yield();
            }
        }

        public Task StopAsync()
        {
            if (nm != null)
            {
                if (nm.ClientManager.Started) nm.ClientManager.StopConnection();
                if (nm.ServerManager.Started) nm.ServerManager.StopConnection(true);
            }
            // whatever FishNet thinks, leave the transport fully stopped (a failed start can leave it half way)
            if (transport != null) transport.Shutdown();
            return Task.CompletedTask;
        }
    }
}
