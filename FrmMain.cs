using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using XCode.DataAccessLayer;
using XTemplate.Templating;
using System.Text;

namespace XCoder
{
    public partial class FrmMain : Form
    {
        #region ����
        /// <summary>
        /// ����
        /// </summary>
        public static XConfig Config { get { return XConfig.Current; } }

        private Engine _Engine;
        /// <summary>������</summary>
        public Engine Engine
        {
            get { return _Engine ?? (_Engine = new Engine(Config)); }
            set { _Engine = value; }
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
            Text = "���������������� V" + Engine.FileVersion;
            Template.BaseClassName = typeof(XCoderBase).FullName;
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            List<String> list = new List<String>();
            foreach (String item in DAL.ConnStrs.Keys)
            {
                list.Add(item);
            }
            //Conns = list;
            SetDatabaseList(list);

            BindTemplate(cb_Template);

            LoadConfig();

            ThreadPool.QueueUserWorkItem(AutoDetectDatabase);
            ThreadPool.QueueUserWorkItem(UpdateArticles);

            if (Config.LastUpdate.Date < DateTime.Now.Date)
            {
                Config.LastUpdate = DateTime.Now;

                AutoUpdate au = new AutoUpdate();
                au.LocalVersion = new Version(Engine.FileVersion);
                au.VerSrc = "http://files.cnblogs.com/nnhy/XCoderVer.xml";
                au.ProcessAsync();
            }

            String url = "http://www.7765.com/api/";
            url += String.Format("?tag=XCoder_v{0}&r={1}", Engine.FileVersion, DateTime.Now.Ticks);
            webBrowser1.Navigate(url);
        }

        /// <summary>
        /// �Զ�������ݿ⣬��Ҫ���MSSQL
        /// </summary>
        /// <param name="state"></param>
        void AutoDetectDatabase(Object state)
        {
            List<String> list = new List<String>();

            // ���ϱ���MSSQL
            String localstr = "Data Source=.;Initial Catalog=master;Integrated Security=True;";
            if (!ContainConnStr(localstr)) DAL.AddConnStr("local_MSSQL", localstr, null, "sqlclient");

            // ��ӱ���Access��SQLite
            String[] ss = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.*", SearchOption.TopDirectoryOnly);
            foreach (String item in ss)
            {
                String access = "Standard Jet DB";
                String sqlite = "SQLite";

                using (FileStream fs = new FileStream(item, FileMode.Open, FileAccess.Read))
                {
                    BinaryReader reader = new BinaryReader(fs);
                    Byte[] bts = reader.ReadBytes(sqlite.Length);
                    if (bts[0] == 'S' && bts[1] == 'Q' && Encoding.ASCII.GetString(bts) == sqlite)
                    {
                        localstr = String.Format("Data Source={0};", item);
                        if (!ContainConnStr(localstr)) DAL.AddConnStr("SQLite_" + Path.GetFileNameWithoutExtension(item), localstr, null, "SQLite");
                    }
                    else if (bts[4] == 'S' && bts[5] == 't')
                    {
                        fs.Seek(4, SeekOrigin.Begin);
                        bts = reader.ReadBytes(access.Length);
                        if (Encoding.ASCII.GetString(bts) == access)
                        {
                            localstr = String.Format("Provider=Microsoft.Jet.OLEDB.4.0; Data Source={0};Persist Security Info=False;OLE DB Services=-1", item);
                            if (!ContainConnStr(localstr)) DAL.AddConnStr("Access_" + Path.GetFileNameWithoutExtension(item), localstr, null, "Access");
                        }
                    }
                }
            }

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

                //Conns = list;
                this.Invoke(new Action<List<String>>(SetDatabaseList), list);
            }
        }

        Boolean ContainConnStr(String connstr)
        {
            foreach (String item in DAL.ConnStrs.Keys)
            {
                if (String.Equals(connstr, DAL.ConnStrs[item].ConnectionString, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        void SetDatabaseList(List<String> list)
        {
            String str = cbConn.Text;

            cbConn.DataSource = list;
            cbConn.DisplayMember = "value";
            cbConn.Update();

            //Conns = null;
            if (!String.IsNullOrEmpty(str)) cbConn.Text = str;
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
            String TemplatePath = Engine.TemplatePath;

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
                cbTableList.Items.Clear();

                Engine = null;
                Engine.Tables = DAL.Create(Config.ConnName).Tables;

                cbTableList.DataSource = Engine.Tables;
                cbTableList.DisplayMember = "Name";
                cbTableList.ValueMember = "Name";

                gbConnect.Enabled = false;
                gbTable.Enabled = true;
                bt_Connection.Text = "�Ͽ�";

                btnImport.Text = "�����ܹ�";
            }
            else
            {
                cbTableList.DataSource = null;
                cbTableList.Items.Clear();

                gbConnect.Enabled = true;
                gbTable.Enabled = false;
                bt_Connection.Text = "����";

                btnImport.Text = "����ܹ�";
            }
        }

        private void btnImport_Click(object sender, EventArgs e)
        {
            if (btnImport.Text == "����ܹ�")
            {
                if (openFileDialog1.ShowDialog() != DialogResult.OK || String.IsNullOrEmpty(openFileDialog1.FileName)) return;
                try
                {
                    List<IDataTable> list = DAL.Import(File.ReadAllText(openFileDialog1.FileName));

                    Engine = null;
                    Engine.Tables = list;

                    cbTableList.DataSource = Engine.Tables;
                    cbTableList.DisplayMember = "Name";
                    cbTableList.ValueMember = "Name";

                    gbTable.Enabled = true;

                    MessageBox.Show("����ܹ��ɹ�����" + (list == null ? 0 : list.Count) + "�ű�", "����ܹ�", MessageBoxButtons.OK);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                if (!String.IsNullOrEmpty(Config.ConnName))
                {
                    String file = Config.ConnName + ".xml";
                    String dir = null;
                    if (!String.IsNullOrEmpty(saveFileDialog1.FileName))
                        dir = Path.GetDirectoryName(saveFileDialog1.FileName);
                    if (String.IsNullOrEmpty(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
                    saveFileDialog1.FileName = Path.Combine(dir, file);
                }
                if (saveFileDialog1.ShowDialog() != DialogResult.OK || String.IsNullOrEmpty(saveFileDialog1.FileName)) return;
                try
                {
                    //String xml = DAL.Create(Config.ConnName).Export();
                    String xml = DAL.Export(Engine.Tables);
                    File.WriteAllText(saveFileDialog1.FileName, xml);

                    MessageBox.Show("�����ܹ��ɹ���", "�����ܹ�", MessageBoxButtons.OK);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
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
                catch //(Exception ex)
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

            if (cb_Template.SelectedValue == null || cbTableList.SelectedValue == null) return;

            sw.Reset();
            sw.Start();

            try
            {
                //Engine.FixTable();
                String[] ss = Engine.Render(cbTableList.Text);
                //richTextBox1.Text = ss[0];
            }
            catch (TemplateException ex)
            {
                MessageBox.Show(ex.Message, "ģ�����", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            sw.Stop();
            lb_Status.Text = "���� " + cbTableList.Text + " ��ɣ���ʱ��" + sw.Elapsed.ToString();
        }

        private void bt_GenAll_Click(object sender, EventArgs e)
        {
            SaveConfig();

            if (cb_Template.SelectedValue == null || cbTableList.Items.Count < 1) return;

            IList<IDataTable> tables = Engine.Tables;
            if (tables == null || tables.Count < 1) return;

            pg_Process.Minimum = 0;
            pg_Process.Maximum = tables.Count;
            pg_Process.Step = 1;
            pg_Process.Value = pg_Process.Minimum;

            List<String> param = new List<string>();
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
            //Engine.FixTable();
            foreach (String tableName in param)
            {
                try
                {
                    Engine.Render(tableName);
                }
                catch (TemplateException ex)
                {
                    bw.ReportProgress(i++, "����" + ex.Message);
                    break;
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
            Engine = null;

            sw.Stop();
            lb_Status.Text = "���� " + cbTableList.Items.Count + " ������ɣ���ʱ��" + sw.Elapsed.ToString();

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
                Engine.AddWord(table.Name, table.Description);
                foreach (IDataColumn field in table.Columns)
                {
                    Engine.AddWord(field.Name, field.Description);
                }
            }

            MessageBox.Show("��ɣ�", this.Text);
        }
        #endregion

        #region �Զ���
        private void timer1_Tick(object sender, EventArgs e)
        {
            //if (Conns != null)
            //{
            //    String str = cbConn.Text;

            //    cbConn.DataSource = Conns;
            //    cbConn.DisplayMember = "value";
            //    cbConn.Update();

            //    Conns = null;
            //    if (!String.IsNullOrEmpty(str)) cbConn.Text = str;
            //}
        }

        private void cbConn_SelectedIndexChanged(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(cbConn, DAL.Create(cbConn.Text).ConnStr);

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

        #region �����Ŀ¼
        private void btnOpenOutputDir_Click(object sender, EventArgs e)
        {
            String dir = txt_OutPath.Text;
            dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir);

            Process.Start("explorer.exe", "/root,\"" + dir + "\"");
            //Process.Start("explorer.exe", "/select," + dir);
        }
        #endregion
    }
}