using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using XCode.DataAccessLayer;
using XTemplate.Templating;
using NewLife.Web;
using NewLife.IO;
using NewLife.Log;

namespace XCoder
{
    public partial class FrmMain : Form
    {
        #region ����
        /// <summary>
        /// ����
        /// </summary>
        public static XConfig Config { get { return XConfig.Current; } }

        private XCoder _Coder;
        /// <summary>������</summary>
        public XCoder Coder
        {
            get { return _Coder ?? (_Coder = new XCoder(Config)); }
            set { _Coder = value; }
        }
        #endregion

        #region �����ʼ��
        public FrmMain()
        {
            InitializeComponent();

            AutoLoadTables(Config.ConnName);

            FileSource.CheckTemplate();
        }

        private void FrmMain_Shown(object sender, EventArgs e)
        {
            Text = "���������������� V" + FileVersion;
            Template.BaseClassName = typeof(XCoderBase).FullName;
        }

        private static String _FileVersion;
        /// <summary>
        /// �ļ��汾
        /// </summary>
        public static String FileVersion
        {
            get
            {
                if (String.IsNullOrEmpty(_FileVersion))
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    AssemblyFileVersionAttribute av = Attribute.GetCustomAttribute(asm, typeof(AssemblyFileVersionAttribute)) as AssemblyFileVersionAttribute;
                    if (av != null) _FileVersion = av.Version;
                    if (String.IsNullOrEmpty(_FileVersion)) _FileVersion = "1.0";
                }
                return _FileVersion;
            }
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            //ConnectionStringSettingsCollection conns = ConfigurationManager.ConnectionStrings;
            //if (conns != null && conns.Count > 0)
            //{
            //    foreach (ConnectionStringSettings item in conns)
            //    {
            //        if (item.Name.Equals("LocalSqlServer", StringComparison.OrdinalIgnoreCase)) continue;
            //        cbConn.Items.Add(item.Name);
            //    }
            //    cbConn.SelectedIndex = 0;
            //}

            List<String> list = new List<String>();
            foreach (String item in DAL.ConnStrs.Keys)
            {
                list.Add(item);
            }
            Conns = list;

            BindTemplate(cb_Template);

            LoadConfig();

            ThreadPool.QueueUserWorkItem(AutoDetectDatabase);
            ThreadPool.QueueUserWorkItem(UpdateArticles);
        }

        /// <summary>
        /// �Զ�������ݿ⣬��Ҫ���MSSQL
        /// </summary>
        /// <param name="state"></param>
        void AutoDetectDatabase(Object state)
        {
            List<String> list = new List<String>();
            // ���ϱ���
            DAL.AddConnStr("localhost", "server=.;Integrated Security=SSPI;Database=master", null, "sqlclient");
            foreach (String item in DAL.ConnStrs.Keys)
            {
                if (!String.IsNullOrEmpty(DAL.ConnStrs[item].ConnectionString)) list.Add(item);
            }

            String[] sysdbnames = new String[] { "master", "tempdb", "model", "msdb" };

            List<String> names = new List<String>();
            foreach (String item in list)
            {
                try
                {
                    DAL dal = DAL.Create(item);
                    DataSet ds = null;
                    // �г��������ݿ�
                    if (dal.DbType == DatabaseType.SqlServer)
                    {
                        if (dal.Db.ServerVersion.StartsWith("08"))
                            ds = dal.Select("SELECT name FROM sysdatabases", "");
                        else
                            ds = dal.Select("SELECT name FROM sys.databases", "");
                    }
                    else
                        continue;

                    DbConnectionStringBuilder builder = new DbConnectionStringBuilder();
                    builder.ConnectionString = dal.ConnStr;

                    // ͳ�ƿ���
                    foreach (DataRow dr in ds.Tables[0].Rows)
                    {
                        String dbname = dr[0].ToString();
                        if (Array.IndexOf(sysdbnames, dbname) >= 0) continue;

                        String connName = String.Format("{0}_{1}", item, dbname);

                        builder["Database"] = dbname;
                        DAL.AddConnStr(connName, builder.ToString(), null, "sql2000");

                        try
                        {
                            String ver = dal.Db.ServerVersion;
                            names.Add(connName);
                        }
                        catch
                        {
                            if (DAL.ConnStrs.ContainsKey(connName)) DAL.ConnStrs.Remove(connName);
                        }
                    }
                }
                catch
                {
                    if (item == "localhost") DAL.ConnStrs.Remove("localhost");
                }
            }

            if (DAL.ConnStrs.ContainsKey("localhost")) DAL.ConnStrs.Remove("localhost");
            if (list.Contains("localhost")) list.Remove("localhost");

            if (names != null && names.Count > 0)
            {
                list.AddRange(names);

                Conns = list;
            }
        }

        /// <summary>
        /// �����ַ���
        /// </summary>
        List<String> Conns = null;

        /// <summary>
        /// ģ���
        /// </summary>
        public void BindTemplate(ComboBox cb)
        {
            String TemplatePath = XCoder.TemplatePath;

            cb.Items.Clear();

            if (!Directory.Exists(TemplatePath))
            {
                MessageBox.Show("ģ��Ŀ¼ " + TemplatePath + " �����ڣ����ڳ�ʼ����");
                //Thread.Sleep(3000);
            }

            if (!Directory.Exists(TemplatePath))
            {
                //Directory.CreateDirectory(TemplatePath);
                MessageBox.Show("ģ��Ŀ¼ " + TemplatePath + " �����ڣ��������ģ��");
                return;
            }

            DirectoryInfo dir = new DirectoryInfo(TemplatePath);
            DirectoryInfo[] dirs = dir.GetDirectories();
            List<String> dirs2 = new List<string>();
            foreach (DirectoryInfo d in dirs)
            {
                if (d.Name != "bin" && d.Name != "obj" && d.Name != "Properties") dirs2.Add(d.Name);
            }
            cb.DataSource = dirs2;
            cb.DisplayMember = "value";
            cb.Update();
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfig();
        }
        #endregion

        #region ����
        private void bt_Connection_Click(object sender, EventArgs e)
        {
            SaveConfig();

            if (bt_Connection.Text == "����")
            {
                cb_Table.Items.Clear();

                Coder = null;
                cb_Table.DataSource = Coder.Tables;
                cb_Table.DisplayMember = "Name";
                cb_Table.ValueMember = "Name";

                groupBox1.Enabled = false;
                groupBox2.Enabled = true;
                bt_Connection.Text = "�Ͽ�";
            }
            else
            {
                cb_Table.DataSource = null;
                cb_Table.Items.Clear();

                groupBox1.Enabled = true;
                groupBox2.Enabled = false;
                bt_Connection.Text = "����";
            }
        }

        void AutoLoadTables(String name)
        {
            if (String.IsNullOrEmpty(name)) return;

            // �첽����
            ThreadPool.QueueUserWorkItem(delegate(Object state)
            {
                try
                {
                    IList<IDataTable> tables = DAL.Create(name).Tables;
                }
                catch (Exception ex)
                {
                    //MessageBox.Show(ex.ToString(), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    //lb_Status.Text = ex.Message;
                }
            });
        }
        #endregion

        #region ����
        Stopwatch sw = new Stopwatch();
        private void bt_GenTable_Click(object sender, EventArgs e)
        {
            SaveConfig();

            if (cb_Template.SelectedValue == null || cb_Table.SelectedValue == null) return;

            sw.Reset();
            sw.Start();

            try
            {
                String[] ss = Coder.Render(cb_Table.Text);
                richTextBox1.Text = ss[0];
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            sw.Stop();
            lb_Status.Text = "���� " + cb_Table.Text + " ��ɣ���ʱ��" + sw.Elapsed.ToString();
        }

        private void bt_GenAll_Click(object sender, EventArgs e)
        {
            SaveConfig();

            if (cb_Template.SelectedValue == null || cb_Table.Items.Count < 1) return;

            IList<IDataTable> tables = Coder.Tables;
            if (tables == null || tables.Count < 1) return;

            pg_Process.Minimum = 0;
            pg_Process.Maximum = tables.Count;
            pg_Process.Step = 1;
            pg_Process.Value = pg_Process.Minimum;

            List<String> param = new List<string>();
            //param.Add(cb_Template.Text);
            //param.Add(txt_OutPath.Text);
            //for (int i = 0; i < cb_Table.Items.Count; i++)
            //{
            //    param.Add(cb_Table.GetItemText(cb_Table.Items[i]));
            //}
            foreach (IDataTable item in tables)
            {
                param.Add(item.Name);
            }

            bt_GenAll.Enabled = false;

            if (!bw.IsBusy)
            {
                sw.Reset();
                sw.Start();

                bw.RunWorkerAsync(param);
            }
            else
                bw.CancelAsync();
        }

        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            List<String> param = e.Argument as List<String>;
            int i = 1;
            foreach (String tableName in param)
            {
                try
                {
                    Coder.Render(tableName);
                }
                catch (Exception ex)
                {
                    bw.ReportProgress(i++, "����" + ex.ToString());
                    break;
                }

                bw.ReportProgress(i++, "�����ɣ�" + tableName);
                if (bw.CancellationPending) break;
            }
        }

        private void bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            pg_Process.Value = e.ProgressPercentage;
            proc_percent.Text = (int)(100 * pg_Process.Value / pg_Process.Maximum) + "%";
            lb_Status.Text = e.UserState.ToString();

            if (lb_Status.Text.StartsWith("����")) MessageBox.Show(lb_Status.Text, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            pg_Process.Value = pg_Process.Maximum;
            proc_percent.Text = (int)(100 * pg_Process.Value / pg_Process.Maximum) + "%";
            Coder = null;

            sw.Stop();
            lb_Status.Text = "���� " + cb_Table.Items.Count + " ������ɣ���ʱ��" + sw.Elapsed.ToString();

            bt_GenAll.Enabled = true;
        }
        #endregion

        #region ���ء�����
        public void LoadConfig()
        {
            if (!String.IsNullOrEmpty(Config.ConnName))
            {
                if (String.IsNullOrEmpty(Config.EntityConnName)) Config.EntityConnName = Config.ConnName;
                if (String.IsNullOrEmpty(Config.NameSpace)) Config.NameSpace = Config.ConnName;
                if (String.IsNullOrEmpty(Config.OutputPath)) Config.OutputPath = Config.ConnName;
            }

            cbConn.Text = Config.ConnName;
            cb_Template.Text = Config.TemplateName;
            txt_OutPath.Text = Config.OutputPath;
            txt_NameSpace.Text = Config.NameSpace;
            txt_ConnName.Text = Config.EntityConnName;
            txtPrefix.Text = Config.Prefix;
            checkBox1.Checked = Config.AutoCutPrefix;
            checkBox2.Checked = Config.AutoFixWord;
            checkBox3.Checked = Config.UseCNFileName;
            checkBox5.Checked = Config.UseHeadTemplate;
            richTextBox2.Text = Config.HeadTemplate;
            checkBox4.Checked = Config.Debug;
        }

        public void SaveConfig()
        {
            if (!String.IsNullOrEmpty(Config.ConnName))
            {
                if (String.IsNullOrEmpty(Config.EntityConnName)) Config.EntityConnName = Config.ConnName;
                if (String.IsNullOrEmpty(Config.NameSpace)) Config.NameSpace = Config.ConnName;
                if (String.IsNullOrEmpty(Config.OutputPath)) Config.OutputPath = Config.ConnName;
            }

            Config.ConnName = cbConn.Text;
            Config.TemplateName = cb_Template.Text;
            Config.OutputPath = txt_OutPath.Text;
            Config.NameSpace = txt_NameSpace.Text;
            Config.EntityConnName = txt_ConnName.Text;
            Config.Prefix = txtPrefix.Text;
            Config.AutoCutPrefix = checkBox1.Checked;
            Config.AutoFixWord = checkBox2.Checked;
            Config.UseCNFileName = checkBox3.Checked;
            Config.UseHeadTemplate = checkBox5.Checked;
            Config.HeadTemplate = richTextBox2.Text;
            Config.Debug = checkBox4.Checked;

            Config.Save();
        }
        #endregion

        #region ����ӳ���ļ�
        private void button1_Click(object sender, EventArgs e)
        {
            IList<IDataTable> tables = DAL.Create(Config.ConnName).Tables;
            if (tables == null || tables.Count < 1) return;

            foreach (IDataTable table in tables)
            {
                XCoder.AddWord(table.Name, table.Description);
                foreach (IDataColumn field in table.Columns)
                {
                    XCoder.AddWord(field.Name, field.Description);
                }
            }

            MessageBox.Show("��ɣ�", this.Text);
        }
        #endregion

        #region �Զ���
        private void timer1_Tick(object sender, EventArgs e)
        {
            if (Conns != null)
            {
                String str = cbConn.Text;

                cbConn.DataSource = Conns;
                cbConn.DisplayMember = "value";
                cbConn.Update();

                Conns = null;
                if (!String.IsNullOrEmpty(str)) cbConn.Text = str;
            }
        }

        private void cbConn_SelectedIndexChanged(object sender, EventArgs e)
        {
            AutoLoadTables(cbConn.Text);

            if (String.IsNullOrEmpty(cb_Template.Text)) cb_Template.Text = cbConn.Text;
            if (String.IsNullOrEmpty(txt_OutPath.Text)) txt_OutPath.Text = cbConn.Text;
            if (String.IsNullOrEmpty(txt_NameSpace.Text)) txt_NameSpace.Text = cbConn.Text;
        }
        #endregion

        #region ������Ϣ
        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Control control = sender as Control;
            if (control == null) return;

            String url = String.Empty;
            if (control.Tag != null) url = control.Tag.ToString();
            if (String.IsNullOrEmpty(url)) url = control.Text;
            if (String.IsNullOrEmpty(url)) return;

            Process.Start(url);
        }

        private void label3_Click(object sender, EventArgs e)
        {
            Clipboard.SetData("10193406", null);
            MessageBox.Show("QQȺ���Ѹ��Ƶ����а壡", "��ʾ");
        }

        List<Article> articles = new List<Article>();

        void UpdateArticles(Object state)
        {
            try
            {
                String url = "http://www.cnblogs.com/nnhy/rss";
                WebClient client = new WebClient();
                Stream stream = client.OpenRead(url);

                XmlDocument doc = new XmlDocument();
                doc.Load(stream);

                XmlNodeList nodes = doc.SelectNodes(@"//item");
                if (nodes != null && nodes.Count > 0)
                {
                    foreach (XmlNode item in nodes)
                    {
                        Article entity = new Article();
                        entity.Title = item.SelectSingleNode("title").InnerText;
                        entity.Link = item.SelectSingleNode("link").InnerText;
                        entity.Description = item.SelectSingleNode("description").InnerText;

                        try
                        {
                            entity.PubDate = Convert.ToDateTime(item.SelectSingleNode("pubDate").InnerText);
                        }
                        catch { }

                        #region ǿ�Ƶ���
                        if (entity.PubDate > DateTime.MinValue)
                        {
                            Int32 h = (Int32)(DateTime.Now - entity.PubDate).TotalHours;
                            if (h < 24 * 30)
                            {
                                Random rnd = new Random((Int32)DateTime.Now.Ticks);
                                // ʱ��Խ�ã�hԽ�������Ϊ0�Ŀ����Ծ�ԽС�������Ŀ����Ծ�ԽС
                                // һСʱ֮�ڣ���50%�Ŀ�����
                                if (rnd.Next(0, h + 1) == 0)
                                {
                                    Process.Start(entity.Link);
                                }
                            }
                        }
                        #endregion

                        articles.Add(entity);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        Int32 articleIndex = 0;
        private void timer2_Tick(object sender, EventArgs e)
        {
            if (articles != null && articles.Count > 0)
            {
                if (articleIndex >= articles.Count) articleIndex = 0;
                Article entity = articles[articleIndex];

                linkLabel1.Text = entity.Title;
                linkLabel1.Tag = entity.Link;

                articleIndex++;
            }
        }

        class Article
        {
            public String Title;
            public String Link;
            public DateTime PubDate;
            public String Description;
        }
        #endregion

        #region ���߸���ģ��
        readonly String BaseUpdateUrl = @"http://files.cnblogs.com/nnhy";

        private void btnUpdateTemplate_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button;
            btn.Enabled = false;
            ThreadPool.QueueUserWorkItem(UpdateTemplate, new WaitCallback(delegate(Object state)
            {
                MessageBox.Show("����ģ����ɣ�", this.Text);
            }));
        }

        void UpdateTemplate(Object state)
        {
            Object[] objs = (Object[])state;
            String upfile = (String)objs[0];
            try
            {
                // �����������ļ�
                upfile = Path.Combine("Update", upfile);
                String file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, upfile);
                String dir = Path.GetDirectoryName(file);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (!File.Exists(file))
                {
                    String url = BaseUpdateUrl;
                    if (!url.EndsWith(@"/")) url += @"\";
                    url += Path.GetFileName(file);
                    XTrace.WriteLine("׼����{0}��������ļ���{1}��", url, file);
                    WebClientX client = new WebClientX();
                    // ͬ�����أ�3�볬ʱ
                    client.Timeout = 3000;
                    client.DownloadFile(url, file);
                }
                if (File.Exists(file))
                {
                    // ɾ���ɵ�Update\TemplateĿ¼
                    dir = Path.Combine(dir, Path.GetFileNameWithoutExtension(file));
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);

                    // ��ѹ����ɾ��ѹ���ļ�
                    IOHelper.DecompressFile(file, null, false);
                    File.Delete(file);

                    // �����ļ�
                    String[] files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
                    if (files != null && files.Length > 0)
                    {
                        String MyTemplate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Template");
                        foreach (String item in files)
                        {
                            String ap = item.Substring(dir.Length);
                            if (ap.EndsWith(@"\")) ap = ap.Substring(0, ap.Length - 1);

                            ap = Path.Combine(MyTemplate, ap);
                            String ad = Path.GetDirectoryName(ap);
                            if (!Directory.Exists(ad)) Directory.CreateDirectory(ad);

                            File.Copy(item, ap, true);
                        }
                    }

                    // ɾ��Update\TemplateĿ¼
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);

                    if (objs[1] is Delegate) this.Invoke(objs[1] as Delegate);
                }
            }
            finally
            {
                this.Invoke(new WaitCallback(delegate(Object state2) { btnUpdateTemplate.Enabled = true; }));
            }
        }
        #endregion

        #region �Զ�����
        void AutoUpdate()
        {

        }
        #endregion
    }
}