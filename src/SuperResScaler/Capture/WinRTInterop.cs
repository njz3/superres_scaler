using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace SuperResScaler.Capture;

/// <summary>
/// Bridges between Windows.Graphics.Capture (WinRT) and Direct3D 11 (Vortice).
/// The classic COM interop interfaces are called directly through their vtables so that
/// nothing depends on built-in COM marshalling.
/// </summary>
internal static unsafe class WinRTInterop
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IID_ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    /// <summary>Wraps a D3D11 device into the WinRT IDirect3DDevice expected by the capture API.</summary>
    public static IDirect3DDevice CreateDirect3DDevice(IDXGIDevice dxgiDevice)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr inspectable));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    /// <summary>IGraphicsCaptureItemInterop::CreateForWindow.</summary>
    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out IntPtr hstring));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, IID_IGraphicsCaptureItemInterop, out IntPtr interop));
            try
            {
                // IUnknown (3 slots), then CreateForWindow, CreateForMonitor.
                var createForWindow = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)interop)[3];
                Guid iid = IID_IGraphicsCaptureItem;
                IntPtr itemPtr;
                Marshal.ThrowExceptionForHR(createForWindow(interop, hwnd, &iid, &itemPtr));
                try
                {
                    return GraphicsCaptureItem.FromAbi(itemPtr);
                }
                finally
                {
                    Marshal.Release(itemPtr);
                }
            }
            finally
            {
                Marshal.Release(interop);
            }
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    /// <summary>IDirect3DDxgiInterfaceAccess::GetInterface(ID3D11Texture2D). The caller owns the texture.</summary>
    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        IntPtr surfacePtr = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            Guid accessIid = IID_IDirect3DDxgiInterfaceAccess;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfacePtr, in accessIid, out IntPtr access));
            try
            {
                var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)access)[3];
                Guid textureIid = IID_ID3D11Texture2D;
                IntPtr texture;
                Marshal.ThrowExceptionForHR(getInterface(access, &textureIid, &texture));
                return new ID3D11Texture2D(texture);
            }
            finally
            {
                Marshal.Release(access);
            }
        }
        finally
        {
            Marshal.Release(surfacePtr);
        }
    }
}
