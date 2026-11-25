using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace MouseDock.Services;

/// <summary>Captures one wallpaper HWND and reuses a staging texture and WPF bitmap for successive frames.</summary>
internal sealed class WallpaperWindowCapture : IDisposable
{
    private const int Bgra8 = 87;
    private const int DxgiStillDrawing = unchecked((int)0x887A000A);
    // COM vtable slots from d3d11.h and windows.graphics.capture.interop.h.
    private const int CreateTexture2DSlot = 5, MapSlot = 14, UnmapSlot = 15, CopyResourceSlot = 47, FlushSlot = 111;
    private const int InteropMethodSlot = 3;
    private static readonly Guid DxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid SurfaceAccess = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private IntPtr _nativeDevice, _context, _staging;
    private IDirect3DDevice _device;
    private Direct3D11CaptureFramePool _pool;
    private GraphicsCaptureSession _session;
    private SizeInt32 _poolSize;
    private WriteableBitmap _bitmap;
    private int _textureWidth, _textureHeight;
    private bool _disposed, _copyPending;

    internal WallpaperWindowCapture(IntPtr window)
    {
        try
        {
            if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Window capture is unavailable.");
            var item = CreateItem(window);
            ThrowIfFailed(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7,
                out _nativeDevice, out _, out _context));
            var iid = DxgiDevice;
            ThrowIfFailed(Marshal.QueryInterface(_nativeDevice, in iid, out var dxgi));
            try
            {
                ThrowIfFailed(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
                try { _device = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
                finally { Marshal.Release(inspectable); }
            }
            finally { Marshal.Release(dxgi); }

            _poolSize = item.Size;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
            _session = _pool.CreateCaptureSession(item);
            _session.IsCursorCaptureEnabled = false;
            _session.StartCapture();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal WriteableBitmap CopyLatestFrame()
    {
        var frame = _pool.TryGetNextFrame();
        if (frame == null) return null;
        var size = frame.ContentSize;
        if (size.Width < 1 || size.Height < 1 || size.Width > 4096 || size.Height > 4096)
        {
            frame.Dispose();
            return null;
        }
        if (size.Width != _poolSize.Width || size.Height != _poolSize.Height)
        {
            frame.Dispose();
            _pool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            _poolSize = size;
            _copyPending = false;
            return null;
        }

        using var captureFrame = frame;
        var surface = frame.Surface;
        bool published;
        var surfaceAbi = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var iid = SurfaceAccess;
            ThrowIfFailed(Marshal.QueryInterface(surfaceAbi, in iid, out var access));
            try
            {
                iid = Texture2D;
                ThrowIfFailed(Method<GetInterface>(access, InteropMethodSlot)(access, ref iid, out var source));
                try { published = Copy(source, surface.Description.Width, surface.Description.Height, size); }
                finally { Marshal.Release(source); }
            }
            finally { Marshal.Release(access); }
        }
        finally { Marshal.Release(surfaceAbi); }
        return published ? _bitmap : null;
    }

    private bool Copy(IntPtr source, int width, int height, SizeInt32 size)
    {
        if (_staging == IntPtr.Zero || _textureWidth != width || _textureHeight != height)
        {
            _copyPending = false;
            Release(ref _staging);
            var description = new TextureDescription
            {
                Width = width, Height = height, MipLevels = 1, ArraySize = 1, Format = Bgra8,
                SampleCount = 1, Usage = 3, CpuAccess = 0x20000
            };
            ThrowIfFailed(Method<CreateTexture>(_nativeDevice, CreateTexture2DSlot)(_nativeDevice, ref description, IntPtr.Zero, out _staging));
            _textureWidth = width;
            _textureHeight = height;
        }
        bool published = _copyPending;
        if (_copyPending)
        {
            // Read the previous copy only when it is ready. The dispatcher must never wait for the GPU.
            int status = Method<Map>(_context, MapSlot)(_context, _staging, 0, 1, 0x100000, out var resource);
            if (status == DxgiStillDrawing) return false;
            ThrowIfFailed(status);
            try
            {
                if (_bitmap == null || _bitmap.PixelWidth != size.Width || _bitmap.PixelHeight != size.Height)
                    _bitmap = new WriteableBitmap(size.Width, size.Height, 96, 96, PixelFormats.Bgr32, null);
                _bitmap.WritePixels(new Int32Rect(0, 0, size.Width, size.Height), resource.Data,
                    checked(resource.RowPitch * size.Height), resource.RowPitch);
            }
            finally { Method<Unmap>(_context, UnmapSlot)(_context, _staging, 0); }
        }
        Method<CopyResource>(_context, CopyResourceSlot)(_context, _staging, source);
        _copyPending = true;
        Method<Flush>(_context, FlushSlot)(_context);
        return published;
    }

    private static GraphicsCaptureItem CreateItem(IntPtr window)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        ThrowIfFailed(WindowsCreateString(name, name.Length, out var value));
        try
        {
            var iid = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
            ThrowIfFailed(RoGetActivationFactory(value, ref iid, out var factory));
            try
            {
                iid = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
                ThrowIfFailed(Method<CreateForWindow>(factory, InteropMethodSlot)(factory, window, ref iid, out var item));
                try { return MarshalInterface<GraphicsCaptureItem>.FromAbi(item); }
                finally { Marshal.Release(item); }
            }
            finally { Marshal.Release(factory); }
        }
        finally { WindowsDeleteString(value); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
        _pool?.Dispose();
        _device?.Dispose();
        Release(ref _staging);
        Release(ref _context);
        Release(ref _nativeDevice);
    }

    private static void Release(ref IntPtr pointer)
    {
        if (pointer != IntPtr.Zero) Marshal.Release(pointer);
        pointer = IntPtr.Zero;
    }

    private static T Method<T>(IntPtr obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));

    private static void ThrowIfFailed(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDescription
    {
        public int Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccess, Misc;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MappedResource { public IntPtr Data; public int RowPitch, DepthPitch; }
    private delegate int CreateForWindow(IntPtr self, IntPtr window, ref Guid iid, out IntPtr result);
    private delegate int GetInterface(IntPtr self, ref Guid iid, out IntPtr result);
    private delegate int CreateTexture(IntPtr self, ref TextureDescription description, IntPtr data, out IntPtr texture);
    private delegate void CopyResource(IntPtr self, IntPtr destination, IntPtr source);
    private delegate int Map(IntPtr self, IntPtr texture, int subresource, int type, int flags, out MappedResource resource);
    private delegate void Unmap(IntPtr self, IntPtr texture, int subresource);
    private delegate void Flush(IntPtr self);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr levels, uint levelCount, uint sdk, out IntPtr device, out int featureLevel, out IntPtr context);
    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr device);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out IntPtr value);
    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
}
