using System.Drawing;
using System.Drawing.Imaging;

namespace DogePet;

/// <summary>
/// Small always-on-top transparent window that displays an animated coin on the desktop.
/// </summary>
internal sealed class CoinOverlayForm : Form
{
    private Image[] _frames = Array.Empty<Image>();
    private int _frameIndex;

    public CoinOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExLayered = 0x00080000;
            const int wsExToolWindow = 0x00000080;

            var cp = base.CreateParams;
            cp.ExStyle |= wsExLayered | wsExToolWindow;
            return cp;
        }
    }

    public void SetFrames(Image[] frames)
    {
        _frames = frames;
        _frameIndex = 0;

        if (frames.Length > 0)
            ClientSize = new Size(frames[0].Width, frames[0].Height);
    }

    public void ShowAt(Point screenCenter)
    {
        if (_frames.Length == 0)
            return;

        var visualCenter = GetVisualCenter();
        Location = new Point(
            screenCenter.X - visualCenter.X,
            screenCenter.Y - visualCenter.Y);

        if (!Visible)
            Show();

        ApplyLayeredFrame();
        BringToFront();
    }

    public void AdvanceFrame()
    {
        if (_frames.Length == 0)
            return;

        _frameIndex = (_frameIndex + 1) % _frames.Length;
        ApplyLayeredFrame();
    }

    public Point ScreenCenter
    {
        get
        {
            var visualCenter = GetVisualCenter();
            return new Point(Location.X + visualCenter.X, Location.Y + visualCenter.Y);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_frames.Length > 0)
            ApplyLayeredFrame();
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);

        if (!IsDisposed && Visible)
            ApplyLayeredFrame();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Layered-window rendering handles drawing.
    }

    private void ApplyLayeredFrame()
    {
        if (_frames.Length == 0 || IsDisposed || !IsHandleCreated)
            return;

        using var bitmap = new Bitmap(_frames[_frameIndex].Width, _frames[_frameIndex].Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(_frames[_frameIndex], 0, 0);
        }

        LayeredWindowHelper.SetBitmap(this, bitmap);
    }

    private static Point GetVisualCenter() =>
        new(MainForm.CoinVisualCenterX, MainForm.CoinVisualCenterY);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var frame in _frames)
                frame.Dispose();
        }

        base.Dispose(disposing);
    }
}