using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Xml.Linq;
using PcMax.Core.Platform;

namespace PcMax.Windows.SystemServices;

/// <summary>
/// Starts the app at logon, elevated, with no UAC prompt: a Task Scheduler task "PC Max" with
/// "run with highest privileges" and a logon trigger for the current user. Registered through the
/// Task Scheduler COM API (ITaskService / ITaskFolder.RegisterTask) with the XML built in memory:
/// no temp file and no schtasks.exe.
/// Only an executable installed under %ProgramFiles% (writable by administrators only) is ever registered,
/// so a copy in a user-writable folder (a dev build, a download) can never be started elevated by the task.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TaskSchedulerAutostart : IAutostart
{
    private const string TaskName = "PC Max";
    private const int TASK_CREATE_OR_UPDATE = 6;
    private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;
    private const int HRESULT_FILE_NOT_FOUND = unchecked((int)0x80070002);
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public bool IsEnabled()
    {
        try
        {
            return WithRootFolder(folder =>
            {
                try
                {
                    var task = folder.GetTask(TaskName);
                    Release(task);
                    return true;
                }
                catch (COMException ex) when (ex.HResult == HRESULT_FILE_NOT_FOUND) { return false; }
            });
        }
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
        if (!InstallLocation.IsUnderProgramFiles(full))
            throw new InvalidOperationException(
                "O arranque com o Windows só funciona com o PC Max instalado em \"Programas\" (Program Files). " +
                "Esta cópia está numa pasta que outros programas podem alterar, por isso não é registada para arrancar como administrador.");

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Utilizador atual desconhecido.");

        var xml = BuildXml(full, identity.Name, sid).ToString(SaveOptions.DisableFormatting);
        WithRootFolder(folder =>
        {
            // userId/password null: the principal (current user, interactive token, highest privileges) comes from the XML.
            var registered = folder.RegisterTask(TaskName, xml, TASK_CREATE_OR_UPDATE, null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
            Release(registered);
            return 0;
        });
        Log.Info($"Autoarranque ativado: {full}");
    }

    public void Disable()
    {
        WithRootFolder(folder =>
        {
            try { folder.DeleteTask(TaskName, 0); }
            catch (COMException ex) when (ex.HResult == HRESULT_FILE_NOT_FOUND) { return 0; }
            Log.Info("Autoarranque desativado.");
            return 0;
        });
    }

    /// <summary>Connects to the local Task Scheduler and runs <paramref name="action"/> on the root folder ("\\").</summary>
    private static T WithRootFolder<T>(Func<dynamic, T> action)
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic? service = null;
        dynamic? folder = null;
        try
        {
            service = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Task Scheduler indisponível.");
            service.Connect();
            folder = service.GetFolder("\\");
            return action(folder);
        }
        finally
        {
            Release(folder);
            Release(service);
        }
    }

    private static void Release(object? com)
    {
        try { if (com is not null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com); } catch { }
    }

    private static XDocument BuildXml(string exe, string userName, string sid) =>
        new(
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
}
