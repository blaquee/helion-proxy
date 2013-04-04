using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Wintellect.Threading.AsyncProgModel;
using System.Configuration;
using System.Net;
using Toub.Collections;
using System.Diagnostics;

namespace helion_proxy
{
    using FilterFunc = Func<ClientState, byte[], int, FilterIntent>;

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Wokf AntiHaxxing Proxy Server by Hunter and Thanatos. Version 1");

            var filter = new Filter();
            var serverState = new ServerState(filter.Recv, filter.Send);

            // a new AsyncEnumerator is constructed for the sake of a new thread of execution
            var ae = new AsyncEnumerator();
            var r = ae.BeginExecute(Server(ae, serverState), ae.EndExecute);
            r.AsyncWaitHandle.WaitOne();
        }

        static IEnumerator<Int32> HandleClient(AsyncEnumerator ae,
                                               ServerState server,
                                               ClientState client,
                                               TcpClient tcpRecv,
                                               TcpClient tcpSend,
                                               BufferWrapper bw,
                                               FilterFunc filterFunc)
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
            bool wantDisconnect = false;
            int len = 0;
            while (true)
            {
                // read message
                try
                {
                    // resize buffer if needed so that it has initialbuffersize spaces at the end
                    if((bw.buffer.Length - len) < server.settings.initialbuffersize)
                    {
                        Array.Resize(ref bw.buffer, len + server.settings.initialbuffersize);
                    }

                    sockRecv.BeginReceive(bw.buffer, len, server.settings.initialbuffersize, SocketFlags.None, ae.End(), null);
                }
                catch (Exception)
                {
                    break;
                }
                
                yield return 1;
                

                try
                {
                    int tmplen = sockRecv.EndReceive(ae.DequeueAsyncResult());
                    
                    if (tmplen == 0)
                    {
                        break;
                    }
                    else
                    {
                        len += tmplen;
                    }
                }
                catch (Exception)
                {
                    break;
                }
                
                // filter the packets here
                switch (filterFunc(client, bw.buffer, len))
                {
                    case FilterIntent.Buffer:
                        break;

                    case FilterIntent.Accept:
                        int sent = 0;
                        while(sent < len)
                        {
                            // echo the data back to the client
                            try
                            {
                                sockSend.BeginSend(bw.buffer, sent, len-sent, SocketFlags.None, ae.End(), null);
                            }
                            catch (Exception)
                            {
                                wantDisconnect = true;
                                break;
                            }
                            yield return 1;

                            try
                            {
                                sent += sockSend.EndSend(ae.DequeueAsyncResult());
                            }
                            catch (Exception)
                            {
                                wantDisconnect = true;
                                break;
                            }
                        }
                        len = 0;
                        break;

                    case FilterIntent.Drop:
                        len = 0;
                        break;

                    case FilterIntent.Disconnect:
                        wantDisconnect = true;
                        break;
                }

                if(wantDisconnect)
                    break;
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
                        Console.WriteLine("Client disconnected {0} - {1}", client.localtcp.Client.RemoteEndPoint, removed);

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
            
            var remote = new TcpClient(ipe.AddressFamily);
            var settings = server.settings;
            remote.NoDelay = true;
            remote.LingerState.Enabled = false;

            remote.BeginConnect(IPAddress.Parse(settings.addressforward), settings.portforward, ae.End(), ae);
            yield return 1;

            try
            {
                remote.EndConnect(ae.DequeueAsyncResult());
            }
            catch (SocketException)
            {
                Console.WriteLine("A client failed to connect, has the real server started?");
                local.Close();
                remote.Close();
                remote = null;
            }

            if(remote != null)
            {
                var bufParam = new ClientBufferParam();

                {
                    server.bufferPoolMutex.BeginRegion(SyncGateMode.Exclusive, ae.End());
                    yield return 1;
                    var res = ae.DequeueAsyncResult();
                    server.AllocBuffer(ae, bufParam);
                    server.bufferPoolMutex.EndRegion(res);
                }

                {
                    var newClientState = new ClientState(local, remote);
                    var remoteAe = new AsyncEnumerator();
                    var localAe = new AsyncEnumerator();
                    var bw1 = new BufferWrapper(bufParam.sendbuffer);
                    var bw2 = new BufferWrapper(bufParam.recvbuffer);
                    localAe.BeginExecute(HandleClient(localAe, server, newClientState, local, remote, bw1, server.filterSend), ae.End(), localAe);
                    remoteAe.BeginExecute(HandleClient(remoteAe, server, newClientState, remote, local, bw2, server.filterRecv), ae.End(), remoteAe);

                    Console.WriteLine("Client Connected {0}", local.Client.RemoteEndPoint);

                    yield return 2;
                    for (int x = 0; x < 2; x++) 
                    {
                        IAsyncResult ar = ae.DequeueAsyncResult();
                        ((AsyncEnumerator)ar.AsyncState).EndExecute(ar);
                    }
                    bufParam.sendbuffer = bw1.buffer;
                    bufParam.recvbuffer = bw2.buffer;
                }

                {
                    server.bufferPoolMutex.BeginRegion(SyncGateMode.Exclusive, ae.End());
                    yield return 1;
                    var res = ae.DequeueAsyncResult();
                    server.FreeBuffer(ae, bufParam);
                    server.bufferPoolMutex.EndRegion(res);
                }
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

    #region classes
    enum FilterIntent
    {
        //Buffer more data
        Buffer,

        // Accept and Drop flushes the buffer before sending the data off
        Accept,
        Drop,

        //Discard the buffer and disconnect
        Disconnect
    }
    class ClientState
    {
        public readonly TcpClient localtcp;
        public readonly TcpClient remotetcp;

        public ClientState(TcpClient localtcp, TcpClient remotetcp)
        {
            this.localtcp = localtcp;
            this.remotetcp = remotetcp;
        }
    }

    class ClientBufferParam
    {
        public byte[] sendbuffer;
        public byte[] recvbuffer;
    }

    class BufferWrapper
    {
        public byte[] buffer;
        public BufferWrapper(byte[] buffer)
        {
            this.buffer = buffer;
        }
    }

    class ServerState
    {
        public readonly Settings settings = LoadSettings();

        public readonly IDictionary<TcpClient, ClientState> clients = new Dictionary<TcpClient, ClientState>();
        public readonly SyncGate clientsMutex = new SyncGate();

        public readonly PriorityQueue<byte[]> bufferPool = new PriorityQueue<byte[]>();
        public readonly SyncGate bufferPoolMutex = new SyncGate();

        public readonly FilterFunc filterRecv;
        public readonly FilterFunc filterSend;

        public ServerState(FilterFunc filterRecv, FilterFunc filterSend)
        {
            this.filterRecv = filterRecv;
            this.filterSend = filterSend;

            for (int i = 0; i < 1024;++i)
            {
                bufferPool.Enqueue(settings.initialbuffersize, new byte[settings.initialbuffersize]);
            }
        }

        public void AllocBuffer(AsyncEnumerator ae, ClientBufferParam buffer)
        {
            if (bufferPool.Count < 2)
            {
                for (int i = 0; i < 1024; ++i)
                {
                    bufferPool.Enqueue(settings.initialbuffersize, new byte[settings.initialbuffersize]);
                }
            }
            buffer.sendbuffer = bufferPool.Dequeue();
            buffer.recvbuffer = bufferPool.Dequeue();
            Console.WriteLine("ALLOCED {0} {1}", buffer.recvbuffer.Length, buffer.sendbuffer.Length);
        }

        public void FreeBuffer(AsyncEnumerator ae, ClientBufferParam buffer)
        {
            bufferPool.Enqueue(buffer.sendbuffer.Length, buffer.sendbuffer);
            bufferPool.Enqueue(buffer.recvbuffer.Length, buffer.recvbuffer);
            Console.WriteLine("FREED {0} {1}", buffer.recvbuffer.Length, buffer.sendbuffer.Length);
        }

        static Settings LoadSettings()
        {
            var s = ConfigurationManager.AppSettings;

            return new Settings
            {
                addresslisten = s["addresslisten"],
                portlisten = Convert.ToInt32(s["portlisten"]),
                addressforward = s["addressforward"],
                portforward = Convert.ToInt32(s["portforward"]),
                initialbuffersize = Convert.ToInt32(s["initialbuffersize"])
            };
        }
    }

    class Settings
    {
        public string addresslisten;
        public int portlisten;

        public string addressforward;
        public int portforward;

        public int initialbuffersize;
    }
    #endregion

}
