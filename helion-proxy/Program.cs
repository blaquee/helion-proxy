using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Wintellect.Threading.AsyncProgModel;
using System.Configuration;
using System.Net;

namespace helion_proxy
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Wokf AntiHaxxing Proxy Server by Hunter and Thanatos. Version 1");

            var serverState = new ServerState();

            // a new AsyncEnumerator is constructed for the sake of a new thread of execution
            var ae = new AsyncEnumerator();
            var r = ae.BeginExecute(Server(ae, serverState), ae.EndExecute);
            r.AsyncWaitHandle.WaitOne();
        }

        class ClientState
        {
            public readonly TcpClient localtcp;

            public ClientState(TcpClient localtcp)
            {
                this.localtcp = localtcp;
            }
        }

        class ServerState
        {
            public readonly Settings settings = LoadSettings();
            public readonly IDictionary<TcpClient, ClientState> clients= new Dictionary<TcpClient, ClientState>();
            public readonly SyncGate clientsMutex = new SyncGate();

            public Socket remote = null;

            public ServerState()
            {
            }
        }

        class Settings
        {
            public string addresslisten;
            public int portlisten;

            public string addressforward;
            public int portforward;
        }

        static Settings LoadSettings()
        {
            var s = ConfigurationManager.AppSettings;

            return new Settings
            {
                addresslisten = s["addresslisten"],
                portlisten = Convert.ToInt32(s["portlisten"]),
                addressforward = s["addressforward"],
                portforward = Convert.ToInt32(s["portforward"])
            };
        }

        static IEnumerator<Int32> HandleClient(AsyncEnumerator ae,
                                               ServerState server,
                                               ClientState client,
                                               TcpClient tcpRecv,
                                               TcpClient tcpSend)
        {
            // add the client to the clients list
            {
                server.clientsMutex.BeginRegion(SyncGateMode.Exclusive, ae.End());
                yield return 1;
                var mutexResult = ae.DequeueAsyncResult();
                {
                    if (!server.clients.ContainsKey(client.localtcp))
                    {
                        server.clients[client.localtcp] = client;
                    }
                }
                server.clientsMutex.EndRegion(mutexResult);
            }

            Socket sockRecv = tcpRecv.Client;
            Socket sockSend = tcpSend.Client;

            byte[] buffer = new byte[1024];
            int len = 0;
            while (true)
            {
                // read message
                sockRecv.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ae.End(), null);
                yield return 1;

                try
                {
                    len = sockRecv.EndReceive(ae.DequeueAsyncResult());
                    if (len == 0) break;
                }
                catch (Exception)
                {
                    break;
                }

                // echo the data back to the client
                sockSend.BeginSend(buffer, 0, len, SocketFlags.None, ae.End(), null);
                yield return 1;

                try
                {
                    sockSend.EndSend(ae.DequeueAsyncResult());
                }
                catch (Exception)
                {
                    break;
                }
            }

            // remove the client from the list
            {
                server.clientsMutex.BeginRegion(SyncGateMode.Exclusive, ae.End());
                yield return 1;
                var mutexResult = ae.DequeueAsyncResult();
                {
                    if (server.clients.ContainsKey(client.localtcp))
                    {
                        var removed = server.clients.Remove(client.localtcp);
                        Console.WriteLine("Client disconnected {0} - {1}", sockRecv.RemoteEndPoint, removed);

                        // dont forget to dispose it
                        tcpRecv.Close();
                        tcpSend.Close();
                    }
                }
                server.clientsMutex.EndRegion(mutexResult);
            }
        }

        static IEnumerator<Int32> ServerConnectRemote(AsyncEnumerator ae, ServerState server, IPEndPoint ipe, TcpClient local)
        {
            // establish a client proxy -> real server connection
            var newClientState = new ClientState(local);
            var remote = new TcpClient(ipe.AddressFamily);
            var settings = server.settings;
            remote.NoDelay = true;
            remote.LingerState.Enabled = false;

            remote.BeginConnect(IPAddress.Parse(settings.addressforward), settings.portforward, ae.End(), ae);
            yield return 1;

            try
            {
                remote.EndConnect(ae.DequeueAsyncResult());

                var remoteAe = new AsyncEnumerator();
                var localAe = new AsyncEnumerator();
                localAe.BeginExecute(HandleClient(localAe, server, newClientState, local, remote), localAe.EndExecute, localAe);
                remoteAe.BeginExecute(HandleClient(remoteAe, server, newClientState, remote, local), remoteAe.EndExecute, remoteAe);

                Console.WriteLine("Client Connected {0}", local.Client.RemoteEndPoint);
            }
            catch (SocketException)
            {
                Console.WriteLine("A client failed to connect, is the real server started?");
                local.Close();
                remote.Close();
            }
        }

        static IEnumerator<Int32> Server(AsyncEnumerator ae, ServerState server)
        {
            var sleeper = new CountdownTimer();
            var settings = server.settings;
            var ipe = new IPEndPoint(IPAddress.Parse(settings.addressforward), settings.portforward);
            var serverSock = new TcpListener(IPAddress.Parse(settings.addresslisten), settings.portlisten);
            serverSock.Start(20);

            Console.WriteLine("Server started");
            Console.WriteLine("Listening on {0}:{1}", settings.addresslisten, settings.portlisten);
            Console.WriteLine("Forwarding to {0}:{1}", settings.addressforward, settings.portforward);
            while (true)
            {
                serverSock.BeginAcceptTcpClient(ae.End(), null);
                yield return 1;

                TcpClient local = null;
                try
                {
                    local = serverSock.EndAcceptTcpClient(ae.DequeueAsyncResult());
                    local.SendTimeout = 10000;
                    local.ReceiveTimeout = 10000;
                    local.NoDelay = true;
                    local.LingerState.Enabled = false;
                }
                catch (SocketException)
                {
                    Console.WriteLine("A client failed to connect");
                }

                if(local != null)
                {
                    // handle client
                    var localAe = new AsyncEnumerator();
                    localAe.BeginExecute(ServerConnectRemote(localAe, server, ipe, local), localAe.EndExecute, localAe);
                }
            }
        }

    }
}
