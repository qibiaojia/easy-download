using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class GuiSmoke
{
    delegate bool EnumWindow(IntPtr h, IntPtr extra);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumWindow callback, IntPtr extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int message, IntPtr w, string l);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L, T, R, B; }
    sealed class Child { public IntPtr Handle; public string Text, Class; public Rect Bounds; }
    static List<Child> Children(IntPtr parent)
    {
        var result = new List<Child>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr unused) {
            var text = new StringBuilder(1024); var cls = new StringBuilder(128); Rect r;
            GetWindowText(h, text, text.Capacity); GetClassName(h, cls, cls.Capacity); GetWindowRect(h, out r);
            result.Add(new Child { Handle = h, Text = text.ToString(), Class = cls.ToString(), Bounds = r }); return true;
        }, IntPtr.Zero);
        return result;
    }
    static IntPtr Button(IntPtr window, string text)
    {
        foreach (var c in Children(window)) if (c.Text == text && c.Class.IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0) return c.Handle;
        throw new Exception("Missing button: " + text);
    }
    static void Click(IntPtr h) { SendMessage(h, 0xF5, IntPtr.Zero, null); }
    static void Capture(IntPtr window, string file)
    {
        Rect r; GetWindowRect(window, out r);
        using (var b = new Bitmap(r.R - r.L, r.B - r.T)) using (var g = Graphics.FromImage(b))
        {
            IntPtr dc = g.GetHdc();
            try { if (!PrintWindow(window, dc, 2)) throw new Exception("Screenshot failed"); }
            finally { g.ReleaseHdc(dc); }
            b.Save(file);
        }
    }
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        Process process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo(args[0]) { UseShellExecute = args[0].EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) });
            process.WaitForInputIdle(5000);
            for (int i = 0; i < 50 && process.MainWindowHandle == IntPtr.Zero; i++) { Thread.Sleep(100); process.Refresh(); }
            IntPtr window = process.MainWindowHandle;
            if (window == IntPtr.Zero) throw new Exception("GUI window did not open");
            Thread.Sleep(300);
            var edits = Children(window).FindAll(c => c.Class.IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) >= 0);
            edits.Sort((a, b) => a.Bounds.T.CompareTo(b.Bounds.T));
            if (edits.Count != 3) throw new Exception("Expected three input fields");
            if (!Path.IsPathRooted(edits[1].Text)) throw new Exception("Default save folder is not absolute: " + edits[1].Text);
            SendMessage(edits[0].Handle, 0xC, IntPtr.Zero, args[1]);
            SendMessage(edits[1].Handle, 0xC, IntPtr.Zero, args[2]);
            SendMessage(edits[2].Handle, 0xC, IntPtr.Zero, "界面测试.zip");
            IntPtr start = Button(window, "开始下载"), cancel = Button(window, "取消");
            Click(start);
            Thread.Sleep(350);
            if (IsWindowEnabled(start)) throw new Exception("Start should be disabled during download");
            if (IsWindowEnabled(edits[0].Handle)) throw new Exception("Inputs should be disabled during download");
            bool canceled = args.Length > 4 && args[4] == "cancel";
            if (canceled) Click(cancel);
            bool complete = false;
            for (int i = 0; i < 250; i++)
            {
                Thread.Sleep(100);
                foreach (var c in Children(window))
                    if (c.Text.StartsWith(canceled ? "已取消" : "下载完成 · ", StringComparison.Ordinal)) complete = true;
                if (complete) break;
            }
            if (!complete) throw new Exception("GUI did not report " + (canceled ? "cancellation" : "completion"));
            // Completion text is rendered before the UI continuation finishes restoring controls.
            for (int i = 0; i < 100 && (!IsWindowEnabled(start) || IsWindowEnabled(cancel)); i++) Thread.Sleep(20);
            if (!IsWindowEnabled(start)) throw new Exception("Start did not re-enable");
            if (IsWindowEnabled(cancel)) throw new Exception("Cancel did not disable");
            foreach (string label in new[] {"开始下载", "取消", "打开文件夹"})
            {
                Rect button, form; IntPtr h = Button(window, label); GetWindowRect(h, out button); GetWindowRect(window, out form);
                if (!IsWindowVisible(h) || button.B > form.B || button.R > form.R) throw new Exception("Clipped action: " + label);
            }
            Capture(window, args[3]);
            Console.WriteLine(canceled ? "PASS GUI cancellation" : "PASS GUI download, controls, default folder and layout");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
        finally
        {
            if (process != null) { if (!process.HasExited) {process.CloseMainWindow(); if (!process.WaitForExit(5000)) process.Kill();} process.Dispose(); }
        }
    }
}
