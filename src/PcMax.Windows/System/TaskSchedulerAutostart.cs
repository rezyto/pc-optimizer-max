using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PcMax.Core.Platform;

namespace PcMax.Windows.SystemServices;

/// <summary>
/// Starts the app at logon, elevated, with no UAC prompt: a Task Scheduler task "PC Max" with
/// "run with highest privileges" and a logon trigger for the current user. Managed through
/// schtasks.exe with an XML definition; arguments are passed via ArgumentList (no shell parsing).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TaskSchedulerAutostart : IAutostart
{
    private const string TaskName = "PC Max";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static string Schtasks => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    public bool IsEnabled()
    {
        try { return Run(out _, "/Query", "/TN", TaskName) == 0; }
        catch (Exception ex)
        {
            Log.Warn("Autoarranque: consulta falhou", ex);
            return false;
        }
    }

    public void Enable(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !Path.IsPathFullyQualified(exePath))
            throw new ArgumentException("O caminho da app tem de ser absoluto.", nameof(exePath));
        var full = Path.GetFullPath(exePath);
        if (!File.Exists(full) || !full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Executável da app não encontrado.", full);
        if (full.Contains('"')) throw new ArgumentException("Caminho inválido.", nameof(exePath));

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Utilizador atual desconhecido.");

        var xml = BuildXml(full, identity.Name, sid);
        var tmp = Path.Combine(Path.GetTempPath(), $"pcmax-task-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks expects UTF-16 when the declaration says so.
            using (var w = XmlWriter.Create(tmp, new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true }))
                xml.Save(w);
            var rc = Run(out var output, "/Create", "/TN", TaskName, "/XML", tmp, "/F");
            if (rc != 0) throw new InvalidOperationException($"schtasks /Create falhou ({rc}): {output.Trim()}");
            Log.Info($"Autoarranque ativado: {full}");
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    public void Disable()
    {
        if (!IsEnabled()) return;
        var rc = Run(out var output, "/Delete", "/TN", TaskName, "/F");
        if (rc != 0) throw new InvalidOperationException($"schtasks /Delete falhou ({rc}): {output.Trim()}");
        Log.Info("Autoarranque desativado.");
    }

    private static XDocument BuildXml(string exe, string userName, string sid) =>
        new(new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task", new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Description", "Inicia o PC Max ao entrar no Windows."),
                    new XElement(Ns + "URI", "\\" + TaskName)),
                new XElement(Ns + "Triggers",
                    new XElement(Ns + "LogonTrigger",
                        new XElement(Ns + "Enabled", "true"),
                        new XElement(Ns + "UserId", userName),
                        new XElement(Ns + "Delay", "PT10S"))),
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", sid),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "HighestAvailable"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "AllowHardTerminate", "true"),
                    new XElement(Ns + "StartWhenAvailable", "false"),
                    new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(Ns + "IdleSettings",
                        new XElement(Ns + "StopOnIdleEnd", "false"),
                        new XElement(Ns + "RestartOnIdle", "false")),
                    new XElement(Ns + "AllowStartOnDemand", "true"),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "Hidden", "false"),
                    new XElement(Ns + "RunOnlyIfIdle", "false"),
                    new XElement(Ns + "WakeToRun", "false"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT0S"), // never stop the app
                    new XElement(Ns + "Priority", "4")),            // 4 = normal process priority (default 7 is below normal)
                new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", "\"" + exe + "\""),
                        new XElement(Ns + "Arguments", "--minimized"),
                        new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(exe) ?? "")))));

    private static int Run(out string output, params string[] args)
    {
        var psi = new ProcessStartInfo(Schtasks)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar schtasks.exe.");
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(20000))
        {
            try { p.Kill(); } catch { }
            output = "timeout";
            return -1;
        }
        output = so.Result + se.Result;
        return p.ExitCode;
    }
}
