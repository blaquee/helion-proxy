using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Wintellect.Threading.AsyncProgModel;
using System.Configuration;

namespace helion_proxy
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Wokf AntiHaxxing Proxy Server by Hunter and Thanatos. Version 1");

            var settings = LoadSettings();
            var ae = new AsyncEnumerator();
            var r = ae.BeginExecute(Server(ae, settings), ae.EndExecute);
            r.AsyncWaitHandle.WaitOne();
        }

        class ClientState
        {
            public readonly TcpClient tcp;

            public ClientState(TcpClient tcp)
            {
                this.tcp = tcp;
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

        static IEnumerator<Int32> HandleClient(AsyncEnumerator ae, IDictionary<TcpClient, ClientState> clients, ClientState client)
        {
            TcpClient tcp = client.tcp;
            Socket sock = tcp.Client;

            byte[] buffer = new byte[1024];
            int len = 0;

            tcp.SendTimeout = 10000;
            tcp.ReceiveTimeout = 10000;
            tcp.NoDelay = true;
            tcp.LingerState.Enabled = false;
            while (true)
            {
                // read message
                sock.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, ae.End(), null);
                yield return 1;

                try
                {
                    len = sock.EndReceive(ae.DequeueAsyncResult());
                    if (len == 0) break;
                }
                catch (Exception)
                {
                    break;
                }

                // echo the data back to the client
                sock.BeginSend(buffer, 0, len, SocketFlags.None, ae.End(), null);
                yield return 1;

                try
                {
                    sock.EndSend(ae.DequeueAsyncResult());
                }
                catch (Exception)
                {
                    break;
                }
            }

            var removed = false;
            lock (clients)
            {
                removed = clients.Remove(tcp);
            }
            Console.WriteLine("Client disconnected {0} - {1}", sock.RemoteEndPoint, removed);
        }

        static IEnumerator<Int32> Server(AsyncEnumerator ae, Settings settings)
        {
            var clients = new Dictionary<TcpClient, ClientState>();
            var serverSock = new TcpListener(System.Net.IPAddress.Parse(settings.addresslisten), settings.portlisten);
            serverSock.Start();

            Console.WriteLine("Server started");
            Console.WriteLine("Listening on {0}:{1}", settings.addresslisten, settings.portlisten);
            Console.WriteLine("Forwarding to {0}:{1}", settings.addressforward, settings.portforward);
            while (true)
            {
                serverSock.BeginAcceptTcpClient(ae.End(), null);
                yield return 1;

                try
                {
                    TcpClient client = serverSock.EndAcceptTcpClient(ae.DequeueAsyncResult());
                    // handle client
                    var newClientState = new ClientState(client);
                    lock (clients)
                    {
                        clients[client] = newClientState;
                    }
                    var clientAe = new AsyncEnumerator();
                    clientAe.BeginExecute(HandleClient(clientAe, clients, newClientState), clientAe.EndExecute, clientAe);

                    Console.WriteLine("Client Connected {0}", client.Client.RemoteEndPoint);
                }
                catch (SocketException)
                {
                    Console.WriteLine("A client failed to connect");
                }
            }
        }
    }
}
