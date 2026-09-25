using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using D3D9 = Vortice.Direct3D9;
using D3D11 = SlimDX.Direct3D11;

namespace Street_Rod_AC.Controls;

/// <summary>
/// Puts what the AcTools renderer draws on a WPF surface.
///
/// The renderer works in DX11 and WPF composes in D3D9, so the two never meet directly. The renderer's
/// target is created as a shared resource; this opens that same texture on a D3D9Ex device of its own and
/// hands its surface to a <see cref="D3DImage"/>. Nothing is copied: both APIs draw on the one texture.
///
/// One of these belongs to one viewport, and the viewport disposes it when it is unloaded.
/// </summary>
public sealed partial class SharedTextureBridge : IDisposable
{
    private readonly CopyableD3DImage _image = new();

    private D3D9.IDirect3D9Ex? _d3d9;
    private D3D9.IDirect3DDevice9Ex? _device;
    private D3D9.IDirect3DTexture9? _texture;
    private D3D9.IDirect3DSurface9? _surface;
    private IntPtr _boundTarget;

    public SharedTextureBridge()
    {
        _image.IsFrontBufferAvailableChanged += (_, _) =>
        {
            // The front buffer goes away on a lock screen or a remote desktop session. When it comes back the
            // old binding is stale, so drop it and let the next present build a new one.
            if (_image.IsFrontBufferAvailable)
            {
                _boundTarget = IntPtr.Zero;
                FrontBufferRestored?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>The image source to put in the visual tree</summary>
    public D3DImage Image => _image;

    /// <summary>
    /// A software copy of what is on screen, for a cut to dissolve from. Null when there is nothing to copy or the
    /// copy fails: the caller cuts instead.
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? CopyFrame()
    {
        if (_boundTarget == IntPtr.Zero || !_image.IsFrontBufferAvailable) return null;

        try
        {
            var copy = _image.Copy();
            copy?.Freeze();
            return copy;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>WPF can present: false while the device is lost</summary>
    public bool IsFrontBufferAvailable => _image.IsFrontBufferAvailable;

    /// <summary>Which render target is on screen, or zero when nothing has been presented yet</summary>
    public IntPtr BoundTarget => _boundTarget;

    /// <summary>The front buffer came back and whatever is drawing should draw again</summary>
    public event EventHandler? FrontBufferRestored;

    /// <summary>
    /// Creates the D3D9Ex device. Safe to call more than once.
    /// </summary>
    public void EnsureDevice()
    {
        if (_device != null) return;

        // Into locals first: a device that cannot be made (a remote session, a driver in the middle of a reset) must
        // not leave half of itself behind for the next try to overwrite
        var d3d9 = D3D9.D3D9.Direct3DCreate9Ex();
        var parameters = new D3D9.PresentParameters
        {
            Windowed = true,
            SwapEffect = D3D9.SwapEffect.Discard,
            DeviceWindowHandle = GetDesktopWindow(),
            PresentationInterval = D3D9.PresentInterval.Immediate,
            BackBufferWidth = 1,
            BackBufferHeight = 1
        };

        try
        {
            _device = d3d9.CreateDeviceEx(0, D3D9.DeviceType.Hardware, IntPtr.Zero,
                D3D9.CreateFlags.HardwareVertexProcessing | D3D9.CreateFlags.Multithreaded | D3D9.CreateFlags.FpuPreserve,
                parameters);
        }
        catch
        {
            d3d9.Dispose();
            throw;
        }

        _d3d9?.Dispose();
        _d3d9 = d3d9;
    }

    /// <summary>
    /// Shows the given render target. Rebinds first when it is not the one already on screen, which is what
    /// happens after a resize: the renderer makes a new target and the old pointer is dead.
    /// </summary>
    public void Present(IntPtr renderTarget)
    {
        if (renderTarget != _boundTarget)
        {
            Bind(renderTarget);
        }

        // Unlocked whatever happens: an image left locked never updates again
        _image.Lock();
        try
        {
            _image.AddDirtyRect(new Int32Rect(0, 0, _image.PixelWidth, _image.PixelHeight));
        }
        finally
        {
            _image.Unlock();
        }
    }

    /// <summary>
    /// Opens the renderer's shared DX11 texture on the D3D9Ex device and hands its surface to the D3DImage
    /// </summary>
    private void Bind(IntPtr renderTarget)
    {
        ReleaseSurface();

        // Owned by the renderer - do not dispose
        var texture = D3D11.Texture2D.FromPointer(renderTarget);
        var description = texture.Description;

        IntPtr sharedHandle;
        using (var resource = new SlimDX.DXGI.Resource(texture))
        {
            sharedHandle = resource.SharedHandle;
        }

        if (sharedHandle == IntPtr.Zero)
            throw new InvalidOperationException("Render target is not a shared resource");

        _texture = _device!.CreateTexture((uint)description.Width, (uint)description.Height, 1,
            D3D9.Usage.RenderTarget, D3D9.Format.A8R8G8B8, D3D9.Pool.Default, ref sharedHandle);
        _surface = _texture.GetSurfaceLevel(0);

        _image.Lock();
        try
        {
            _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface.NativePointer);
        }
        finally
        {
            _image.Unlock();
        }

        _boundTarget = renderTarget;
    }

    /// <summary>
    /// Lets go of the texture on screen, leaving the device up. Call before the renderer that owns the
    /// target is disposed.
    /// </summary>
    public void ReleaseSurface()
    {
        if (_boundTarget != IntPtr.Zero)
        {
            _image.Lock();
            try
            {
                _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
            }
            finally
            {
                _image.Unlock();
            }
        }

        _surface?.Dispose();
        _surface = null;
        _texture?.Dispose();
        _texture = null;
        _boundTarget = IntPtr.Zero;
    }

    public void Dispose()
    {
        ReleaseSurface();

        _device?.Dispose();
        _device = null;
        _d3d9?.Dispose();
        _d3d9 = null;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDesktopWindow();
}

/// <summary>D3DImage keeps its read-back protected; this lets the bridge take a still of the picture</summary>
internal sealed class CopyableD3DImage : D3DImage
{
    public System.Windows.Media.Imaging.BitmapSource? Copy() => CopyBackBuffer();

    protected override Freezable CreateInstanceCore() => new CopyableD3DImage();
}
