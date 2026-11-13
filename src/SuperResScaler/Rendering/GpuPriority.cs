// Copyright (c) Benjamin Maurin (njz3) 2026

using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SuperResScaler.Rendering;

/// <summary>
/// Puts SuperResScaler's GPU work ahead of the game's. Copying and scaling a frame takes the GPU
/// a fraction of a millisecond, but behind a game that keeps the GPU busy it can wait a whole
/// refresh and miss it: a frame lost. Raised like OBS does: the process's GPU scheduling class
/// (high) and the device's GPU thread priority. Either may be refused; the scaling still works.
/// </summary>
internal static class GpuPriority
{
    private const int SchedulingPriorityHigh = 4; // D3DKMT_SCHEDULINGPRIORITYCLASS_HIGH
    private const int HighestThreadPriority = 7;
    private static int _processRaised;

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr process, int priority);

    public static void Raise(ID3D11Device device)
    {
        if (Interlocked.Exchange(ref _processRaised, 1) == 0)
        {
            try
            {
                int status = D3DKMTSetProcessSchedulingPriorityClass(new IntPtr(-1), SchedulingPriorityHigh); // current process
                Log.Info(status == 0 ? "GPU scheduling priority: high" : $"GPU scheduling priority left normal (NTSTATUS 0x{status:X8})");
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
            {
                Log.Warn($"GPU scheduling priority: {ex.Message}");
            }
        }
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            var result = dxgiDevice.SetGPUThreadPriority(HighestThreadPriority);
            if (result.Failure)
                Log.Info($"GPU thread priority left normal ({result.Code:X8})");
        }
        catch (Exception ex)
        {
            Log.Warn($"GPU thread priority: {ex.Message}");
        }
    }
}
