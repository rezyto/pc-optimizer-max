using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using PcMax.Core.Platform;
using PcMax.Core.Settings;
using PcMax.Windows.Native;
using static PcMax.Windows.Native.NativeMethods;

namespace PcMax.Windows;

/// <summary>
/// Game priority boost (High, never Realtime) and suspension of background apps.
/// Suspended processes are recorded in <c>suspended.json</c> (pid + creation time) BEFORE they are
/// suspended, so <see cref="ResumeLeftovers"/> can wake them after a crash without ever touching
/// a different process that reused the same pid.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessBooster : IProcessBooster
{
    private static readonly object FileGate = new();
    private readonly AppPaths _paths;

    public ProcessBooster(AppPaths paths) => _paths = paths;

    internal sealed record SuspendedProcess(int Pid, long CreationTimeUtc, string Name);

    // ------------------------------------------------------------------ priority

    public string? BoostGame(int processId)
    {
        using var p = Process.GetProcessById(processId);
        if (Core.Settings.ProtectedProcesses.IsProtected(p.ProcessName)) return null;
        var previous = p.PriorityClass;
        if (previous is ProcessPriorityClass.High or ProcessPriorityClass.RealTime) return previous.ToString();
        p.PriorityClass = ProcessPriorityClass.High;
        Log.Info($"Prioridade de {p.ProcessName} ({processId}): {previous} -> High");
        return previous.ToString();
    }

    public void RestoreGame(int processId, string? previousPriority)
    {
        if (previousPriority is null || !Enum.TryParse<ProcessPriorityClass>(previousPriority, out var prev)) return;
        if (prev == ProcessPriorityClass.RealTime) prev = ProcessPriorityClass.Normal; // never set Realtime
        try
        {
            using var p = Process.GetProcessById(processId);
            // Only undo our own change: if it is no longer High, the user (or the game) changed it.
            if (p.PriorityClass != ProcessPriorityClass.High || prev == ProcessPriorityClass.High) return;
            p.PriorityClass = prev;
            Log.Info($"Prioridade de {p.ProcessName} ({processId}) reposta: {prev}");
        }
        catch (ArgumentException) { /* already exited */ }
        catch (InvalidOperationException) { }
    }

    public bool IsRunning(int processId, string executableName)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            if (p.HasExited) return false;
            var exe = executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? executableName[..^4] : executableName;
            return string.Equals(p.ProcessName, exe, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }          // no such pid
        catch (InvalidOperationException) { return false; }  // exited
        catch { return true; }                               // access denied etc.: assume it still runs
    }

    // ------------------------------------------------------------------ suspend / resume

    public IReadOnlyList<int> SuspendBackground(IEnumerable<string> processNames, int? excludePid = null)
    {
        var names = processNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n)
            .Where(n => !Core.Settings.ProtectedProcesses.IsProtected(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0) return Array.Empty<int>();

        var ownPid = Environment.ProcessId;
        var mySid = CurrentUserSid();
        int mySession = -1;
        ProcessIdToSessionId(ownPid, out mySession);
        // The game and whatever launched it (launcher, anti-cheat bootstrapper, Steam…) are never suspended.
        var keepRunning = excludePid is { } gamePid ? ParentChain(gamePid) : new HashSet<int>();

        var suspended = new List<int>();
        lock (FileGate)
        {
            var records = Load(_paths);
            foreach (var name in names)
            {
                Process[] procs;
                try { procs = Process.GetProcessesByName(name); }
                catch { continue; }
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id == ownPid || p.Id <= 4 || p.Id == excludePid || keepRunning.Contains(p.Id)) continue; // never the game or its parents
                        if (Core.Settings.ProtectedProcesses.IsProtected(p.ProcessName)) continue;
                        if (records.Any(r => r.Pid == p.Id)) continue; // already suspended by us
                        if (p.SessionId != mySession) continue;

                        using var h = OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                        if (h.IsInvalid) continue;
                        if (IsProcessCritical(h, out var critical) && critical) continue;
                        if (!IsOwnedBy(h, mySid)) continue;
                        if (IsElevated(h)) continue; // elevated apps are not "background apps" of this user
                        if (!GetProcessTimes(h, out var created, out _, out _, out _)) continue;

                        // Persist first: if we crash right after suspending, the record is already on disk.
                        var rec = new SuspendedProcess(p.Id, created, p.ProcessName);
                        records.Add(rec);
                        try { Save(_paths, records); }
                        catch (Exception ex)
                        {
                            // Without a record we could not undo it after a crash: do not suspend.
                            records.Remove(rec);
                            Log.Warn("Não foi possível guardar suspended.json; suspensão cancelada", ex);
                            continue;
                        }

                        var status = NtSuspendProcess(h);
                        if (status < 0)
                        {
                            records.Remove(rec);
                            try { Save(_paths, records); } catch { /* harmless: resuming a running process is a no-op */ }
                            Log.Warn($"Não foi possível suspender {p.ProcessName} ({p.Id}): 0x{status:X8}");
                            continue;
                        }
                        suspended.Add(p.Id);
                        Log.Info($"Suspenso: {p.ProcessName} ({p.Id})");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Suspensão ignorada para {name}", ex);
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
        }
        return suspended;
    }

    public void ResumeAll(IEnumerable<int> processIds)
    {
        var wanted = processIds.ToHashSet();
        var mySid = CurrentUserSid();
        lock (FileGate)
        {
            var records = Load(_paths);
            // Only pids we recorded ourselves: never resume something another tool (e.g. a debugger) suspended.
            foreach (var r in records.Where(r => wanted.Contains(r.Pid)).ToList())
            {
                ResumeRecord(r, mySid);
                records.Remove(r);
            }
            try { Save(_paths, records); } catch (Exception ex) { Log.Warn("Não foi possível atualizar suspended.json", ex); }
        }
    }

    /// <summary>
    /// At startup: resume processes left suspended by a previous run that crashed. A pid is only resumed
    /// when its creation time still matches the recorded one (so a reused pid is never touched), it belongs
    /// to the current user and its executable name is the recorded one.
    /// </summary>
    public static void ResumeLeftovers(AppPaths paths)
    {
        try
        {
            lock (FileGate)
            {
                var records = Load(paths);
                if (records.Count == 0) return;
                Log.Info($"A retomar {records.Count} processo(s) deixados suspensos.");
                var mySid = CurrentUserSid();
                foreach (var r in records) ResumeRecord(r, mySid);
                Save(paths, new List<SuspendedProcess>());
            }
        }
        catch (Exception ex)
        {
            Log.Error("ResumeLeftovers falhou", ex);
        }
    }

    private static void ResumeRecord(SuspendedProcess r, SecurityIdentifier? mySid)
    {
        try
        {
            using var h = OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_QUERY_LIMITED_INFORMATION, false, r.Pid);
            if (h.IsInvalid) return; // exited
            if (!GetProcessTimes(h, out var created, out _, out _, out _) || created != r.CreationTimeUtc)
            {
                Log.Warn($"PID {r.Pid} ({r.Name}) já não é o mesmo processo; não foi retomado.");
                return;
            }
            if (!IsOwnedBy(h, mySid))
            {
                Log.Warn($"PID {r.Pid} ({r.Name}) não pertence ao utilizador atual; não foi retomado.");
                return;
            }
            var image = ImageName(h);
            if (image is null || !string.Equals(image, r.Name, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn($"PID {r.Pid} não é {r.Name} ({image ?? "?"}); não foi retomado.");
                return;
            }
            var status = NtResumeProcess(h);
            if (status < 0) Log.Warn($"Não foi possível retomar {r.Name} ({r.Pid}): 0x{status:X8}");
            else Log.Info($"Retomado: {r.Name} ({r.Pid})");
        }
        catch (Exception ex)
        {
            Log.Warn($"Retomar {r.Name} ({r.Pid}) falhou", ex);
        }
    }

    private static SecurityIdentifier? CurrentUserSid()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return id.User;
        }
        catch { return null; }
    }

    /// <summary>Executable name without folder or ".exe", from the process image path.</summary>
    private static string? ImageName(SafeProcessHandle h)
    {
        var buf = new char[1024];
        var size = buf.Length;
        if (!QueryFullProcessImageName(h, 0, buf, ref size) || size <= 0) return null;
        return Path.GetFileNameWithoutExtension(new string(buf, 0, size));
    }

    /// <summary>True when the process runs with an elevated (administrator) token. Unknown counts as elevated.</summary>
    private static bool IsElevated(SafeProcessHandle h)
    {
        if (!OpenProcessToken(h, TOKEN_QUERY, out var token)) return true;
        try
        {
            return !GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) || elevated != 0;
        }
        finally { CloseHandle(token); }
    }

    /// <summary>The pid and every ancestor (parent, grandparent, …) according to a process snapshot.</summary>
    internal static HashSet<int> ParentChain(int pid)
    {
        var chain = new HashSet<int> { pid };
        var parents = new Dictionary<int, int>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return chain;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32W>() };
            if (Process32FirstW(snap, ref e))
            {
                do { parents[(int)e.th32ProcessID] = (int)e.th32ParentProcessID; }
                while (Process32NextW(snap, ref e));
            }
        }
        finally { CloseHandle(snap); }

        var current = pid;
        for (var depth = 0; depth < 64 && parents.TryGetValue(current, out var parent) && parent > 4; depth++)
        {
            if (!chain.Add(parent)) break; // loop (reused pids)
            current = parent;
        }
        return chain;
    }

    private static bool IsOwnedBy(SafeProcessHandle h, SecurityIdentifier? sid)
    {
        if (sid is null) return false;
        if (!OpenProcessToken(h, TOKEN_QUERY, out var token)) return false;
        try
        {
            using var id = new WindowsIdentity(token);
            return id.User is { } u && sid.Equals(u);
        }
        catch { return false; }
        finally { CloseHandle(token); }
    }

    // ------------------------------------------------------------------ persistence

    private static string FilePath(AppPaths paths) => Path.Combine(paths.Root, "suspended.json");

    internal static List<SuspendedProcess> Load(AppPaths paths)
    {
        try
        {
            var f = FilePath(paths);
            if (!File.Exists(f)) return new();
            return Parse(AtomicFile.ReadAllTextCapped(f));
        }
        catch (Exception ex)
        {
            Log.Warn("suspended.json ilegível", ex);
            AtomicFile.Backup(FilePath(paths));
            return new();
        }
    }

    /// <summary>Only well-formed records: a plain process name, a real pid and a creation time. No side effects.</summary>
    internal static List<SuspendedProcess> Parse(string json)
    {
        var list = JsonSerializer.Deserialize<List<SuspendedProcess?>>(json, JsonDefaults.Options) ?? new();
        return list
            .Where(r => r is not null && r.Pid > 4 && r.CreationTimeUtc > 0 && ProcessNames.IsValid(r.Name))
            .Select(r => r!)
            .GroupBy(r => r.Pid)
            .Select(g => g.First())
            .Take(500)
            .ToList();
    }

    internal static void Save(AppPaths paths, List<SuspendedProcess> records)
    {
        var f = FilePath(paths);
        if (records.Count == 0)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch (Exception ex) { Log.Warn("Não foi possível apagar suspended.json", ex); }
            return;
        }
        AtomicFile.WriteAllText(f, JsonSerializer.Serialize(records, JsonDefaults.Options));
    }
}
