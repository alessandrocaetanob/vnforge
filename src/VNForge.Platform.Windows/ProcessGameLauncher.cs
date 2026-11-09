using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

using VNForge.Core.Library;

namespace VNForge.Platform.Windows;

/// <summary>
/// O <see cref="IGameLauncher"/> de verdade: inicia o programa e o coloca num job object do Windows, para que os filhos dele (o jogo aberto por um lançador, o jogo
/// aberto pelo <c>LEProc.exe</c>) entrem na contagem mesmo depois que o pai sai.
/// Limites: o processo é colocado no job logo depois de iniciado, então um filho criado nesse intervalo de milissegundos fica de fora; e um jogo que pede
/// administrador abre pelo shell (com o aviso do UAC) e não entra no job, então só o processo aberto é acompanhado, sem os filhos.
/// </summary>
public sealed class ProcessGameLauncher : IGameLauncher
{
    // ERROR_ELEVATION_REQUIRED: o executável pede administrador e o CreateProcess recusa.
    private const int ElevationRequired = 740;

    public IGameProcess Start(GameLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (!OperatingSystem.IsWindows())
        {
            throw new LibraryException("Abrir o jogo pela biblioteca só funciona no Windows.");
        }

        return StartOnWindows(launch);
    }

    [SupportedOSPlatform("windows")]
    private static JobGameProcess StartOnWindows(GameLaunch launch)
    {
        var process = Open(launch, useShell: false);
        var job = JobObject.TryCreate();
        if (job is not null && !TryAssign(job, process))
        {
            job.Dispose();
            job = null;
        }

        return new JobGameProcess(process, job);
    }

    private static Process Open(GameLaunch launch, bool useShell)
    {
        var info = new ProcessStartInfo(launch.Executable) { WorkingDirectory = launch.WorkingDirectory, UseShellExecute = useShell };
        foreach (var argument in launch.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(info) ?? throw new LibraryException($"O sistema não abriu {launch.Executable}.");
        }
        catch (Win32Exception e) when (!useShell && e.NativeErrorCode == ElevationRequired)
        {
            return Open(launch, useShell: true);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            throw new LibraryException($"Não foi possível abrir {launch.Executable}: {e.Message}", e);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryAssign(JobObject job, Process process)
    {
        try
        {
            return job.TryAssign(process.Handle);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // O processo já saiu: não há o que acompanhar no job.
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class JobGameProcess(Process root, JobObject? job) : IGameProcess
    {
        public GameProcessState Poll()
        {
            var ids = job is not null && job.TryListProcesses(out var inJob) ? inJob : RootOnly();
            if (ids.Count == 0)
            {
                return GameProcessState.Exited;
            }

            return ids.All(IsSuspended) ? GameProcessState.Suspended : GameProcessState.Running;
        }

        public void Dispose()
        {
            job?.Dispose();
            root.Dispose();
        }

        private int[] RootOnly()
        {
            try
            {
                return root.HasExited ? [] : [root.Id];
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                return [];
            }
        }

        // O Windows não tem "processo suspenso": ele está suspenso quando todas as threads esperam com o motivo Suspended.
        private static bool IsSuspended(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var threads = process.Threads;
                if (threads.Count == 0)
                {
                    return false;
                }

                foreach (ProcessThread thread in threads)
                {
                    using (thread)
                    {
                        if (thread.ThreadState != System.Diagnostics.ThreadState.Wait || thread.WaitReason != ThreadWaitReason.Suspended)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or Win32Exception or NotSupportedException)
            {
                return false;
            }
        }
    }
}
