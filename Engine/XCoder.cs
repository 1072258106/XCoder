using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using XCode.DataAccessLayer;
using XTemplate.Templating;
using System.Reflection;

namespace XCoder
{
    /// <summary>
    /// ������������
    /// </summary>
    public class XCoder
    {
        #region ����
        public const String TemplatePath = "Template";

        public XCoder(XConfig config)
        {
            Config = config;
        }

        private XConfig _Config;
        /// <summary>����</summary>
        public XConfig Config
        {
            get { return _Config; }
            set { _Config = value; }
        }

        private List<IDataTable> _Tables;
        /// <summary>���б�</summary>
        public List<IDataTable> Tables
        {
            get
            {
                if (_Tables == null)
                {
                    try
                    {
                        _Tables = DAL.Create(Config.ConnName).Tables;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.ToString());
                    }
                }
                return _Tables;
            }
            set { _Tables = value; }
        }

        private String _OuputPath;
        /// <summary>���·��</summary>
        public String OuputPath
        {
            get
            {
                if (_OuputPath == null)
                {
                    String str = Config.OutputPath;
                    if (!Directory.Exists(str)) Directory.CreateDirectory(str);

                    _OuputPath = str;
                    if (_OuputPath == null) _OuputPath = "";
                }
                return _OuputPath;
            }
            set
            {
                if (_OuputPath != null && !Directory.Exists(value)) Directory.CreateDirectory(value);
                _OuputPath = value;
            }
        }
        #endregion

        #region ��������
        /// <summary>
        /// ����ǰ׺
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static String CutPrefix(String name)
        {
            if (String.IsNullOrEmpty(name)) return null;

            //�Զ�ȥ��ǰ׺
            if (XConfig.Current.AutoCutPrefix && name.Contains("_"))
            {
                name = name.Substring(name.IndexOf("_") + 1);
            }

            if (String.IsNullOrEmpty(XConfig.Current.Prefix)) return name;
            String[] ss = XConfig.Current.Prefix.Split(new Char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (String s in ss)
            {
                if (name.StartsWith(s))
                {
                    name = name.Substring(s.Length);
                }
                else if (name.EndsWith(s))
                {
                    name = name.Substring(0, name.Length - s.Length);
                }
            }

            return name;
        }

        /// <summary>
        /// �Զ������Сд
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static String FixWord(String name)
        {
            Int32 count1 = 0;
            Int32 count2 = 0;
            foreach (Char item in name.ToCharArray())
            {
                if (item >= 'a' && item <= 'z')
                    count1++;
                else if (item >= 'A' && item <= 'Z')
                    count2++;
            }

            //û�л���ֻ��һ��Сд��ĸ�ģ���Ҫ����
            //û�д�д�ģ�ҲҪ����
            if (count1 <= 1 || count2 < 1)
            {
                name = name.ToLower();
                Char c = name[0];
                c = (Char)(c - 'a' + 'A');
                name = c + name.Substring(1);
            }

            //����Is��ͷ�ģ���������ĸҪ��д
            if (name.StartsWith("Is") && name.Length >= 3)
            {
                Char c = name[2];
                if (c >= 'a' && c <= 'z')
                {
                    c = (Char)(c - 'a' + 'A');
                    name = name.Substring(0, 2) + c + name.Substring(3);
                }
            }

            //�Զ�ƥ�䵥��
            foreach (String item in Words.Keys)
            {
                if (name.Equals(item, StringComparison.OrdinalIgnoreCase))
                {
                    name = item;
                    break;
                }
            }

            return name;
        }

        /// <summary>
        /// Ӣ����ת������
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static String ENameToCName(String name)
        {
            if (String.IsNullOrEmpty(name)) return null;

            //foreach (String item in Words.Keys)
            //{
            //    if (name.Equals(item, StringComparison.OrdinalIgnoreCase)) return Words[item];
            //}
            //return null;
            String key = name.ToLower();
            if (LowerWords.ContainsKey(key))
                return LowerWords[key];
            else
                return null;
        }

        private static Dictionary<String, String> _Words;
        /// <summary>����</summary>
        public static Dictionary<String, String> Words
        {
            get
            {
                if (_Words == null)
                {
                    _Words = new Dictionary<string, string>();

                    if (File.Exists("e2c.txt"))
                    {
                        String content = File.ReadAllText("e2c.txt");
                        if (!String.IsNullOrEmpty(content))
                        {
                            String[] ss = content.Split(new Char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            if (ss != null && ss.Length > 0)
                            {
                                foreach (String item in ss)
                                {
                                    String[] s = item.Split(new Char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (s != null && s.Length > 0)
                                    {
                                        String str = "";
                                        if (s.Length > 1) str = s[1];
                                        if (!_Words.ContainsKey(s[0])) _Words.Add(s[0], str);
                                    }
                                }
                            }
                        }
                    }
                }
                return _Words;
            }
        }

        private static SortedList<String, String> _LowerWords;
        /// <summary>����</summary>
        public static SortedList<String, String> LowerWords
        {
            get
            {
                if (_LowerWords == null)
                {
                    _LowerWords = new SortedList<string, string>();

                    foreach (String item in Words.Keys)
                    {
                        if (!_LowerWords.ContainsKey(item.ToLower()))
                            _LowerWords.Add(item.ToLower(), Words[item]);
                        else if (String.IsNullOrEmpty(_LowerWords[item.ToLower()]))
                            _LowerWords[item.ToLower()] = Words[item];
                    }
                }
                return _LowerWords;
            }
        }

        public static void AddWord(String name, String cname)
        {
            String ename = CutPrefix(name);
            ename = FixWord(ename);
            if (LowerWords.ContainsKey(ename.ToLower())) return;
            LowerWords.Add(ename.ToLower(), cname);
            Words.Add(ename, cname);
            File.AppendAllText("e2c.txt", Environment.NewLine + ename + " " + cname, Encoding.UTF8);
        }
        #endregion

        #region ����
        /// <summary>
        /// ���ɴ��룬������Config����
        /// </summary>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public String[] Render(String tableName)
        {
            if (Tables == null && Tables.Count < 1) return null;

            IDataTable table = Tables.Find(delegate(IDataTable item) { return String.Equals(item.Name, tableName, StringComparison.OrdinalIgnoreCase); });
            if (tableName == null) return null;

            String path = Path.Combine(TemplatePath, Config.TemplateName);
            if (!Directory.Exists(path)) return null;

            String[] ss = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly);
            if (ss == null || ss.Length < 1) return null;

            Dictionary<String, Object> data = new Dictionary<string, object>();
            data["Config"] = Config;
            data["Tables"] = Tables;
            data["Table"] = table;

            // ����ģ������
            //Template tt = new Template();
            Template.Debug = Config.Debug;
            Dictionary<String, String> templates = new Dictionary<string, string>();
            foreach (String item in ss)
            {
                if (item.EndsWith("scc", StringComparison.OrdinalIgnoreCase)) continue;

                String tempFile = item;
                if (!Path.IsPathRooted(tempFile) && !tempFile.StartsWith(TemplatePath, StringComparison.OrdinalIgnoreCase))
                    tempFile = Path.Combine(TemplatePath, tempFile);

                String content = File.ReadAllText(tempFile);

                // ����ļ�ͷ
                if (Config.UseHeadTemplate && !String.IsNullOrEmpty(Config.HeadTemplate))
                    content = Config.HeadTemplate + content;

                //tt.AddTemplateItem(item, content);

                templates.Add(item, content);
            }
            Template tt = Template.Create(templates);

            //tt.Process();

            //// ����ģ��
            //tt.Compile();

            List<String> rs = new List<string>();
            foreach (String item in ss)
            {
                if (item.EndsWith("scc", StringComparison.OrdinalIgnoreCase)) continue;

                //String content = RenderFile(table, item, data);
                String content = tt.Render(item, data);

                // ��������ļ���
                String fileName = Path.GetFileName(item);
                String className = CutPrefix(table.Name);
                className = FixWord(className);
                String remark = table.Description;
                if (String.IsNullOrEmpty(remark)) remark = ENameToCName(className);
                if (Config.UseCNFileName && !String.IsNullOrEmpty(remark)) className = remark;
                fileName = fileName.Replace("����", className).Replace("��˵��", remark).Replace("������", Config.EntityConnName);

                fileName = Path.Combine(OuputPath, fileName);
                File.WriteAllText(fileName, content, Encoding.UTF8);

                rs.Add(content);
            }
            return rs.ToArray();
        }

        //private String RenderFile(XTable table, String tempFile, Dictionary<String, Object> data)
        //{
        //    if (!Path.IsPathRooted(tempFile) && !tempFile.StartsWith(TemplatePath, StringComparison.OrdinalIgnoreCase))
        //        tempFile = Path.Combine(TemplatePath, tempFile);

        //    data["Table"] = table;
        //    return Template.Process(tempFile, data);
        //}
        #endregion

        #region ��̬
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
        #endregion
    }
}