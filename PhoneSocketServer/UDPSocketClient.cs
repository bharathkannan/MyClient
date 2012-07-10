/// Copyright (c) 2011 Brian Bonnett
/// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
/// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections;
using System.Text;
namespace SocketServer
{
    public class ConnectMgr
    {

        public static EndPoint GetIPEndpoint(string ipaddr, int nport)
        {
            try
            {
                return new DnsEndPoint(ipaddr, nport, AddressFamily.InterNetwork);
            }
            catch (Exception e) /// could not resolve host name
            {
            }
            return null;
        }
    }

   public class BufferSocketRef
   {
      public BufferSocketRef(UDPSocketClient client)
      {
         Client = client;

         /// See if we are using our pinned-memory protection, if we are use from the pool, if not, just new it
         if (UDPSocketClient.m_BufferPool != null)
            bRecv = UDPSocketClient.m_BufferPool.Checkout();
         else
            bRecv = new byte[UDPSocketClient.m_nBufferSize];
      }

      public UDPSocketClient Client;
      public byte[] bRecv;

      public void CheckInCopy(int nLen)
      {
         /// Copy our buffer to a non-pinned byte array, and release our pinned array back to the pool
         if (UDPSocketClient.m_BufferPool != null)
         {
            byte[] bPassIn = new byte[nLen];
            if (nLen > 0)
               Array.Copy(bRecv, 0, bPassIn, 0, nLen);

            UDPSocketClient.m_BufferPool.Checkin(bRecv);
            bRecv = bPassIn;
         }
      }

   }

	/// <summary>
	/// Summary description for UDPSocketClient.
	/// </summary>
	public class UDPSocketClient //: System.Net.Sockets.UdpClient
	{
		public UDPSocketClient(IPEndPoint ep) // : base(ep)
		{
            m_ipEp = ep;

            /// no ipv6 support in windows phone 7
            s = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            m_tempRemoteEP = (System.Net.IPEndPoint)sender;
      }

      //public DateTimePrecise DateTimePrecise = new DateTimePrecise();

      
      /// <summary>
      /// set for logging
      /// </summary>
      public ILogInterface m_Logger = null;
      public string OurGuid = "UDPClient";

      void LogMessage(MessageImportance importance, string strEventName, string strMessage)
      {
         if (m_Logger != null)
         {
            m_Logger.LogMessage(ToString(), importance, strMessage);
         }
      }

      void LogWarning(MessageImportance importance, string strEventName, string strMessage)
      {
         if (m_Logger != null)
         {
             m_Logger.LogWarning(ToString(), importance, strMessage);
         }
      }

      void LogError(MessageImportance importance, string strEventName, string strMessage)
      {
         if (m_Logger != null)
         {
             m_Logger.LogError(ToString(), importance, strMessage);
         }
      }

      public readonly IPEndPoint m_ipEp;         /// our endpoint
      public System.Net.Sockets.Socket s = null;

      public delegate void DelegateReceivePacket(byte[] bData, int nLength, IPEndPoint epfrom, IPEndPoint epthis, DateTime dtReceived);
      public event DelegateReceivePacket OnReceivePacket = null;
      public event DelegateReceivePacket OnReceiveMessage = null;
      public static readonly int m_nBufferSize = 2048; /// Max udp buffer size for windows phone
      
      protected IPEndPoint m_tempRemoteEP = null;  /// temp endpoint for receivefrom
      //protected System.AsyncCallback asyncb;
      protected bool m_bReceive = true;
      public int NumberOfReceivingThreads; // This would allow the consumer to know the status of Receiving operation.
		
		public delegate void DelegateReceivingStopped(string reason);

		public event DelegateReceivingStopped OnReceivingStopped=null;

      public object SyncRoot = new object();
      public string ThreadNameShutdown = "";
      public string ThreadNameDispose = "";

      //const uint SIO_UDP_CONNRESET = 0x9800000C;
      // 0x9800000C == 2440136844 (uint) == -1744830452 (int) == 0x9800000C
      const int SIO_UDP_CONNRESET = -1744830452;


      public static IBufferPool m_BufferPool = null;

      bool m_bIsBound = false;
      public bool Bind()
      {
         if (m_bIsBound == true)
            return true;

          m_bIsBound = true;
          /// Doesn't appear to be a bind option in windows phone 7
          /// 

         return true;
      }

		public bool StartReceiving()
      {
         /// See http://blog.devstone.com/aaron/archive/2005/02/20/460.aspx
         /// This will stop winsock errors when receiving an ICMP packet 
         /// "Destination unreachable"
         byte[] inValue = new byte[] { 0, 0, 0, 0 };     // == false
         byte[] outValue = new byte[] { 0, 0, 0, 0 };    // initialize to 0
         //s.IOControl(SIO_UDP_CONNRESET, inValue, outValue);


         if (Bind() == false)
            return false;

         lock (SyncRoot)
         {
            m_bReceive = true;
            this.NumberOfReceivingThreads = 1;
            DoReceive();
           
         }
         return true;
      }

      public void StopReceiving()
      {
         lock (SyncRoot)
         {
            if (m_bReceive == false)
            {
               this.LogError(MessageImportance.Highest, "error", string.Format("Can't call StopReceiving, Thread has been disposed by {0} or closed by {1}", this.ThreadNameDispose, this.ThreadNameShutdown));
               return;
            }

            ThreadNameShutdown = System.Threading.Thread.CurrentThread.Name;
            LogMessage(MessageImportance.Lowest, this.OurGuid, string.Format("Called StopReceiving for {0}", s));
            m_bReceive = false;
            s.Close();
         }
      }

      protected void DoReceive()
      {
         lock (SyncRoot)
         {
            if (m_bReceive == false)
            {
               this.LogError(MessageImportance.Highest, "error", string.Format("Can't call DoReceive, socket has been disposed by thread {0} or closed by {1}", this.ThreadNameDispose, this.ThreadNameShutdown));
               return;
            }
            
            try
            {
                LogMessage(MessageImportance.Lowest, this.OurGuid, string.Format("Called DoReceive"));

                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.RemoteEndPoint = new IPEndPoint(IPAddress.Any, this.m_ipEp.Port); //m_ipEp; // i edited new IPEndPoint(IPAddress.Any, this.m_ipEp.Port);
                byte [] bBuffer = new byte[2048];
                args.SetBuffer(bBuffer, 0, bBuffer.Length);
                args.Completed += new EventHandler<SocketAsyncEventArgs>(OnReceiveUDP);
                s.ReceiveFromAsync(args);
            }
            catch (SocketException e3) /// winso
            {
               string strError = string.Format("{0} - {1}", e3.ErrorCode, e3.ToString());
               LogError(MessageImportance.High, "SocketEXCEPTION", strError);
               --(this.NumberOfReceivingThreads);
               if (this.NumberOfReceivingThreads == 0 && this.OnReceivingStopped != null)
                  this.OnReceivingStopped(strError);
               return;
            }
            catch (ObjectDisposedException e4) // socket was closed
            {
               string strError = e4.ToString();
               LogError(MessageImportance.High, "ObjectDisposedEXCEPTION", strError);
               --(this.NumberOfReceivingThreads);
               if (this.NumberOfReceivingThreads == 0 && this.OnReceivingStopped != null)
                  this.OnReceivingStopped(strError);
               return;
            }
            catch (Exception e5)
            {
               string strError = string.Format("{0}", e5.ToString());
               LogError(MessageImportance.High, "EXCEPTION", strError);
               --(this.NumberOfReceivingThreads);
               if (this.NumberOfReceivingThreads == 0 && this.OnReceivingStopped != null)
                  this.OnReceivingStopped(strError);
               return;
            }
         }
         return;
      }


      protected void OnReceiveUDP(object sender, SocketAsyncEventArgs e)
      {
          DateTime dtReceive = DateTime.Now; // DateTimePrecise.Now;

         int nRecv = 0;
         try
         {
             nRecv = e.BytesTransferred;
         }
         catch (SocketException e3) /// winso
         {
            string strError = string.Format("{0} - {1}", e3.ErrorCode, e3.ToString());
            LogError(MessageImportance.High, "EXCEPTION", strError);
            --(this.NumberOfReceivingThreads);
            //if (this.NumberOfReceivingThreads == 0 && this.OnReceivingStopped != null)
              // this.OnReceivingStopped(strError);

            /// Get 10054 if the other end is not listening (ICMP returned)... fixed above with IOControl
            if (e3.ErrorCode != 10054)
            {
            }
            return;

         }
         catch (ObjectDisposedException e4) // socket was closed
         {
            string strError = e4.ToString();
            this.LogWarning(MessageImportance.Low, "EXCEPTION", strError);
            --(this.NumberOfReceivingThreads);
            if (this.NumberOfReceivingThreads == 0 && this.OnReceivingStopped != null)
               this.OnReceivingStopped(strError);
            return;
         }
         catch (Exception e5)
         {
            string strError = string.Format("{0}", e5.ToString());
            LogError(MessageImportance.High, "EXCEPTION", strError);
            --(this.NumberOfReceivingThreads);
            if (this.NumberOfReceivingThreads == 0 && this.OnReceivingStopped != null)
               this.OnReceivingStopped(strError);
            return;
         }


         OnRecv(e.Buffer, nRecv, e.RemoteEndPoint as IPEndPoint, dtReceive);
      }

      private void OnRecv(byte[] bRecv, int nRecv, IPEndPoint ipep, DateTime dtReceive)
      {

         if (nRecv > 0)
         {
             if (OnReceivePacket != null)
             {
                 OnReceivePacket(bRecv, nRecv, ipep, this.m_ipEp, dtReceive);
             }

             if (OnReceiveMessage != null)
             {
                 byte[] bMessage = new byte[nRecv];
                 Array.Copy(bRecv, 0, bMessage, 0, nRecv);
                 OnReceiveMessage(bMessage, bMessage.Length, ipep, this.m_ipEp, dtReceive);
             }

             if (m_bReceive == true)
                 DoReceive();
         }

      }

     

      public int SendUDP(byte[] bData, int nLength, System.Net.EndPoint ep)
      {
         lock (SyncRoot)
         {
            if (m_bReceive == false)
            {
               this.LogError(MessageImportance.Highest, "error", string.Format("Can't call SendUDP, socket not valid or closed"));
               return 0;
            }

            LogMessage(MessageImportance.Lowest, this.OurGuid, string.Format("SendUDP to {0}", ep));

            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.RemoteEndPoint = ep;
            args.SetBuffer(bData, 0, nLength);
            args.Completed += new EventHandler<SocketAsyncEventArgs>(SendUDP_Completed);
            s.SendToAsync(args);
            return nLength;
         }
      }

      void SendUDP_Completed(object sender, SocketAsyncEventArgs e)
      {
          /// Windows phone 7 can only receive after sending???? fucked up
          /// 
            /// http://stackoverflow.com/questions/7501495/windows-phone-udp
          /// http://stackoverflow.com/questions/6551477/issues-with-async-receiving-udp-unicast-packets-in-windows-phone-7/
          DoReceive();
      }



      #region ReStarting DoReceive()
      //
      // Only want to start the DoReceive().  The main purpose is to keep ListenOn the local 
      // port while recover the error caused by operation of sending to a unreachable destination(usually un-listened port).
      // Dead computer does not cause error.
      //
      public bool RestartReceivingData()
      {
         if(this.NumberOfReceivingThreads!=0)return false;
         this.NumberOfReceivingThreads=8;
         for(int i=0;i<8;i++)
            DoReceive();
         return true;
      }
      #endregion


     #region Myedit

      static ManualResetEvent _clientDone = new ManualResetEvent(false);

      // Define a timeout in milliseconds for each asynchronous call. If a response is not received within this 
      // timeout period, the call is aborted.
      const int TIMEOUT_MILLISECONDS = 5000;

      // The maximum size of the data buffer to use with the asynchronous socket methods
      const int MAX_BUFFER_SIZE = 2048;


        public string MySend(string serverName, int portNumber, string data)
        {
            string response = "Operation Timeout";

            // We are re-using the s object that was initialized in the Connect method
            if (s != null)
            {
                // Create SocketAsyncEventArgs context object
                SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();

                // Set properties on context object
                socketEventArg.RemoteEndPoint = new DnsEndPoint(serverName, portNumber);

                // Inline event handler for the Completed event.
                // Note: This event handler was implemented inline in order to make this method self-contained.
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate(object so, SocketAsyncEventArgs e)
                {
                    response = e.SocketError.ToString();

                    // Unblock the UI thread
                    _clientDone.Set();
                });

                // Add the data to be sent into the buffer
                byte[] payload = Encoding.UTF8.GetBytes(data);
                socketEventArg.SetBuffer(payload, 0, payload.Length);

                // Sets the state of the event to nonsignaled, causing threads to block
                _clientDone.Reset();

                // Make an asynchronous Send request over the socket
                s.SendToAsync(socketEventArg);

                // Block the UI thread for a maximum of TIMEOUT_MILLISECONDS milliseconds.
                // If no response comes back within this time then proceed
                _clientDone.WaitOne(TIMEOUT_MILLISECONDS);
            }
            else
            {
                response = "Socket is not initialized";
            }

           
            return response;
        }
        public string MyReceive(int portNumber)
        {
            string response = "Operation Timeout";

            // We are receiving over an established socket connection
            if (s != null)
            {
                // Create SocketAsyncEventArgs context object
                SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();
                socketEventArg.RemoteEndPoint = new IPEndPoint(IPAddress.Any, portNumber);

                // Setup the buffer to receive the data
                socketEventArg.SetBuffer(new Byte[MAX_BUFFER_SIZE], 0, MAX_BUFFER_SIZE);

                // Inline event handler for the Completed event.
                // Note: This even handler was implemented inline in order to make this method self-contained.
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate(object so, SocketAsyncEventArgs e)
                {
                    if (e.SocketError == SocketError.Success)
                    {
                        // Retrieve the data from the buffer
                        response = Encoding.UTF8.GetString(e.Buffer, e.Offset, e.BytesTransferred);
                        response = response.Trim('\0');


                        //involves code in th UI  thread -- called a dispatcher  


                    }
                    else
                    {
                        response = e.SocketError.ToString();
                    }

                    _clientDone.Set();
                });

                // Sets the state of the event to nonsignaled, causing threads to block
                _clientDone.Reset();

                // Make an asynchronous Receive request over the socket
                s.ReceiveFromAsync(socketEventArg);

                // Block the UI thread for a maximum of TIMEOUT_MILLISECONDS milliseconds.
                // If no response comes back within this time then proceed
                _clientDone.WaitOne();
            }
            else
            {
                response = "Socket is not initialized";
            }
            return response;
        }


        public string MyReceive1(int portNumber,int timeout=10000)
        {
            string response = "Operation Timeout";

            // We are receiving over an established socket connection
            if (s != null)
            {
                // Create SocketAsyncEventArgs context object
                SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();
                socketEventArg.RemoteEndPoint = new IPEndPoint(IPAddress.Any, portNumber);

                // Setup the buffer to receive the data
                socketEventArg.SetBuffer(new Byte[MAX_BUFFER_SIZE], 0, MAX_BUFFER_SIZE);

                // Inline event handler for the Completed event.
                // Note: This even handler was implemented inline in order to make this method self-contained.
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate(object so, SocketAsyncEventArgs e)
                {
                    if (e.SocketError == SocketError.Success)
                    {
                        // Retrieve the data from the buffer
                        response = Encoding.UTF8.GetString(e.Buffer, e.Offset, e.BytesTransferred);
                        response = response.Trim('\0');


                        //involves code in th UI  thread -- called a dispatcher  


                    }
                    else
                    {
                        response = e.SocketError.ToString();
                    }

                    _clientDone.Set();
                });

                // Sets the state of the event to nonsignaled, causing threads to block
                _clientDone.Reset();

                // Make an asynchronous Receive request over the socket
                s.ReceiveFromAsync(socketEventArg);

                // Block the UI thread for a maximum of TIMEOUT_MILLISECONDS milliseconds.
                // If no response comes back within this time then proceed
                _clientDone.WaitOne(timeout);
            }
            else
            {
                response = "Socket is not initialized";
            }
            return response;
        }

        public string SendByteArray(byte[] message,DnsEndPoint Remote)
        {
            string response = "Operation Timeout";

            string serverName = Remote.Host;
            int portNumber = Remote.Port;

            // We are re-using the s object that was initialized in the Connect method
            if (s != null)
            {
                // Create SocketAsyncEventArgs context object
                SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();

                // Set properties on context object
                socketEventArg.RemoteEndPoint = new DnsEndPoint(serverName, portNumber);

                // Inline event handler for the Completed event.
                // Note: This event handler was implemented inline in order to make this method self-contained.
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate(object so, SocketAsyncEventArgs e)
                {
                    response = e.SocketError.ToString();

                    // Unblock the UI thread
                    _clientDone.Set();
                });

                // Add the data to be sent into the buffer
                byte[] payload = message;
                socketEventArg.SetBuffer(payload, 0, payload.Length);

                // Sets the state of the event to nonsignaled, causing threads to block
                _clientDone.Reset();

                // Make an asynchronous Send request over the socket
                s.SendToAsync(socketEventArg);

                // Block the UI thread for a maximum of TIMEOUT_MILLISECONDS milliseconds.
                // If no response comes back within this time then proceed
                _clientDone.WaitOne(TIMEOUT_MILLISECONDS);
            }
            else
            {
                response = "Socket is not initialized";
            }


            return response;
        }


        public byte[] ReceiveByteArray(int portNumber,int timeout)
        {
            string response = "Operation Timeout";
            byte[] ret = null;
            // We are receiving over an established socket connection
            if (s != null)
            {
                // Create SocketAsyncEventArgs context object
                SocketAsyncEventArgs socketEventArg = new SocketAsyncEventArgs();
                socketEventArg.RemoteEndPoint = new IPEndPoint(IPAddress.Any, portNumber);

                // Setup the buffer to receive the data
                socketEventArg.SetBuffer(new Byte[MAX_BUFFER_SIZE], 0, MAX_BUFFER_SIZE);

                // Inline event handler for the Completed event.
                // Note: This even handler was implemented inline in order to make this method self-contained.
                socketEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(delegate(object so, SocketAsyncEventArgs e)
                {
                    if (e.SocketError == SocketError.Success)
                    {
                        ret = new byte[e.BytesTransferred];
                        
                        Array.Copy(e.Buffer,e.Offset,ret,0,e.BytesTransferred);
                        // Retrieve the data from the buffer


                        //involves code in th UI  thread -- called a dispatcher  


                    }
                    else
                    {
                        response = e.SocketError.ToString();
                    }

                    _clientDone.Set();
                });

                // Sets the state of the event to nonsignaled, causing threads to block
                _clientDone.Reset();

                // Make an asynchronous Receive request over the socket
                s.ReceiveFromAsync(socketEventArg);

                // Block the UI thread for a maximum of TIMEOUT_MILLISECONDS milliseconds.
                // If no response comes back within this time then proceed
                _clientDone.WaitOne(timeout);
            }
            else
            {
                response = "Socket is not initialized";
            }
            if (ret != null)
                return ret;
            return Encoding.UTF8.GetBytes(response);
        }
       
        #endregion

    }
}







           
    

