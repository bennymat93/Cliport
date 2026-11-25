// Cliport installer / uninstaller. Per-user, no administrator rights needed.
//   Cliport-Setup.exe            install (asks first)
//   Cliport-Setup.exe /S         silent install
//   Uninstall.exe /uninstall     remove (add /S for silent)
// The program itself is embedded as a resource; build.ps1 compiles this file.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Cliport Setup")]
[assembly: AssemblyProduct("Cliport")]
[assembly: AssemblyVersion("1.6.0.0")]
[assembly: AssemblyFileVersion("1.6.0.0")]

static class Setup
{
    const string Version = "1.6";
    static bool he = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "he";
    static string T(string hebrew, string english) { return he ? hebrew : english; }
    static string Name = "Cliport";                                   // "/test" installs under a throw-away name, with no shortcuts on the desktop and no start-up entry
    static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
    static string Dir { get { return Path.Combine(Local, "Programs", Name); } }
    static string UninstallKey { get { return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Name; } }
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static MessageBoxOptions Rtl { get { return he ? MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading : 0; } }

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = false, uninstall = false, test = false;
        foreach (string a in args)
        {
            string x = a.ToLowerInvariant();
            if (x == "/s" || x == "/silent") silent = true; else if (x == "/uninstall") uninstall = true; else if (x == "/test") test = true;
        }
        if (test) Name = "Cliport-SetupTest";
        Application.EnableVisualStyles();
        try { return uninstall ? Uninstall(silent, test) : Install(silent, test); }
        catch (Exception e)
        {
            if (!silent) MessageBox.Show(T("הפעולה נכשלה: ", "The operation failed: ") + e.Message, "Cliport", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static int Install(bool silent, bool test)
    {
        if (!silent && MessageBox.Show(T("להתקין את Cliport " + Version + " במחשב זה?\n\nההתקנה היא למשתמש הנוכחי בלבד ואינה דורשת הרשאות מנהל.\nCliport יופעל אוטומטית עם עליית Windows (אפשר לשנות בהגדרות).",
                "Install Cliport " + Version + " on this computer?\n\nThe installation is for the current user only and needs no administrator rights.\nCliport will start automatically with Windows (you can change this in Settings)."),
                T("התקנת Cliport", "Cliport Setup"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, Rtl) != DialogResult.OK) return 2;
        string exe = Path.Combine(Dir, "Cliport.exe");
        StopRunning(exe);
        Directory.CreateDirectory(Dir);
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Cliport.exe"))
        {
            if (s == null) throw new InvalidOperationException("installer is incomplete");
            using (var f = new FileStream(exe, FileMode.Create, FileAccess.Write)) s.CopyTo(f);
        }
        string un = Path.Combine(Dir, "Uninstall.exe");
        File.Copy(Application.ExecutablePath, un, true);

        Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"), exe);
        if (!test) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk"), exe);
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", Name); k.SetValue("DisplayVersion", Version); k.SetValue("Publisher", "Cliport");
            k.SetValue("DisplayIcon", exe); k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", "\"" + un + "\" /uninstall" + (test ? " /test" : ""));
            k.SetValue("QuietUninstallString", "\"" + un + "\" /uninstall /S" + (test ? " /test" : ""));
            k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        }
        if (!test)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue("Cliport", "\"" + exe + "\" --minimized");
            Process.Start(exe);
        }
        if (!silent) MessageBox.Show(T("Cliport הותקן והופעל.\nקיצור דרך נוסף לשולחן העבודה ולתפריט התחל.", "Cliport was installed and started.\nA shortcut was added to the desktop and the Start menu."),
            T("התקנת Cliport", "Cliport Setup"), MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Rtl);
        return 0;
    }

    static int Uninstall(bool silent, bool test)
    {
        bool wipe = false;
        if (!silent)
        {
            if (MessageBox.Show(T("להסיר את Cliport מהמחשב?", "Remove Cliport from this computer?"), T("הסרת Cliport", "Uninstall Cliport"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1, Rtl) != DialogResult.OK) return 2;
            wipe = MessageBox.Show(T("למחוק גם את ההגדרות, הצימוד והגיבויים המקומיים?\n(בחר \"לא\" כדי לשמור אותם להתקנה עתידית)", "Also delete settings, pairing and local backups?\n(Choose \"No\" to keep them for a future installation)"),
                T("הסרת Cliport", "Uninstall Cliport"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, Rtl) == DialogResult.Yes;
        }
        StopRunning(Path.Combine(Dir, "Cliport.exe"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"));
        if (!test) TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk"));
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        if (!test) try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null) k.DeleteValue("Cliport", false); } catch { }
        if (wipe && !test) try { Directory.Delete(Path.Combine(Local, "Cliport"), true); } catch { }
        // this program runs from the folder it has to delete: let a detached command finish the job
        var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + Dir + "\"");
        psi.WindowStyle = ProcessWindowStyle.Hidden; psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.WorkingDirectory = Path.GetTempPath();
        Process.Start(psi);
        if (!silent) MessageBox.Show(T("Cliport הוסר.", "Cliport was removed."), T("הסרת Cliport", "Uninstall Cliport"), MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Rtl);
        return 0;
    }

    /// <summary>Stops a running copy (main process and its watchdog together, so neither restarts the other) and marks the stop as clean.</summary>
    static void StopRunning(string exe)
    {
        for (int round = 0; round < 4; round++)
        {
            bool any = false;
            foreach (var p in Process.GetProcessesByName("Cliport"))
            {
                try { if (string.Equals(p.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) { p.Kill(); any = true; } } catch { }
            }
            if (!any) break;
            Thread.Sleep(400);
        }
        TryDelete(Path.Combine(Local, "Cliport", "running.flag"));     // not a crash: no recovery notice on the next start
    }
    static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    static void Shortcut(string lnk, string target)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell"); object shell = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        Type st = sc.GetType();
        st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
        st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Cliport" });
        st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }
}
