﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using NewLife;
using NewLife.Log;
using NewLife.Net;
using NewLife.Net.Sockets;
using NewLife.Reflection;
using NewLife.Threading;
using XCoder;

namespace XNet
{
    public partial class FrmMain : Form
    {
        NetServer _Server;
        TcpSession _Tcp;
        UdpServer _Udp;

        #region 窗体
        public FrmMain()
        {
            InitializeComponent();

            this.Icon = IcoHelper.GetIcon("网络");
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            txtReceive.UseWinFormControl();
            //NetHelper.Debug = true;

            txtReceive.SetDefaultStyle(12);
            txtSend.SetDefaultStyle(12);
            numMutilSend.SetDefaultStyle(12);

            gbReceive.Tag = gbReceive.Text;
            gbSend.Tag = gbSend.Text;

            var list = EnumHelper.GetDescriptions<WorkModes>().Select(kv => kv.Value).ToList();
            foreach (var item in GetNetServers())
            {
                list.Add(item.Name);
            }
            cbMode.DataSource = list;
            cbMode.SelectedIndex = 0;

            cbAddr.DropDownStyle = ComboBoxStyle.DropDownList;
            cbAddr.DataSource = GetIPs();

            var config = NetConfig.Current;
            if (config.Port > 0) numPort.Value = config.Port;

            // 加载保存的颜色
            UIConfig.Apply(txtReceive);

            LoadConfig();
        }
        #endregion

        #region 加载/保存 配置
        void LoadConfig()
        {
            var cfg = NetConfig.Current;
            mi显示发送数据.Checked = cfg.ShowSend;
            mi显示接收数据.Checked = cfg.ShowReceive;
            mi显示统计信息.Checked = cfg.ShowStat;
        }

        void SaveConfig()
        {
            var cfg = NetConfig.Current;
            cfg.ShowSend = mi显示发送数据.Checked;
            cfg.ShowReceive = mi显示接收数据.Checked;
            cfg.ShowStat = mi显示统计信息.Checked;
            cfg.Save();
        }
        #endregion

        #region 收发数据
        void Connect()
        {
            _Server = null;
            _Tcp = null;
            _Udp = null;

            var port = (Int32)numPort.Value;

            var config = NetConfig.Current;
            config.Port = port;

            var mode = GetMode();
            switch (mode)
            {
                case WorkModes.UDP_TCP:
                    CreateServer(port);
                    _Server.Start();
                    break;
                case WorkModes.UDP_Server:
                    CreateServer(port);
                    _Server.ProtocolType = ProtocolType.Udp;
                    _Server.Start();
                    break;
                case WorkModes.TCP_Server:
                    CreateServer(port);
                    _Server.ProtocolType = ProtocolType.Tcp;
                    _Server.Start();
                    break;
                case WorkModes.TCP_Client:
                    var tcp = new TcpSession();
                    tcp.Log = XTrace.Log;
                    tcp.Received += OnReceived;
                    tcp.Remote.Port = port;
                    tcp.Remote.Host = cbAddr.Text;
                    tcp.Open();
                    _Tcp = tcp;

                    config.Address = cbAddr.Text;
                    break;
                case WorkModes.UDP_Client:
                    var udp = new UdpServer();
                    udp.Log = XTrace.Log;
                    udp.Received += OnReceived;
                    udp.Remote.Port = port;
                    udp.Remote.Host = cbAddr.Text;
                    udp.Open();
                    _Udp = udp;

                    config.Address = cbAddr.Text;
                    break;
                default:
                    if ((Int32)mode > 0)
                    {
                        var ns = GetNetServers().Where(n => n.Name == cbMode.Text).FirstOrDefault();
                        if (ns == null) throw new XException("未识别服务[{0}]", mode);

                        _Server = ns.GetType().CreateInstance() as NetServer;
                        CreateServer(port);
                        _Server.Start();
                    }
                    break;
            }

            pnlSetting.Enabled = false;
            btnConnect.Text = "关闭";

            config.Save();

            BizLog = TextFileLog.Create("NetLog");
        }

        void CreateServer(Int32 port)
        {
            if (_Server == null) _Server = new NetServer();
            _Server.Log = XTrace.Log;
            _Server.Port = port;
            if (!cbAddr.Text.Contains("所有本地")) _Server.Local.Host = cbAddr.Text;
            _Server.Received += OnReceived;

            var cfg = NetConfig.Current;
            _Server.LogSend = cfg.ShowSend;
            _Server.LogReceive = cfg.ShowReceive;
        }

        void Disconnect()
        {
            var tcp = _Tcp;
            if (tcp != null)
            {
                _Tcp = null;
                tcp.Dispose();
            }
            var udp = _Udp;
            if (udp != null)
            {
                _Udp = null;
                udp.Dispose();
            }
            if (_Server != null)
            {
                _Server.Dispose();
                _Server = null;
            }

            pnlSetting.Enabled = true;
            btnConnect.Text = "打开";
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            SaveConfig();

            var btn = sender as Button;
            if (btn.Text == "打开")
                Connect();
            else
                Disconnect();
        }

        /// <summary>业务日志输出</summary>
        ILog BizLog;

        void OnReceived(Object sender, ReceivedEventArgs e)
        {
            var session = sender as ISocketSession;
            if (session == null)
            {
                var ns = sender as INetSession;
                if (ns == null) return;
                session = ns.Session;
            }

            //var line = String.Format("{0} [{1}]: {2}", session.Remote, e.Length, e.ToHex());
            //XTrace.UseWinFormWriteLog(txtReceive, line, 100000);
            //TextControlLog.WriteLog(txtReceive, line);
            var line = e.ToStr();
            XTrace.WriteLine(line);
            //XTrace.WriteLine(e.ToStr());

            if (BizLog != null) BizLog.Info(line);
        }

        Int32 _pColor = 0;
        Int32 BytesOfReceived = 0;
        Int32 BytesOfSent = 0;
        Int32 lastReceive = 0;
        Int32 lastSend = 0;
        private void timer1_Tick(object sender, EventArgs e)
        {
            if (!pnlSetting.Enabled)
            {
                //    // 检查串口是否已断开，自动关闭已断开的串口，避免内存暴涨
                //    if (!spList.Enabled && btnConnect.Text == "打开")
                //    {
                //        Disconnect();
                //        return;
                //    }

                var rcount = BytesOfReceived;
                var tcount = BytesOfSent;
                if (rcount != lastReceive)
                {
                    gbReceive.Text = (gbReceive.Tag + "").Replace("0", rcount + "");
                    lastReceive = rcount;
                }
                if (tcount != lastSend)
                {
                    gbSend.Text = (gbSend.Tag + "").Replace("0", tcount + "");
                    lastSend = tcount;
                }

                if (cbColor.Checked) txtReceive.ColourDefault(_pColor);
                _pColor = txtReceive.TextLength;
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            var str = txtSend.Text;
            if (String.IsNullOrEmpty(str))
            {
                MessageBox.Show("发送内容不能为空！", this.Text);
                txtSend.Focus();
                return;
            }

            // 多次发送
            var count = (Int32)numMutilSend.Value;
            var sleep = (Int32)numSleep.Value;
            if (count <= 0) count = 1;
            if (sleep <= 0) sleep = 100;

            // 处理换行
            str = str.Replace("\n", "\r\n");

            ThreadPoolX.QueueUserWorkItem(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    if (_Tcp != null)
                        _Tcp.Send(str);
                    else if (_Udp != null)
                        _Udp.Send(str);

                    if (count > 1) Thread.Sleep(sleep);
                }
            });
        }
        #endregion

        #region 右键菜单
        private void mi清空_Click(object sender, EventArgs e)
        {
            txtReceive.Clear();
            //spList.ClearReceive();
        }

        private void mi清空2_Click(object sender, EventArgs e)
        {
            txtSend.Clear();
            //spList.ClearSend();
        }

        private void mi显示发送数据_Click(object sender, EventArgs e)
        {
            var mi = sender as ToolStripMenuItem;
            mi.Checked = !mi.Checked;
        }

        private void mi显示接收数据_Click(object sender, EventArgs e)
        {
            var mi = sender as ToolStripMenuItem;
            mi.Checked = !mi.Checked;
        }

        private void mi显示统计信息_Click(object sender, EventArgs e)
        {
            var mi = sender as ToolStripMenuItem;
            mi.Checked = !mi.Checked;
        }
        #endregion

        private void cbMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            var mode = GetMode();
            if ((Int32)mode == 0) return;

            switch (mode)
            {
                case WorkModes.TCP_Client:
                case WorkModes.UDP_Client:
                    cbAddr.DropDownStyle = ComboBoxStyle.DropDown;
                    cbAddr.DataSource = null;
                    cbAddr.Items.Clear();
                    cbAddr.Text = NetConfig.Current.Address;
                    break;
                default:
                case WorkModes.UDP_TCP:
                case WorkModes.UDP_Server:
                case WorkModes.TCP_Server:
                    cbAddr.DropDownStyle = ComboBoxStyle.DropDownList;
                    cbAddr.DataSource = GetIPs();
                    break;
                case (WorkModes)0xFF:
                    cbAddr.DropDownStyle = ComboBoxStyle.DropDownList;
                    cbAddr.DataSource = GetIPs();

                    // 端口
                    var ns = GetNetServers().Where(n => n.Name == cbMode.Text).FirstOrDefault();
                    if (ns != null) numPort.Value = ns.Port;

                    break;
            }
        }

        WorkModes GetMode()
        {
            var mode = cbMode.Text;
            if (String.IsNullOrEmpty(mode)) return (WorkModes)0;

            var list = EnumHelper.GetDescriptions<WorkModes>().Where(kv => kv.Value == mode).ToList();
            if (list.Count == 0) return (WorkModes)0xFF;

            return (WorkModes)list[0].Key;
        }

        static String[] GetIPs()
        {
            var list = NetHelper.GetIPs().Select(e => e.ToString()).ToList();
            list.Insert(0, "所有本地IPv4/IPv6");
            list.Insert(1, IPAddress.Any.ToString());
            list.Insert(2, IPAddress.IPv6Any.ToString());

            return list.ToArray();
        }

        static NetServer[] _ns;
        static NetServer[] GetNetServers()
        {
            if (_ns != null) return _ns;

            var list = new List<NetServer>();
            foreach (var item in typeof(NetServer).GetAllSubclasses(true))
            {
                var ns = item.CreateInstance() as NetServer;
                if (ns != null) list.Add(ns);
            }

            return _ns = list.ToArray();
        }
    }
}