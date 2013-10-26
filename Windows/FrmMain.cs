using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using NewLife.Log;
using NewLife.Reflection;
using NewLife.Threading;
using XCode.DataAccessLayer;
using XTemplate.Templating;

namespace XCoder
{
    public partial class FrmMain : Form
    {
        #region ����
        /// <summary>����</summary>
        public static XConfig Config { get { return XConfig.Current; } }

        private Engine _Engine;
        /// <summary>������</summary>
        Engine Engine
        {
            get { return _Engine ?? (_Engine = new Engine(Config)); }
            set { _Engine = value; }
        }
        #endregion

        #region �����ʼ��
        public FrmMain()
        {
            InitializeComponent();

            this.Icon = IcoHelper.GetIcon("ģ��");

            AutoLoadTables(Config.ConnName);
        }

        private void FrmMain_Shown(object sender, EventArgs e)
        {
            //var asm = AssemblyX.Create(Assembly.GetExecutingAssembly());
            //Text = String.Format("����������ģ�͹��� v{0} {1:HH:mm:ss}����", asm.CompileVersion, asm.Compile);
        }

        private void FrmMain_Load(object sender, EventArgs e)
        {
            LoadConfig();

            try
            {
                SetDatabaseList(DAL.ConnStrs.Keys.ToList());

                BindTemplate(cb_Template);
            }
            catch (Exception ex)
            {
                XTrace.WriteException(ex);
            }

            LoadConfig();

            ThreadPoolX.QueueUserWorkItem(AutoDetectDatabase, null);
        }

        private void FrmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                SaveConfig();
            }
            catch { }
        }
        #endregion

        #region ���ӡ��Զ�������ݿ⡢���ر�
        private void bt_Connection_Click(object sender, EventArgs e)
        {
            SaveConfig();

            if (bt_Connection.Text == "����")
            {
                Engine = null;
                LoadTables();

                gbConnect.Enabled = false;
                gbTable.Enabled = true;
                ģ��ToolStripMenuItem.Visible = true;
                �ܹ�����SToolStripMenuItem.Visible = true;
                //btnImport.Enabled = false;
                btnImport.Text = "����ģ��";
                bt_Connection.Text = "�Ͽ�";
                btnRefreshTable.Enabled = true;
            }
            else
            {
                SetTables(null);

                gbConnect.Enabled = true;
                gbTable.Enabled = false;
                ģ��ToolStripMenuItem.Visible = false;
                �ܹ�����SToolStripMenuItem.Visible = false;
                btnImport.Enabled = true;
                btnImport.Text = "����ģ��";
                bt_Connection.Text = "����";
                btnRefreshTable.Enabled = false;
                Engine = null;

                // �Ͽ���ʱ����ȡһ�Σ�ȷ���´��ܼ�ʱ�õ��µ�
                try
                {
                    var list = DAL.Create(Config.ConnName).Tables;
                }
                catch { }
            }
        }

        private void cbConn_SelectionChangeCommitted(object sender, EventArgs e)
        {
            if (!String.IsNullOrEmpty(cbConn.Text)) toolTip1.SetToolTip(cbConn, DAL.Create(cbConn.Text).ConnStr);

            AutoLoadTables(cbConn.Text);

            if (String.IsNullOrEmpty(cb_Template.Text)) cb_Template.Text = cbConn.Text;
            if (String.IsNullOrEmpty(txt_OutPath.Text)) txt_OutPath.Text = cbConn.Text;
            if (String.IsNullOrEmpty(txt_NameSpace.Text)) txt_NameSpace.Text = cbConn.Text;
        }

        void AutoDetectDatabase()
        {
            var list = new List<String>();

            // ���ϱ���MSSQL
            String localName = "local_MSSQL";
            String localstr = "Data Source=.;Initial Catalog=master;Integrated Security=True;";
            if (!ContainConnStr(localstr)) DAL.AddConnStr(localName, localstr, null, "mssql");

            var sw = new Stopwatch();
            sw.Start();

            #region ��Ȿ��Access��SQLite
            var n = 0;
            String[] ss = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.*", SearchOption.TopDirectoryOnly);
            foreach (String item in ss)
            {
                String ext = Path.GetExtension(item);
                //if (ext.EqualIC(".exe")) continue;
                //if (ext.EqualIC(".dll")) continue;
                //if (ext.EqualIC(".zip")) continue;
                //if (ext.EqualIC(".rar")) continue;
                //if (ext.EqualIC(".txt")) continue;
                //if (ext.EqualIC(".config")) continue;
                if (ext.EqualIgnoreCase(".exe", ".dll", ".zip", ".rar", ".txt", ".config")) continue;

                try
                {
                    if (DetectFileDb(item)) n++;
                }
                catch (Exception ex) { XTrace.WriteException(ex); }
            }
            #endregion

            sw.Stop();
            XTrace.WriteLine("�Զ�����ļ�{0}�����������ݿ�{1}������ʱ��{2}��", ss.Length, n, sw.Elapsed);

            foreach (var item in DAL.ConnStrs)
            {
                if (!String.IsNullOrEmpty(item.Value.ConnectionString)) list.Add(item.Key);
            }

            // Զ�����ݿ��ʱ̫�����������г���
            this.Invoke(new Action<List<String>>(SetDatabaseList), list);
            //!!! ��������ʵ����һ���б�������Ϊ����Դ��ʱ������Ϊ��ͬһ�������������
            list = new List<String>(list);

            sw.Reset();
            sw.Start();

            #region ̽�������е�������
            var sysdbnames = new String[] { "master", "tempdb", "model", "msdb" };
            n = 0;
            var names = new List<String>();
            foreach (var item in list)
            {
                try
                {
                    var dal = DAL.Create(item);
                    if (dal.DbType != DatabaseType.SqlServer) continue;

                    DataTable dt = null;
                    String dbprovider = null;

                    // �г��������ݿ�
                    Boolean old = DAL.ShowSQL;
                    DAL.ShowSQL = false;
                    try
                    {
                        if (dal.Db.CreateMetaData().MetaDataCollections.Contains("Databases"))
                        {
                            dt = dal.Db.CreateSession().GetSchema("Databases", null);
                            dbprovider = dal.DbType.ToString();
                        }
                    }
                    finally { DAL.ShowSQL = old; }

                    if (dt == null) continue;

                    var builder = new DbConnectionStringBuilder();
                    builder.ConnectionString = dal.ConnStr;

                    // ͳ�ƿ���
                    foreach (DataRow dr in dt.Rows)
                    {
                        String dbname = dr[0].ToString();
                        if (Array.IndexOf(sysdbnames, dbname) >= 0) continue;

                        String connName = String.Format("{0}_{1}", item, dbname);

                        builder["Database"] = dbname;
                        DAL.AddConnStr(connName, builder.ToString(), null, dbprovider);
                        n++;

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
                    if (item == localName) DAL.ConnStrs.Remove(localName);
                }
            }
            #endregion

            sw.Stop();
            XTrace.WriteLine("����Զ�����ݿ�{0}������ʱ��{1}��", n, sw.Elapsed);

            if (DAL.ConnStrs.ContainsKey(localName)) DAL.ConnStrs.Remove(localName);
            if (list.Contains(localName)) list.Remove(localName);

            if (names != null && names.Count > 0)
            {
                list.AddRange(names);

                this.Invoke(new Action<List<String>>(SetDatabaseList), list);
            }
        }

        Boolean DetectFileDb(String item)
        {
            String access = "Standard Jet DB";
            String sqlite = "SQLite";

            using (var fs = new FileStream(item, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (fs.Length <= 0) return false;

                var reader = new BinaryReader(fs);
                var bts = reader.ReadBytes(sqlite.Length);
                if (bts != null && bts.Length > 0)
                {
                    if (bts[0] == 'S' && bts[1] == 'Q' && Encoding.ASCII.GetString(bts) == sqlite)
                    {
                        var localstr = String.Format("Data Source={0};", item);
                        if (!ContainConnStr(localstr)) DAL.AddConnStr("SQLite_" + Path.GetFileNameWithoutExtension(item), localstr, null, "SQLite");
                        return true;
                    }
                    else if (bts.Length > 5 && bts[4] == 'S' && bts[5] == 't')
                    {
                        fs.Seek(4, SeekOrigin.Begin);
                        bts = reader.ReadBytes(access.Length);
                        if (Encoding.ASCII.GetString(bts) == access)
                        {
                            var localstr = String.Format("Provider=Microsoft.Jet.OLEDB.4.0; Data Source={0};Persist Security Info=False", item);
                            if (!ContainConnStr(localstr)) DAL.AddConnStr("Access_" + Path.GetFileNameWithoutExtension(item), localstr, null, "Access");
                            return true;
                        }
                    }
                }

                if (fs.Length > 20)
                {
                    fs.Seek(16, SeekOrigin.Begin);
                    var ver = reader.ReadInt32();
                    if (ver == 0x73616261 ||
                        ver == 0x002dd714 ||
                        ver == 0x00357b9d ||
                        ver == 0x003d0900
                        )
                    {
                        var localstr = String.Format("Data Source={0};", item);
                        if (!ContainConnStr(localstr)) DAL.AddConnStr("SqlCe_" + Path.GetFileNameWithoutExtension(item), localstr, null, "SqlCe");
                        return true;
                    }
                }
            }

            return false;
        }

        Boolean ContainConnStr(String connstr)
        {
            foreach (var item in DAL.ConnStrs)
            {
                if (connstr.EqualIgnoreCase(item.Value.ConnectionString)) return true;
            }
            return false;
        }

        void SetDatabaseList(List<String> list)
        {
            String str = cbConn.Text;

            cbConn.DataSource = list;
            cbConn.DisplayMember = "value";

            if (!String.IsNullOrEmpty(str)) cbConn.Text = str;

            if (!String.IsNullOrEmpty(Config.ConnName))
            {
                cbConn.SelectedText = Config.ConnName;
            }

            if (cbConn.SelectedIndex < 0 && cbConn.Items != null && cbConn.Items.Count > 0) cbConn.SelectedIndex = 0;
        }

        void LoadTables()
        {
            try
            {
                var list = DAL.Create(Config.ConnName).Tables;
                if (!cbIncludeView.Checked) list = list.Where(t => !t.IsView).ToList();
                if (Config.NeedFix) list = Engine.FixTable(list);
                Engine.Tables = list;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), this.Text);
                return;
            }

            SetTables(null);
            SetTables(Engine.Tables);
        }

        void SetTables(Object source)
        {
            if (source == null)
            {
                cbTableList.DataSource = source;
                cbTableList.Items.Clear();
                return;
            }
            var list = source as List<IDataTable>;
            if (list[0].DbType == DatabaseType.SqlServer) // ���Ӷ�SqlServer 2000�����⴦��  ahuang
            {
                //list.Remove(list.Find(delegate(IDataTable p) { return p.Name == "dtproperties"; }));
                //list.Remove(list.Find(delegate(IDataTable p) { return p.Name == "sysconstraints"; }));
                //list.Remove(list.Find(delegate(IDataTable p) { return p.Name == "syssegments"; }));
                //list.RemoveAll(delegate(IDataTable p) { return p.Description.Contains("[0E232FF0-B466-"); });
                list.RemoveAll(dt => dt.Name == "dtproperties" || dt.Name == "sysconstraints" || dt.Name == "syssegments" || dt.Description.Contains("[0E232FF0-B466-"));
            }

            // ����ǰ�����գ���������������Դ���õ�һ�ΰ󶨿ؼ���Ȼ��ʵ�����������һ��
            //cbTableList.DataSource = source;
            cbTableList.Items.Clear();
            if (source != null)
            {
                // ��������
                var tables = source as List<IDataTable>;
                if (tables == null)
                    cbTableList.DataSource = source;
                else
                {
                    tables.Sort((t1, t2) => t1.Name.CompareTo(t2.Name));
                    cbTableList.DataSource = tables;
                }
                ////cbTableList.DisplayMember = "Name";
                //cbTableList.ValueMember = "Name";
            }
            cbTableList.Update();
        }

        void AutoLoadTables(String name)
        {
            if (String.IsNullOrEmpty(name)) return;
            //if (!DAL.ConnStrs.ContainsKey(name) || String.IsNullOrEmpty(DAL.ConnStrs[name].ConnectionString)) return;
            ConnectionStringSettings setting;
            if (!DAL.ConnStrs.TryGetValue(name, out setting) || setting.ConnectionString.IsNullOrWhiteSpace()) return;

            // �첽����
            ThreadPoolX.QueueUserWorkItem(delegate(Object state) { IList<IDataTable> tables = DAL.Create((String)state).Tables; }, name, null);
        }

        private void btnRefreshTable_Click(object sender, EventArgs e)
        {
            LoadTables();
        }
        #endregion

        #region ����
        Stopwatch sw = new Stopwatch();
        private void bt_GenTable_Click(object sender, EventArgs e)
        {
            SaveConfig();

            if (cb_Template.SelectedValue == null || cbTableList.SelectedValue == null) return;

            var table = cbTableList.SelectedValue as IDataTable;
            if (table == null) return;

            sw.Reset();
            sw.Start();

            try
            {
                var ss = Engine.Render(table);

                MessageBox.Show("����" + table + "�ɹ���", "�ɹ�", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            lb_Status.Text = "���� " + cbTableList.Text + " ��ɣ���ʱ��" + sw.Elapsed;
        }

        private void bt_GenAll_Click(object sender, EventArgs e)
        {
            SaveConfig();

            if (cb_Template.SelectedValue == null || cbTableList.Items.Count < 1) return;

            var tables = Engine.Tables;
            if (tables == null || tables.Count < 1) return;

            sw.Reset();
            sw.Start();

            foreach (var tb in tables)
            {
                Engine.Render(tb);
            }

            sw.Stop();
            lb_Status.Text = "���� " + tables.Count + " ������ɣ���ʱ��" + sw.Elapsed.ToString();

            MessageBox.Show("����" + tables.Count + " ����ɹ���", "�ɹ�", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        #endregion

        #region ���ء�����
        public void LoadConfig()
        {
            cbConn.Text = Config.ConnName;
            cb_Template.Text = Config.TemplateName;
            txt_OutPath.Text = Config.OutputPath;
            txt_NameSpace.Text = Config.NameSpace;
            txt_ConnName.Text = Config.EntityConnName;
            txtBaseClass.Text = Config.BaseClass;
            cbRenderGenEntity.Checked = Config.RenderGenEntity;

            checkBox3.Checked = Config.UseCNFileName;
            checkBox5.Checked = Config.UseHeadTemplate;
            //richTextBox2.Text = Config.HeadTemplate;
            checkBox4.Checked = Config.Debug;
        }

        public void SaveConfig()
        {
            Config.ConnName = cbConn.Text;
            Config.TemplateName = cb_Template.Text;
            Config.OutputPath = txt_OutPath.Text;
            Config.NameSpace = txt_NameSpace.Text;
            Config.EntityConnName = txt_ConnName.Text;
            Config.BaseClass = txtBaseClass.Text;
            Config.RenderGenEntity = cbRenderGenEntity.Checked;

            Config.UseCNFileName = checkBox3.Checked;
            Config.UseHeadTemplate = checkBox5.Checked;
            //Config.HeadTemplate = richTextBox2.Text;
            Config.Debug = checkBox4.Checked;

            Config.Save();
        }
        #endregion

        #region ������Ϣ
        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var control = sender as Control;
            if (control == null) return;

            String url = String.Empty;
            if (control.Tag != null) url = control.Tag.ToString();
            if (String.IsNullOrEmpty(url)) url = control.Text;
            if (String.IsNullOrEmpty(url)) return;

            Process.Start(url);
        }

        private void label3_Click(object sender, EventArgs e)
        {
            Clipboard.SetData("1600800", null);
            MessageBox.Show("QQȺ���Ѹ��Ƶ����а壡", "��ʾ");
        }
        #endregion

        #region �����Ŀ¼
        private void btnOpenOutputDir_Click(object sender, EventArgs e)
        {
            var dir = txt_OutPath.Text.GetFullPath();
            if (!Directory.Exists(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;

            Process.Start("explorer.exe", "\"" + dir + "\"");
            //Process.Start("explorer.exe", "/root,\"" + dir + "\"");
            //Process.Start("explorer.exe", "/select," + dir);
        }

        private void frmItems_Click(object sender, EventArgs e)
        {
            //FrmItems.Create(XConfig.Current.Items).Show();

            FrmItems.Create(XConfig.Current).Show();
        }
        #endregion

        #region ģ�����
        public void BindTemplate(ComboBox cb)
        {
            var list = new List<String>();
            foreach (var item in Engine.FileTemplates)
            {
                list.Add("[�ļ�]" + item);
            }
            foreach (String item in Engine.Templates.Keys)
            {
                String[] ks = item.Split('.');
                if (ks == null || ks.Length < 1) continue;

                String name = "[����]" + ks[0];
                if (!list.Contains(name)) list.Add(name);
            }
            cb.Items.Clear();
            cb.DataSource = list;
            cb.DisplayMember = "value";
            cb.Update();
        }

        private void btnRelease_Click(object sender, EventArgs e)
        {
            try
            {
                Source.ReleaseAllTemplateFiles();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private void lbEditHeader_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var frm = FrmText.Create("C#�ļ�ͷģ��", Config.HeadTemplate);
            frm.ShowDialog();
            Config.HeadTemplate = frm.Content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            frm.Dispose();
        }
        #endregion

        #region �˵�
        private void �˳�XToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //Application.Exit();
            this.Close();
        }

        private void ����ֲ�ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var file = "X����ֲ�.chm";
            if (!File.Exists(file)) file = Path.Combine(@"C:\X\DLL", file);
            if (File.Exists(file)) Process.Start(file);
        }

        private void �����ֶ��������淶ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FrmText.Create("�����ֶ��������淶", Source.GetText("���ݿ������淶")).Show();
        }

        private void ���߰����ĵ�ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("http://www.NewLifeX.com/showtopic-260.aspx?r=XCoder_v" + AssemblyX.Create(Assembly.GetExecutingAssembly()).Version);
        }

        private void ������ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            XConfig.Current.LastUpdate = DateTime.Now;

            try
            {
                var au = new AutoUpdate();
                au.Update();

                MessageBox.Show("û�п��ø��£�", "�Զ�����");
            }
            catch (Exception ex)
            {
                XTrace.WriteException(ex);
                MessageBox.Show("����ʧ�ܣ�" + ex.Message, "�Զ�����");
            }
        }

        private void ����ToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            FrmText.Create("������ʷ", Source.GetText("UpdateInfo")).Show();
        }

        private void ����ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("http://nnhy.cnblogs.com");
        }

        private void qQȺ1600800ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("http://www.NewLifeX.com/?r=XCoder_v" + AssemblyX.Create(Assembly.GetExecutingAssembly()).Version);
        }

        private void oracle�ͻ�������ʱ���ToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            ThreadPoolX.QueueUserWorkItem(CheckOracle);
        }
        void CheckOracle()
        {
            if (!DAL.ConnStrs.ContainsKey("Oracle")) return;

            try
            {
                var list = DAL.Create("Oracle").Tables;

                MessageBox.Show("Oracle�ͻ�������ʱ���ͨ����");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Oracle�ͻ�������ʱ���ʧ�ܣ�Ҳ�������û����������" + ex.ToString());
            }
        }

        private void �Զ���ʽ������ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FrmFix.Create(Config).ShowDialog();
        }
        #endregion

        #region ģ�͹���
        private void ģ�͹���MToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var tables = Engine.Tables;
            if (tables == null || tables.Count < 1) return;

            FrmModel.Create(tables).Show();
        }

        private void ����ģ��EToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var tables = Engine.Tables;
            if (tables == null || tables.Count < 1)
            {
                MessageBox.Show(this.Text, "���ݿ�ܹ�Ϊ�գ�", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!String.IsNullOrEmpty(Config.ConnName))
            {
                var file = Config.ConnName + ".xml";
                String dir = null;
                if (!String.IsNullOrEmpty(saveFileDialog1.FileName))
                    dir = Path.GetDirectoryName(saveFileDialog1.FileName);
                if (String.IsNullOrEmpty(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
                //saveFileDialog1.FileName = Path.Combine(dir, file);
                saveFileDialog1.InitialDirectory = dir;
                saveFileDialog1.FileName = file;
            }
            if (saveFileDialog1.ShowDialog() != DialogResult.OK || String.IsNullOrEmpty(saveFileDialog1.FileName)) return;
            try
            {
                String xml = DAL.Export(tables);
                File.WriteAllText(saveFileDialog1.FileName, xml);

                MessageBox.Show("�����ܹ��ɹ���", "�����ܹ�", MessageBoxButtons.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private void �ܹ�����SToolStripMenuItem_Click(object sender, EventArgs e)
        {
            String connName = "" + cbConn.SelectedValue;
            if (String.IsNullOrEmpty(connName)) return;

            FrmSchema.Create(DAL.Create(connName).Db).Show();
        }

        private void sQL��ѯ��QToolStripMenuItem_Click(object sender, EventArgs e)
        {
            String connName = "" + cbConn.SelectedValue;
            if (String.IsNullOrEmpty(connName)) return;

            FrmQuery.Create(DAL.Create(connName)).Show();
        }

        private void btnImport_Click(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Text == "����ģ��")
            {
                ����ģ��EToolStripMenuItem_Click(null, EventArgs.Empty);
                return;
            }

            if (openFileDialog1.ShowDialog() != DialogResult.OK || String.IsNullOrEmpty(openFileDialog1.FileName)) return;
            try
            {
                var list = DAL.Import(File.ReadAllText(openFileDialog1.FileName));
                if (!cbIncludeView.Checked) list = list.Where(t => !t.IsView).ToList();
                if (Config.NeedFix) list = Engine.FixTable(list);

                Engine = null;
                Engine.Tables = list;

                SetTables(list);

                gbTable.Enabled = true;
                ģ��ToolStripMenuItem.Visible = true;
                �ܹ�����SToolStripMenuItem.Visible = false;

                MessageBox.Show("����ܹ��ɹ�����" + (list == null ? 0 : list.Count) + "�ű�", "����ܹ�", MessageBoxButtons.OK);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        #endregion

        #region ��ҳ
        private void webBrowser1_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            // ��ҳ������ɺ��Զ����¹���һ�ξ��룬Խ��ͷ��
            webBrowser1.Document.Window.ScrollTo(0, 90);
        }
        #endregion

        #region ���ģ��-@����-С�� 2013
        private void ���ģ��ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            NewModel.CreateForm().Show();
        }
        #endregion
    }
}