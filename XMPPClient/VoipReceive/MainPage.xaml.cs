﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Phone.Controls;
using RTP;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using System.Threading;
using System.Windows.Threading;
using FindMyIP;
using XMPPClient;
using AudioClasses;
using SocketServer;
namespace VoipReceive
{
    public partial class MainPage : PhoneApplicationPage
    {
        public MediaElement AudioStream = null;
        RTPAudioStream stream = null,ControlStream = null,sstream=null;
        AudioClasses.ByteBuffer MicrophoneQueue = new AudioClasses.ByteBuffer();
        IPAddress myip;
        Boolean IsCallActive;
        IPEndPoint localEp;
        Thread SpeakerThread,MicrophoneThread;
        AudioStreamSource source = null;
        IPEndPoint remote,stunEp;
        string myname,yourname;
        IPEndPoint[] remotecandidates,localcandidates;
        // Constructor
        public MainPage()
        {
            InitializeComponent();
            mediaElement1.Stop();
            AudioStream = this.mediaElement1;
            var _Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _Timer.Tick += (s, arg) =>
            {
                FrameworkDispatcher.Update();

            };
            _Timer.Start();
            FindMyIP();
            InitializeStream();
            button3.IsEnabled = false;
        }


        void Log(string message)
        {
            Deployment.Current.Dispatcher.BeginInvoke(() => { textBlock1.Text += message + '\n'; });
        }

        void FindMyIP()
        {
            MyIPAddress my = new MyIPAddress();
            myip = my.Find();
            localEp = new IPEndPoint(myip, 3001);

        }

        void InitializeStream()
        {
            stream = new RTPAudioStream(0, null);
            stream.Bind(localEp);
            stream.AudioCodec = new G722CodecWrapper();
            stream.UseInternalTimersForPacketPushPull = false;
            try
            {
                stunEp = stream.GetSTUNAddress(new DnsEndPoint("stun.ekiga.net", 3478), 4000);
            }
            catch (Exception e)
            {
                Log("Stun address retrieval failed.Cant work with this device.");
            }

            Log(stunEp.ToString());
            
            ControlStream = new RTPAudioStream(0, null);
            localEp = new IPEndPoint(myip, 3002);
            ControlStream.Bind(localEp);
            ControlStream.AudioCodec = new G722CodecWrapper();
            ControlStream.UseInternalTimersForPacketPushPull = false;

            sstream = new RTPAudioStream(0, null);
            localEp = new IPEndPoint(myip, 3003);
            sstream.Bind(localEp);
            sstream.AudioCodec = new G722CodecWrapper();
            sstream.UseInternalTimersForPacketPushPull = false;
          
            Log(stunEp.ToString());

        }     
       
        
        void SafeStartMediaElement(object obj, EventArgs args)
        {
            if (AudioStream.CurrentState != MediaElementState.Playing)
            {
                AudioStream.BufferingTime = new TimeSpan(0, 0, 0);

                AudioStream.SetSource(source);
                AudioStream.Play();
            }
        }
        void SafeStopMediaElement(object obj, EventArgs args)
        {
            AudioStream.Stop();
        }

      
        
        public void SpeakerThreadFunction()
        {
            Log("Start Receiving");
            TimeSpan tsPTime = TimeSpan.FromMilliseconds(stream.PTimeReceive);
            int nSamplesPerPacket = stream.AudioCodec.AudioFormat.CalculateNumberOfSamplesForDuration(tsPTime);
            int nBytesPerPacket = nSamplesPerPacket * stream.AudioCodec.AudioFormat.BytesPerSample;
            byte[] bDummySample = new byte[nBytesPerPacket];
            source.PacketSize = nBytesPerPacket;
            stream.IncomingRTPPacketBuffer.InitialPacketQueueMinimumSize = 4;
            stream.IncomingRTPPacketBuffer.PacketSizeShiftMax = 10;
            int nMsTook = 0;


            Deployment.Current.Dispatcher.BeginInvoke(new EventHandler(SafeStartMediaElement), null, null);
            /// Get first packet... have to wait for our rtp buffer to fill
            byte[] bData = stream.WaitNextPacketSample(true, stream.PTimeReceive * 5, out nMsTook);
            if ((bData != null) && (bData.Length > 0))
            {

                source.Write(bData);
            }
                 
            
            DateTime dtNextPacketExpected = DateTime.Now + tsPTime;

            System.Diagnostics.Stopwatch WaitPacketWatch = new System.Diagnostics.Stopwatch();
            int nDeficit = 0;
            while (IsCallActive == true)
            {
                bData = stream.WaitNextPacketSample(true, stream.PTimeReceive, out nMsTook);
                if ((bData != null) && (bData.Length > 0))
                {
                    source.Write(bData);
                }

                TimeSpan tsRemaining = dtNextPacketExpected - DateTime.Now;
                int nMsRemaining = (int)tsRemaining.TotalMilliseconds;
                if (nMsRemaining > 0)
                {
                    nMsRemaining += nDeficit;
                    if (nMsRemaining > 0)
                        System.Threading.Thread.Sleep(nMsRemaining);
                    else
                    {
                        nDeficit = nMsRemaining;
                    }
                }
                else
                    nDeficit += nMsRemaining;

                dtNextPacketExpected += tsPTime;
            }


            Deployment.Current.Dispatcher.BeginInvoke(new EventHandler(SafeStopMediaElement), null, null);
           Log("Done Receiving");
        }

       

        private void button3_Click(object sender, RoutedEventArgs e)
        {
            IsCallActive = false;
            ControlStream.SendMessage(myname, yourname, "end");
            EnableAll();
        }

        private void SignIn_Click(object sender, RoutedEventArgs e)
        {
           bool result = ControlStream.SignIn(textBox1.Text);
           int ind = (result.Equals(true)?0:1);
           string[] arr = new string[2];
           arr[0]="success";
           arr[1]="failure";
           Log("Signing in "+arr[ind] + '\n');
           myname = textBox1.Text;
           localEp = stream.FindMe();
           localcandidates = new IPEndPoint[2];
           localcandidates[0] = localEp;
           localcandidates[1] = stunEp;
           SignIn.IsEnabled = false;


           Thread WaitForCall = new Thread(new ThreadStart(WaitForCallFunction));
           WaitForCall.Name = "WaitForCallThread";
           WaitForCall.Start();
        }

        private void textBox1_TextChanged(object sender, TextChangedEventArgs e)
        {

        }


        private void button6_Click(object sender, RoutedEventArgs e)
        {
            button6.IsEnabled = false;
         
            button3.IsEnabled = true;
            String username = textBox2.Text;
            yourname = username;
            stream.SendMessage(myname, myname, "stop recv");
            remotecandidates = ControlStream.CallUser(username, myname, localEp.ToString() + ';' + stunEp.ToString());
            remote = stream.StartNeg(true, localcandidates, remotecandidates);
            if (remote != null)
            {
                Log(remote.ToString());
                button2.IsEnabled = false;
                StartCall();
            }
            else
            {
                Log("Negotiation failed");
                EnableAll();
            }
        }

        public void StartCall()
        {
            //stream init
            IsCallActive = true;
            stream.Start(remote, 50, 50);
            source = new AudioStreamSource();
            Log("Stream Initialised");
            //stream start recv
            SpeakerThread = new Thread(new ThreadStart(SpeakerThreadFunction));
            SpeakerThread.Name = "Speaker Thread";
            SpeakerThread.Start();

            MicrophoneThread = new Thread(new ThreadStart(MicrophoneThreadFunction));
            MicrophoneThread.Name = "Microphone Thread";
            MicrophoneThread.Start();

            Thread CallEndThread = new Thread(new ThreadStart(CallEndThreadFunction));
            CallEndThread.Name = "Call End Thread";
            CallEndThread.Start();

         
        }

        public void CallEndThreadFunction()
        {
            while (IsCallActive == true)
            {
                string s = ControlStream.ReceiveMessage();
                if (s == yourname + " end")
                    IsCallActive = false;
            }
            EnableAll();
        }


        public void EnableAll()
        {   Deployment.Current.Dispatcher.BeginInvoke( () => {
            button6.IsEnabled = true;
            button2.IsEnabled = true;
            button3.IsEnabled = false;
        });
        }
        public void MicrophoneThreadFunction()
        {
            StartMic();
            Log("Mic Started");
            int nSamplesPerPacket = stream.AudioCodec.AudioFormat.CalculateNumberOfSamplesForDuration(TimeSpan.FromMilliseconds(stream.PTimeTransmit));
            int nBytesPerPacket = nSamplesPerPacket * stream.AudioCodec.AudioFormat.BytesPerSample;
            TimeSpan tsPTime = TimeSpan.FromMilliseconds(stream.PTimeTransmit);
            DateTime dtNextPacketExpected = DateTime.Now + tsPTime;
            int nUnavailableAudioPackets = 0;
            while (IsCallActive == true)
            {
                dtNextPacketExpected = DateTime.Now + tsPTime;
                if (MicrophoneQueue.Size >= nBytesPerPacket)
                {
                    byte[] buffer = MicrophoneQueue.GetNSamples(nBytesPerPacket);
                    stream.SendNextSample(buffer);
                }
                else
                {
                    nUnavailableAudioPackets++;
                }

                if (MicrophoneQueue.Size > nBytesPerPacket * 6)
                    MicrophoneQueue.GetNSamples(MicrophoneQueue.Size - nBytesPerPacket * 5);

                TimeSpan tsRemaining = dtNextPacketExpected - DateTime.Now;
                int nMsRemaining = (int)tsRemaining.TotalMilliseconds;
                if (nMsRemaining > 0)
                {

                    System.Threading.Thread.Sleep(nMsRemaining);
                }
            }
            Log("Mic Stopped");
            StopMic();
        }

        byte[] buffer = new byte[16 * 40];
        void StartMic()
        {
            Microphone mic = Microphone.Default;
            buffer = new byte[mic.GetSampleSizeInBytes(TimeSpan.FromMilliseconds(100)) * 4];
            mic.BufferDuration = TimeSpan.FromMilliseconds(100);
            mic.BufferReady += new EventHandler<EventArgs>(mic_BufferReady);
            mic.Start();
        }

        void StopMic()
        {
            Microphone mic = Microphone.Default;
            mic.BufferReady -= new EventHandler<EventArgs>(mic_BufferReady);
            mic.Stop();
        }

        void mic_BufferReady(object sender, EventArgs e)
        {
            Microphone mic = Microphone.Default;
            int nSize = mic.GetData(buffer);
            MicrophoneQueue.AppendData(buffer, 0, nSize);
        }
       
        public void WaitForCallFunction()
        {
            remotecandidates = ControlStream.WaitForCall(localEp.ToString() + ';' + stunEp.ToString(), myname, ref yourname);
            if (remotecandidates == null) return;
            Deployment.Current.Dispatcher.BeginInvoke(() =>
            {

                button6.IsEnabled = false;
                button2.IsEnabled = false;
                button3.IsEnabled = true;
            });
            remote = stream.StartNeg(false, localcandidates, remotecandidates);
            if (remote != null)
            {
                Log(remote.ToString());
                StartCall();
            }
            else
            {
                Log("Negotiation failed");
                EnableAll();
            }
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            
            if (sstream.SignOut(myname))
            {
                SignIn.IsEnabled = true;
                mediaElement1.Stop();
                AudioStream = this.mediaElement1;
                InitializeStream();
                button3.IsEnabled = false;
                this.NavigationService.Navigate(new Uri("/MainPage.xaml", UriKind.Relative));
               
            }
        }

       



    }
}