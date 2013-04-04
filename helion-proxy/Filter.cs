using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace helion_proxy
{
    class Filter
    {
        // return either Buffer, Accept, Drop or Disconnect
        public FilterIntent Send(ClientState client, byte[] buffer, int len)
        {
            return FilterIntent.Accept;
        }

        // return either Buffer, Accept, Drop or Disconnect
        public FilterIntent Recv(ClientState client, byte[] buffer, int len)
        {
            return FilterIntent.Accept;
        }
    }
}
