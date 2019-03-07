using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.Forms;
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using log4net;
using MissionPlanner;

namespace Xamarin
{
    public partial class MainPage : ContentPage
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static UdpClient client;

        private static Timer timer;

        public IServiceProvider Services { get; set; }

        public MainPage()
        {
            InitializeComponent();

            Task.Run(async () =>
            {
                try
                {
                    client = new UdpClient(14550, AddressFamily.InterNetwork);
                    client.BeginReceive(clientdata, client);
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            });
        }

        private void clientdata(IAsyncResult ar)
        {
            timer = null;
            var client = ((UdpClient) ar.AsyncState);

            if (client == null || client.Client == null)
                return;
            try
            {
                var port = ((IPEndPoint) client.Client.LocalEndPoint).Port;

                //if (client != null)
                //client.Close();

                var udpclient = new MissionPlanner.Comms.UdpSerial(client);


                var mav = new MAVLinkInterface();
                mav.BaseStream = udpclient;
                //MainV2.instance.doConnect(mav, "preset", port.ToString());
                //mav.getHeartBeat();

                Device.BeginInvokeOnMainThread(() =>
                {
                    Button1.Text = "here";
                    Button2.Text = "Now";
                });
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private void Button1_Pressed(object sender, EventArgs e)
        {

        }

        private void Button2_Pressed(object sender, EventArgs e)
        {

        }

        private void Button3_Pressed(object sender, EventArgs e)
        {

        }
    }
}