﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DumpBinInvoke
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var index = Environment.CommandLine.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            var temp = Environment.CommandLine.Remove(0, index + 5).Trim();
            if (temp.Length > 1)
            {
                if (File.Exists(temp))
                {
                    mTextBoxPath.Text = temp;
                }
            }
        }


        private void mbuttonExec_Click(object sender, RoutedEventArgs e)
        {

            if (mTextBoxPath.Text == string.Empty) return;

            GetOutput(mHeaderBox, mTextBoxPath.Text, " /HEADERS");
            GetOutput(mAsmHeader, mTextBoxPath.Text, " /DISASM");
            GetOutput(mExports, mTextBoxPath.Text, " /EXPORTS");
            GetOutput(mDependents, mTextBoxPath.Text, " /DEPENDENTS");
            GetOutput(mImports, mTextBoxPath.Text, " /IMPORTS");
            GetOutput(mRowData, mTextBoxPath.Text, " /RAWDATA");
            GetOutput(mReLocations, mTextBoxPath.Text, " /RELOCATIONS");

            if (mTextBoxPath.Text.ToLower().EndsWith(".exe"))
            {
                MonitorProcess();

                mTabControl.SelectedIndex = mTabControl.Items.Count - 1;
            }

        }


        private void mButtonOpen_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.OpenFileDialog open = new System.Windows.Forms.OpenFileDialog();
            open.Multiselect = false;
            open.Title = "选择执行文件";
            open.CheckFileExists = true;
            open.Filter = "所有文件|*.*|可执行文件|*.exe|静态链接库|*.lib|动态链接库|*.dll|中间目标文件|*.obj";

            if (open.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                mTextBoxPath.Text = open.FileName;
            }

        }


        public void MonitorProcess()
        {
            mExeOutput.Text = string.Empty;

            ProcessStartInfo startInfo = new ProcessStartInfo(mTextBoxPath.Text)
            {
                WindowStyle = ProcessWindowStyle.Normal,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            Func<string> mGetTime = () =>
            {
                return DateTime.Now.ToString("[ yyyy-MM-dd HH::mm::ss::fff ] ");
            };

            Process process = null;

            try
            {
                process = Process.Start(startInfo);
            }
            catch (Exception)
            {
                DllHandler.RunAsAdmin(mTextBoxPath.Text);

                Hide();

                Close();

                return;
            }

            process.OutputDataReceived += (o, e1) =>
            {
                mExeOutput.Dispatcher.BeginInvoke(new Action(() =>
                {
                    mExeOutput.Text += mGetTime() + e1.Data + Environment.NewLine;
                    mExeOutput.SelectionStart = mExeOutput.Text.Length;
                    mExeOutput.ScrollToEnd();
                }), null);
            };
            process.BeginOutputReadLine();

        }


        public void GetOutput(TextBox textbox, string dllpath, string arg)
        {
            textbox.Text = string.Empty;

            Task.Run(() =>
            {
                ProcessStartInfo startInfo = new ProcessStartInfo("cmd.exe", "/c " + "dumpbin.exe " + DllHandler.GetShortName(dllpath) + arg + "&exit")
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                Process process = Process.Start(startInfo);

                string output = process.StandardOutput.ReadToEnd();

                process.WaitForExit();
                process.Close();

                if (textbox == mImports)
                {
                    Regex regex = new Regex(@"\?(.*)[Zz]");

                    var newSource = regex.Replace(output, new MatchEvaluator((Match m) =>
                    {
                        return DllHandler.GetDecryptSymbolName(m.Value);
                    }));

                    output = newSource;

                }
                else if (textbox == mExports)
                {

                    {
                        Regex regex = new Regex(@"\?(.*)[Zz] ");

                        var tt = regex.Matches(output);



                        if (tt.Count > 0)
                        {
                            var newSource = regex.Replace(output, new MatchEvaluator((Match m) =>
                            {
                                return DllHandler.GetDecryptSymbolName(m.Value.Trim());
                            }));

                            output = newSource;
                        }

                    }

                    {
                        Regex regex = new Regex(@"\(\?(.*)[Zz]\)");

                        var tt = regex.Matches(output);

                        if (tt.Count > 0)
                        {
                            var newSource = regex.Replace(output, new MatchEvaluator((Match m) =>
                            {
                                var t = m.Value.Substring(1, m.Value.Length - 2);

                                return DllHandler.GetDecryptSymbolName(t);
                            }));

                            output = newSource;
                        }

                    }

                }

                textbox.Dispatcher.BeginInvoke(new Action(() => textbox.AppendText(output)), null);

            });

        }



        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;

            e.Handled = true;
        }


        private void Window_Drag(object sender, DragEventArgs e)
        {
            try
            {
                mTextBoxPath.Text = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString();

            }
            catch (Exception ex)
            {
                mTextBoxPath.Text = "";
                MessageBox.Show(ex.Message);
            }
        }


        public static List<T> GetChildObjects<T>(DependencyObject obj, Action<T> actor = null) where T : FrameworkElement
        {
            DependencyObject child = null;

            List<T> childList = new List<T>();

            for (int i = 0; i <= VisualTreeHelper.GetChildrenCount(obj) - 1; i++)
            {
                child = VisualTreeHelper.GetChild(obj, i);

                if (child is T && (((T)child).GetType() == typeof(T)))
                {
                    actor?.Invoke((T)child);

                    childList.Add((T)child);
                }
                childList.AddRange(GetChildObjects<T>(child, actor));
            }



            return childList;
        }


    }

    public class DllHandler
    {

        [DllImport("dbghelp.dll", SetLastError = true, PreserveSig = true)]
        static extern int UnDecorateSymbolName(
                [In] [MarshalAs(UnmanagedType.LPStr)] string DecoratedName,
                [Out] StringBuilder UnDecoratedName,
                [In] [MarshalAs(UnmanagedType.U4)] int UndecoratedLength,
                [In] [MarshalAs(UnmanagedType.U4)] UnDecorateFlags Flags);

        [Flags]
        enum UnDecorateFlags
        {
            UNDNAME_COMPLETE = (0x0000),  // Enable full undecoration
            UNDNAME_NO_LEADING_UNDERSCORES = (0x0001),  // Remove leading underscores from MS extended keywords
            UNDNAME_NO_MS_KEYWORDS = (0x0002),  // Disable expansion of MS extended keywords
            UNDNAME_NO_FUNCTION_RETURNS = (0x0004),  // Disable expansion of return type for primary declaration
            UNDNAME_NO_ALLOCATION_MODEL = (0x0008),  // Disable expansion of the declaration model
            UNDNAME_NO_ALLOCATION_LANGUAGE = (0x0010),  // Disable expansion of the declaration language specifier
            UNDNAME_NO_MS_THISTYPE = (0x0020),  // NYI Disable expansion of MS keywords on the 'this' type for primary declaration
            UNDNAME_NO_CV_THISTYPE = (0x0040),  // NYI Disable expansion of CV modifiers on the 'this' type for primary declaration
            UNDNAME_NO_THISTYPE = (0x0060),  // Disable all modifiers on the 'this' type
            UNDNAME_NO_ACCESS_SPECIFIERS = (0x0080),  // Disable expansion of access specifiers for members
            UNDNAME_NO_THROW_SIGNATURES = (0x0100),  // Disable expansion of 'throw-signatures' for functions and pointers to functions
            UNDNAME_NO_MEMBER_TYPE = (0x0200),  // Disable expansion of 'static' or 'virtual'ness of members
            UNDNAME_NO_RETURN_UDT_MODEL = (0x0400),  // Disable expansion of MS model for UDT returns
            UNDNAME_32_BIT_DECODE = (0x0800),  // Undecorate 32-bit decorated names
            UNDNAME_NAME_ONLY = (0x1000),  // Crack only the name for primary declaration;
                                           // return just [scope::]name.  Does expand template params
            UNDNAME_NO_ARGUMENTS = (0x2000),  // Don't undecorate arguments to function
            UNDNAME_NO_SPECIAL_SYMS = (0x4000),  // Don't undecorate special names (v-table, vcall, vector xxx, metatype, etc)
        }

        [DllImport("kernel32", EntryPoint = "GetShortPathName", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufSize);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        public static string GetShortName(string sLongFileName)
        {
            var buffer = new StringBuilder(259);
            int len = DllHandler.GetShortPathName(sLongFileName, buffer, buffer.Capacity);
            if (len == 0) throw new System.ComponentModel.Win32Exception();
            return buffer.ToString();
        }


        public static string GetDecryptSymbolName(string src)
        {
            //src = "?FindEdge@IPCV@@YAXVMat@cv@@V?$Rect_@H@3@HH_NW4SEARCHDIRECTION@@AAUEdgeFindResult@@@Z";

            var buffer = new StringBuilder(259);

            var len = 1000;

            UnDecorateSymbolName(src, buffer, len, UnDecorateFlags.UNDNAME_COMPLETE);

            return "  解码签名：  " + buffer.ToString();
        }

        public static void RunAsAdmin(string arg)
        {
            SHELLEXECUTEINFO info = new DumpBinInvoke.DllHandler.SHELLEXECUTEINFO();
            info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(info);
            info.lpVerb = "runas";
            info.nShow = 1;
            info.lpFile = System.Windows.Forms.Application.ExecutablePath;
            info.lpParameters = File.Exists(arg) ? arg : "";

            ShellExecuteEx(ref info);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpFile;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        public enum ShowCommands : int
        {
            SW_HIDE = 0,
            SW_SHOWNORMAL = 1,
            SW_NORMAL = 1,
            SW_SHOWMINIMIZED = 2,
            SW_SHOWMAXIMIZED = 3,
            SW_MAXIMIZE = 3,
            SW_SHOWNOACTIVATE = 4,
            SW_SHOW = 5,
            SW_MINIMIZE = 6,
            SW_SHOWMINNOACTIVE = 7,
            SW_SHOWNA = 8,
            SW_RESTORE = 9,
            SW_SHOWDEFAULT = 10,
            SW_FORCEMINIMIZE = 11,
            SW_MAX = 11
        }

        [Flags]
        public enum ShellExecuteMaskFlags : uint
        {
            SEE_MASK_DEFAULT = 0x00000000,
            SEE_MASK_CLASSNAME = 0x00000001,
            SEE_MASK_CLASSKEY = 0x00000003,
            SEE_MASK_IDLIST = 0x00000004,
            SEE_MASK_INVOKEIDLIST = 0x0000000c,   // Note SEE_MASK_INVOKEIDLIST(0xC) implies SEE_MASK_IDLIST(0x04) 
            SEE_MASK_HOTKEY = 0x00000020,
            SEE_MASK_NOCLOSEPROCESS = 0x00000040,
            SEE_MASK_CONNECTNETDRV = 0x00000080,
            SEE_MASK_NOASYNC = 0x00000100,
            SEE_MASK_FLAG_DDEWAIT = SEE_MASK_NOASYNC,
            SEE_MASK_DOENVSUBST = 0x00000200,
            SEE_MASK_FLAG_NO_UI = 0x00000400,
            SEE_MASK_UNICODE = 0x00004000,
            SEE_MASK_NO_CONSOLE = 0x00008000,
            SEE_MASK_ASYNCOK = 0x00100000,
            SEE_MASK_HMONITOR = 0x00200000,
            SEE_MASK_NOZONECHECKS = 0x00800000,
            SEE_MASK_NOQUERYCLASSSTORE = 0x01000000,
            SEE_MASK_WAITFORINPUTIDLE = 0x02000000,
            SEE_MASK_FLAG_LOG_USAGE = 0x04000000,
        }



    }


}
