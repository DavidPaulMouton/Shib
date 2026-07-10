using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

using Timer = System.Windows.Forms.Timer;

namespace DogePet;

/// <summary>
/// Main desktop pet form - a tiny always-on-top Doge-style Shiba Inu.
/// </summary>
public partial class MainForm : Form
{
    // Animation & state
    private Timer _animationTimer = null!;
    private readonly Random _rng = new();

    private PetState _currentState = PetState.Idle;
    private int _frameIndex;
    private int _stateTimeLeft; // how many ticks to stay in special state

    private float _happiness = 80f; // 0-100, decays slowly

    // Doge-style pixel art sprites (preferred)
    private Image? _frontIdle, _frontBlink, _frontHappy;
    private Image? _rightWalk1, _rightWalk2, _rightWalk3, _rightWalk4, _rightWalk5, _rightWalk6;

    // Dragging support (with threshold so clicks are reliable)
    private bool _isDragging;
    private Point _dragStartScreenPos;
    private Point _dragStartFormPos;
    private const int DragThreshold = 5; // pixels before we consider it a drag instead of a pat

    // Roaming
    private Point? _walkTarget;
    private DateTime _nextWalkTime = DateTime.UtcNow.AddSeconds(12);
    private Facing _facing = Facing.Front;
    private bool _roamingEnabled = true;
    private ToolStripMenuItem? _roamingMenuItem;

    // Follow cursor
    private bool _followEnabled;
    private ToolStripMenuItem? _followMenuItem;
    private Point _lastMousePosition;
    private DateTime _lastMouseActivityTime = DateTime.UtcNow;
    private bool _reachedFollowTarget;
    private DateTime _roamAllowedAfter = DateTime.MinValue;
    private const double FollowInactivitySeconds = 10;
    private const double FollowArrivalPauseSeconds = 5;
    private const int FollowMouseMoveThreshold = 12;

    // Fetch coin
    private const int CoinVisualSize = 24;
    private const int CoinCanvasPadding = 8;
    private const int CoinCanvasWidth = CoinVisualSize + CoinCanvasPadding * 2;
    private const int CoinCanvasHeight = CoinVisualSize + CoinCanvasPadding * 2;
    internal const int CoinVisualCenterX = CoinCanvasPadding + CoinVisualSize / 2;
    internal const int CoinVisualCenterY = CoinCanvasPadding + CoinVisualSize / 2;

    private bool _fetchEnabled;
    private bool _syncingFollowFetchMenus;
    private ToolStripMenuItem? _fetchMenuItem;
    private CoinOverlayForm? _coinOverlay;
    private Point? _coinCenter;

    private enum RoamingSpeed { Slow, Medium, Fast, Ludicrous }
    private RoamingSpeed _currentSpeed = RoamingSpeed.Medium;

    private ToolStripMenuItem? _speedSlow;
    private ToolStripMenuItem? _speedMedium;
    private ToolStripMenuItem? _speedFast;
    private ToolStripMenuItem? _speedLudicrous;

    // Pet size (small classic desktop pet size)
    private const int PetSize = 80;   // matches the new clean 80x80 Doge pixel sprites

    public MainForm()
    {
        InitializeComponent();
        LoadSprites();
        LoadCoinSprite();
        SetupForm();
        SetupTimer();

        // Force the window to be visible (helps with debugging transparency/position issues)
        this.Visible = true;
        this.BringToFront();
        this.Activate();

        Console.WriteLine("DogePet started successfully. Window should be visible now.");
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // Proper desktop pet look
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        ClientSize = new Size(118, 105);

        // Default position: bottom right corner
        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(
            screen.Right - ClientSize.Width - 30,
            screen.Bottom - ClientSize.Height - 50
        );

        // Context menu on right-click
        var menu = new ContextMenuStrip();

        var petItem = new ToolStripMenuItem("Pet the Shiba");
        petItem.Click += (_, _) => TriggerHappy(12);

        _roamingMenuItem = new ToolStripMenuItem("Roaming Enabled");
        _roamingMenuItem.CheckOnClick = true;
        _roamingMenuItem.Checked = _roamingEnabled;
        _roamingMenuItem.CheckedChanged += RoamingMenuItem_CheckedChanged;

        _followMenuItem = new ToolStripMenuItem("Follow Cursor");
        _followMenuItem.CheckOnClick = true;
        _followMenuItem.Checked = _followEnabled;
        _followMenuItem.CheckedChanged += FollowMenuItem_CheckedChanged;

        _fetchMenuItem = new ToolStripMenuItem("Fetch Coin");
        _fetchMenuItem.CheckOnClick = true;
        _fetchMenuItem.Checked = _fetchEnabled;
        _fetchMenuItem.CheckedChanged += FetchMenuItem_CheckedChanged;

        var alwaysOnTopItem = new ToolStripMenuItem("Always on Top");
        alwaysOnTopItem.CheckOnClick = true;
        alwaysOnTopItem.Checked = TopMost;
        alwaysOnTopItem.CheckedChanged += (s, e) => TopMost = alwaysOnTopItem.Checked;

        // Speed submenu
        var speedMenu = new ToolStripMenuItem("Speed");

        _speedSlow      = new ToolStripMenuItem("Slow");
        _speedMedium    = new ToolStripMenuItem("Medium");
        _speedFast      = new ToolStripMenuItem("Fast");
        _speedLudicrous = new ToolStripMenuItem("Ludicrous");

        _speedSlow.CheckOnClick = true;
        _speedMedium.CheckOnClick = true;
        _speedFast.CheckOnClick = true;
        _speedLudicrous.CheckOnClick = true;

        _speedSlow.Click      += (s, e) => SetRoamingSpeed(RoamingSpeed.Slow);
        _speedMedium.Click    += (s, e) => SetRoamingSpeed(RoamingSpeed.Medium);
        _speedFast.Click      += (s, e) => SetRoamingSpeed(RoamingSpeed.Fast);
        _speedLudicrous.Click += (s, e) => SetRoamingSpeed(RoamingSpeed.Ludicrous);

        speedMenu.DropDownItems.Add(_speedSlow);
        speedMenu.DropDownItems.Add(_speedMedium);
        speedMenu.DropDownItems.Add(_speedFast);
        speedMenu.DropDownItems.Add(_speedLudicrous);

        var aboutItem = new ToolStripMenuItem("About DogePet...");
        aboutItem.Click += (_, _) => ShowAbout();

        menu.Items.Add(petItem);
        menu.Items.Add("-");
        menu.Items.Add(_roamingMenuItem);
        menu.Items.Add(_followMenuItem);
        menu.Items.Add(_fetchMenuItem);
        menu.Items.Add(alwaysOnTopItem);
        menu.Items.Add(speedMenu);
        menu.Items.Add("-");
        menu.Items.Add("Reset Position", null, ResetPosition);
        menu.Items.Add("-");
        menu.Items.Add(aboutItem);
        menu.Items.Add("Exit", null, (_, _) => BeginInvoke(Close));

        ContextMenuStrip = menu;

        // Set initial checked state for speed
        SetRoamingSpeed(_currentSpeed, updateMenuOnly: true);

        // Mouse handlers for dragging and petting
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;

        ResumeLayout(false);
    }

    private void SetupForm()
    {
        // Optional: slight shadow / modern look can be added with P/Invoke later
    }

    private void SetupTimer()
    {
        _lastMousePosition = Cursor.Position;

        _animationTimer = new Timer
        {
            Interval = 120 // ~8 fps for cute chunky animation
        };
        _animationTimer.Tick += AnimationTick;
        _animationTimer.Start();
    }

    private void LoadSprites()
    {
        string externalSpritesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sprites");

        try
        {
            // Front sprites - use LoadWalkFrame for consistent character size with side views
            _frontIdle  = LoadSprite("front_idle.png", externalSpritesDir, walkFrame: true);
            _frontBlink = LoadSprite("front_blink.png", externalSpritesDir, walkFrame: true);
            _frontHappy = LoadSprite("front_happy.png", externalSpritesDir, walkFrame: true);

            // Side walking animation frames (6-frame walk cycle for smoother stride)
            _rightWalk1 = LoadSprite("right_walk1.png", externalSpritesDir, walkFrame: true);
            _rightWalk2 = LoadSprite("right_walk2.png", externalSpritesDir, walkFrame: true);
            _rightWalk3 = LoadSprite("right_walk3.png", externalSpritesDir, walkFrame: true);
            _rightWalk4 = LoadSprite("right_walk4.png", externalSpritesDir, walkFrame: true);
            _rightWalk5 = LoadSprite("right_walk5.png", externalSpritesDir, walkFrame: true);
            _rightWalk6 = LoadSprite("right_walk6.png", externalSpritesDir, walkFrame: true);

            // Legacy fallbacks (external folder only)
            if (_frontIdle == null)  _frontIdle  = LoadSprite("idle.png", externalSpritesDir, walkFrame: false);
            if (_frontBlink == null) _frontBlink = LoadSprite("blink.png", externalSpritesDir, walkFrame: false);
            if (_frontHappy == null) _frontHappy = _frontIdle;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load sprites:\n{ex.Message}\n\nPlace improved Doge-style PNGs in the 'Sprites' folder.",
                "DogePet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void LoadCoinSprite()
    {
        var frames = LoadCoinFrames();
        _coinOverlay = new CoinOverlayForm();

        if (frames.Length > 0)
            _coinOverlay.SetFrames(frames);
    }

    private static readonly string[] CoinFrameFiles =
    {
        "Coin 10.png",
        "Coin 11.png",
        "Coin 12.png",
        "Coin 13.png",
        "Coin 14.png",
        "Coin 15.png"
    };

    private static readonly string[] CoinEmbeddedResources =
    {
        "DogePet.Sprites.coin_10.png",
        "DogePet.Sprites.coin_11.png",
        "DogePet.Sprites.coin_12.png",
        "DogePet.Sprites.coin_13.png",
        "DogePet.Sprites.coin_14.png",
        "DogePet.Sprites.coin_15.png"
    };

    private Image[] LoadCoinFrames()
    {
        string externalDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Sprites", "Coin Flip");

        var frames = new List<Image>(CoinFrameFiles.Length);

        for (int i = 0; i < CoinFrameFiles.Length; i++)
        {
            string externalPath = Path.Combine(externalDir, CoinFrameFiles[i]);
            using Image? source = File.Exists(externalPath)
                ? Image.FromFile(externalPath)
                : LoadEmbeddedImage(CoinEmbeddedResources[i]);

            if (source == null)
                continue;

            frames.Add(ProcessCoinFrame(source));
        }

        return frames.ToArray();
    }

    private static Image? LoadEmbeddedImage(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        return stream == null ? null : Image.FromStream(stream);
    }

    private static Image ProcessCoinFrame(Image original)
    {
        using var prepared = NormalizeCoinCanvas(original);
        Rectangle bounds = GetCoinContentBounds(prepared);
        float centroidX = bounds.X + bounds.Width / 2f;
        float centroidY = bounds.Y + bounds.Height / 2f;

        // Scale the full source image so the coin content fits the target size.
        float scale = Math.Min(
            CoinVisualSize / (float)bounds.Width,
            CoinVisualSize / (float)bounds.Height);
        int drawWidth = Math.Max(1, (int)Math.Round(prepared.Width * scale));
        int drawHeight = Math.Max(1, (int)Math.Round(prepared.Height * scale));

        var frame = new Bitmap(CoinCanvasWidth, CoinCanvasHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.None;
            g.CompositingMode = CompositingMode.SourceOver;

            int x = CoinVisualCenterX - (int)Math.Round(centroidX * scale);
            int y = CoinVisualCenterY - (int)Math.Round(centroidY * scale);
            g.DrawImage(prepared, x, y, drawWidth, drawHeight);
        }

        return frame;
    }

    private static Bitmap NormalizeCoinCanvas(Image original)
    {
        using var source = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(original, 0, 0);
        }

        Rectangle bounds = GetCoinContentBounds(source);
        int padLeft = bounds.Left;
        int padTop = bounds.Top;
        int padRight = source.Width - bounds.Right;
        int padBottom = source.Height - bounds.Bottom;
        int targetPad = Math.Max(Math.Max(padLeft, padRight), Math.Max(padTop, padBottom));

        var normalized = new Bitmap(
            bounds.Width + targetPad * 2,
            bounds.Height + targetPad * 2,
            PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(normalized))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(
                source,
                targetPad - padLeft,
                targetPad - padTop);
        }

        return normalized;
    }

    private static Rectangle GetCoinContentBounds(Bitmap bmp)
    {
        int minX = bmp.Width;
        int minY = bmp.Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).A == 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
            return new Rectangle(0, 0, bmp.Width, bmp.Height);

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static Image? LoadSprite(string fileName, string externalSpritesDir, bool walkFrame)
    {
        string externalPath = Path.Combine(externalSpritesDir, fileName);
        if (File.Exists(externalPath))
            return walkFrame ? LoadWalkFrame(externalPath) : LoadAndPrepare(externalPath);

        using var stream = OpenEmbeddedSprite(fileName);
        if (stream == null) return null;

        using var original = Image.FromStream(stream);
        return walkFrame ? ProcessWalkFrame(original) : ProcessPreparedFrame(original);
    }

    private static Stream? OpenEmbeddedSprite(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceStream($"DogePet.Sprites.{fileName}");
    }

    private static Image? LoadAndPrepare(string path)
    {
        if (!File.Exists(path)) return null;

        using var original = Image.FromFile(path);
        return ProcessPreparedFrame(original);
    }

    private static Image ProcessPreparedFrame(Image original)
    {

        // Resize to target pet size — use NearestNeighbor for pixel art to keep the crisp Doge style
        var resized = new Bitmap(PetSize, PetSize);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(original, 0, 0, PetSize, PetSize);
        }

        // Re-enabled with a very high threshold.
        // Only removes pixels that are extremely close to pure white (255,255,255).
        // This should remove the background without eating the light cream parts of the Doge face.
        // Distance-based background removal using the actual corner color.
        // 30-40 usually works well for these pixel art sprites.
        MakeBackgroundTransparentSafe(resized, 36);

        return resized;
    }

    /// <summary>
    /// Loads a walk animation frame and normalizes the character size so it is always the same visual height
    /// and centered on the canvas. This fixes both the "growing/shrinking" bug and the "zoomed in orange box"
    /// problem when the AI draws the Shiba at inconsistent scales across frames.
    /// </summary>
    private static Image? LoadWalkFrame(string path)
    {
        if (!File.Exists(path)) return null;

        using var original = Image.FromFile(path);
        return ProcessWalkFrame(original);
    }

    private static Image ProcessWalkFrame(Image original)
    {
        // We want the Shiba to occupy a consistent portion of the 80x80 frame (e.g. ~68px tall)
        const int targetCharacterHeight = 68;

        // Calculate uniform scale so the sprite's height becomes targetCharacterHeight
        float scale = (float)targetCharacterHeight / original.Height;
        int newWidth = (int)Math.Round(original.Width * scale);
        int newHeight = targetCharacterHeight;

        var frame = new Bitmap(PetSize, PetSize);
        using (var g = Graphics.FromImage(frame))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // Center the scaled sprite
            int offsetX = (PetSize - newWidth) / 2;
            int offsetY = (PetSize - newHeight) / 2;

            g.DrawImage(original, offsetX, offsetY, newWidth, newHeight);
        }

        // Thorough background removal for walk frames.
        // Flood fill from the edges + brightness cleanup. This should finally eliminate
        // the flashing white box on the side profiles.
        RemoveBackgroundFromWalkFrame(frame);

        return frame;
    }

    /// <summary>
    /// Detects the background color from the image corners and removes all pixels
    /// that are similar to it. This is much more reliable for the current Doge
    /// sprites than brightness-only or flood-fill methods.
    /// </summary>
    private static void MakeBackgroundTransparentSafe(Bitmap bmp, int tolerance = 32)
    {
        int w = bmp.Width;
        int h = bmp.Height;

        if (w == 0 || h == 0) return;

        // Sample the actual background color from the four corners
        var c1 = bmp.GetPixel(0, 0);
        var c2 = bmp.GetPixel(w - 1, 0);
        var c3 = bmp.GetPixel(0, h - 1);
        var c4 = bmp.GetPixel(w - 1, h - 1);

        int bgR = (c1.R + c2.R + c3.R + c4.R) / 4;
        int bgG = (c1.G + c2.G + c3.G + c4.G) / 4;
        int bgB = (c1.B + c2.B + c3.B + c4.B) / 4;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = bmp.GetPixel(x, y);

                int dr = p.R - bgR;
                int dg = p.G - bgG;
                int db = p.B - bgB;

                // Euclidean distance in RGB space
                double distance = Math.Sqrt(dr * dr + dg * dg + db * db);

                if (distance <= tolerance)
                {
                    bmp.SetPixel(x, y, Color.Transparent);
                }
            }
        }
    }

    /// <summary>
    /// Aggressive background removal specifically for the side walk frames.
    /// Uses flood-fill from the borders + multiple brightness passes.
    /// Tuned to finally kill the last few stubborn white dots without eating the character.
    /// </summary>
    private static void RemoveBackgroundFromWalkFrame(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;

        bool[,] visited = new bool[w, h];
        Queue<Point> queue = new Queue<Point>();

        // Seed from the entire outer border
        for (int x = 0; x < w; x++)
        {
            TryEnqueueBright(bmp, queue, visited, x, 0, 220);
            TryEnqueueBright(bmp, queue, visited, x, h - 1, 220);
        }
        for (int y = 0; y < h; y++)
        {
            TryEnqueueBright(bmp, queue, visited, 0, y, 220);
            TryEnqueueBright(bmp, queue, visited, w - 1, y, 220);
        }

        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        while (queue.Count > 0)
        {
            Point p = queue.Dequeue();
            int x = p.X;
            int y = p.Y;

            if (x < 0 || x >= w || y < 0 || y >= h || visited[x, y])
                continue;

            visited[x, y] = true;

            var pixel = bmp.GetPixel(x, y);
            int brightness = (pixel.R + pixel.G + pixel.B) / 3;

            if (brightness > 220)
            {
                bmp.SetPixel(x, y, Color.Transparent);

                for (int d = 0; d < 4; d++)
                {
                    TryEnqueueBright(bmp, queue, visited, x + dx[d], y + dy[d], 220);
                }
            }
        }

        // Final multi-pass cleanup — very aggressive on bright pixels
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = bmp.GetPixel(x, y);
                    int brightness = (p.R + p.G + p.B) / 3;

                    // Very bright pixels (including slightly off-white from generation/scaling)
                    if (brightness > 232)
                    {
                        bmp.SetPixel(x, y, Color.Transparent);
                    }
                }
            }
        }
    }

    private static void TryEnqueueBright(Bitmap bmp, Queue<Point> queue, bool[,] visited, int x, int y, int threshold)
    {
        int w = bmp.Width;
        int h = bmp.Height;

        if (x < 0 || x >= w || y < 0 || y >= h || visited[x, y])
            return;

        var p = bmp.GetPixel(x, y);
        int brightness = (p.R + p.G + p.B) / 3;

        if (brightness > threshold)
        {
            visited[x, y] = true;
            queue.Enqueue(new Point(x, y));
        }
    }

    private void AnimationTick(object? sender, EventArgs e)
    {
        // Decay happiness slowly
        if (_happiness > 20)
            _happiness -= 0.08f;

        // Handle special state timeout (happy / blink)
        if (_stateTimeLeft > 0)
        {
            _stateTimeLeft--;
            if (_stateTimeLeft <= 0)
            {
                _currentState = ShouldBeWalking(Cursor.Position) ? PetState.Walking : PetState.Idle;
            }
        }

        var now = DateTime.UtcNow;
        Point mousePos = Cursor.Position;

        RegisterMouseMovement(mousePos, now);

        bool isFollowing = IsFollowingCursor();
        bool canRoam = CanStartRoaming(now);
        bool canMove = _stateTimeLeft <= 0 && !_isDragging;

        // === Roaming logic (paused while following, fetching, after arrival, or during lead window) ===
        if (canRoam && !_fetchEnabled && !_walkTarget.HasValue && now >= _nextWalkTime && _currentState == PetState.Idle)
        {
            _reachedFollowTarget = false;

            var screen = Screen.PrimaryScreen!.WorkingArea;
            int margin = 90;

            _walkTarget = new Point(
                _rng.Next(screen.Left + margin, screen.Right - margin),
                _rng.Next(screen.Top + 80, screen.Bottom - 140)
            );

            int roamDx = _walkTarget.Value.X - Location.X;
            _facing = Math.Abs(roamDx) > 20
                ? (roamDx > 0 ? Facing.Right : Facing.Left)
                : Facing.Front;

            _currentState = PetState.Walking;
        }

        Point? activeTarget = null;
        bool followingCursor = false;

        if (isFollowing && canMove)
        {
            activeTarget = GetFollowTarget(mousePos);
            followingCursor = true;
        }
        else if (IsFetchingCoin() && canMove)
        {
            activeTarget = GetCoinWalkTarget();
        }
        else if (_roamingEnabled && _walkTarget.HasValue)
        {
            activeTarget = _walkTarget;
        }

        if (activeTarget.HasValue && canMove)
        {
            var target = activeTarget.Value;
            var current = Location;

            int dx = target.X - current.X;
            int dy = target.Y - current.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            int directionThreshold = _currentSpeed == RoamingSpeed.Ludicrous ? 45 : 20;

            if (Math.Abs(dx) > directionThreshold)
                _facing = dx > 0 ? Facing.Right : Facing.Left;

            float arrivalThreshold = GetArrivalThreshold(followingCursor);

            if (distance <= arrivalThreshold)
            {
                if (followingCursor)
                    CompleteFollowArrival(target);
                else if (IsFetchingCoin())
                    CompleteCoinFetch();
                else
                {
                    _walkTarget = null;
                    _facing = Facing.Front;
                    _currentState = PetState.Idle;
                    _nextWalkTime = now.AddSeconds(_rng.Next(7, 20));
                }
            }
            else
            {
                if (followingCursor && distance <= GetMovementSpeed())
                    CompleteFollowArrival(target);
                else
                {
                    var nextPos = MoveToward(current, target, GetMovementSpeed(), ensureProgress: !followingCursor);
                    Location = followingCursor ? nextPos : ClampToWorkingArea(nextPos);
                    _currentState = PetState.Walking;
                }
            }
        }
        else if (canMove && _currentState == PetState.Walking && !_walkTarget.HasValue && !isFollowing && !IsFetchingCoin())
        {
            _currentState = PetState.Idle;
            _facing = Facing.Front;
        }

        if (_fetchEnabled && _coinOverlay?.Visible == true)
            _coinOverlay.AdvanceFrame();

        // Random idle animations (blink) when sitting still
        if (_currentState == PetState.Idle && _rng.Next(100) < 5)
        {
            if (_rng.Next(100) < 45)
            {
                _currentState = PetState.Blink;
                _stateTimeLeft = 2;
            }
        }

        // Advance animation frame (important for leg movement when walking)
        _frameIndex = (_frameIndex + 1) % 4;

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;

        // Transparent background (magenta is the transparency key)
        g.Clear(BackColor);

        // Render using Doge-style pixel sprites when available
        Image? sprite = null;
        float yOffset = 0;

        bool isHappy = _currentState == PetState.Happy || _currentState == PetState.Petted;
        bool isWalking = _currentState == PetState.Walking;
        bool flipHorizontal = false;

        if (isHappy)
        {
            // Always show the cute happy face when petted, even if we were in side view
            sprite = _frontHappy;
        }
        else if (_facing == Facing.Front)
        {
            if (_currentState == PetState.Blink)
                sprite = _frontBlink;
            else
                sprite = _frontIdle;
        }
        else if (_facing == Facing.Left)
        {
            // Mirror the right-facing walk frames so left has the exact same high-quality stride
            flipHorizontal = true;
            int frame = _frameIndex % 6;
            sprite = frame switch
            {
                0 => _rightWalk1,
                1 => _rightWalk2,
                2 => _rightWalk3,
                3 => _rightWalk4,
                4 => _rightWalk5,
                5 => _rightWalk6,
                _ => _rightWalk1
            };
        }
        else // Right - use original right frames
        {
            int frame = _frameIndex % 6;
            sprite = frame switch
            {
                0 => _rightWalk1,
                1 => _rightWalk2,
                2 => _rightWalk3,
                3 => _rightWalk4,
                4 => _rightWalk5,
                5 => _rightWalk6,
                _ => _rightWalk1
            };
        }

        if (sprite != null)
        {
            int x = (ClientSize.Width - sprite.Width) / 2;
            int y = (ClientSize.Height - sprite.Height) / 2;

            // Normal drawing — the new 6-frame walk animations contain proper leg movement
            if (isWalking)
            {
                // Smoother vertical bob synced to 6-frame cycle
                int phase = _frameIndex % 6;
                yOffset = phase switch
                {
                    0 => -1,
                    1 => -2,
                    2 => -1,
                    3 =>  0,
                    4 =>  1,
                    5 =>  0,
                    _ =>  0
                };
            }

            if (flipHorizontal)
            {
                // Draw mirrored (for left-facing using right sprites)
                g.DrawImage(sprite,
                    new Rectangle(x + sprite.Width, y + (int)yOffset, -sprite.Width, sprite.Height),
                    0, 0, sprite.Width, sprite.Height,
                    GraphicsUnit.Pixel);
            }
            else
            {
                g.DrawImage(sprite, x, y + (int)yOffset);
            }
        }
        else
        {
            // Fallback to vector drawing if sprites are missing
            if (_facing == Facing.Front)
                DrawFrontShiba(g, _currentState);
            else if (_facing == Facing.Left)
                DrawLeftProfile(g, _currentState);
            else
                DrawRightProfile(g, _currentState);
        }

        // (Sprite drawing temporarily disabled while we fix the art)
        /*
        Image? currentFrame = _currentState switch
        {
            PetState.Idle   => (_frameIndex % 3 == 1 && _blink != null) ? _blink : _idle,
            PetState.Blink  => _blink ?? _idle,
            PetState.Happy  => _happy ?? _idle,
            PetState.Petted => _petted ?? _happy ?? _idle,
            _               => _idle
        };

        if (currentFrame != null)
        {
            int x = (ClientSize.Width - currentFrame.Width) / 2;
            int y = (ClientSize.Height - currentFrame.Height) / 2;
            g.DrawImage(currentFrame, x, y);
        }
        else
        {
            DrawPlaceholderShiba(g);
        }
        */

        // (heart indicator removed per request)
    }

    private void DrawFrontShiba(Graphics g, PetState state)
    {
        int w = ClientSize.Width;
        int h = ClientSize.Height;
        float cx = w / 2f;
        float baseY = h * 0.62f;   // body sits lower in the window

        using var orange = new SolidBrush(Color.FromArgb(255, 165, 0));
        using var cream = new SolidBrush(Color.FromArgb(255, 250, 240));
        using var black = new SolidBrush(Color.Black);
        using var darkBrown = new SolidBrush(Color.FromArgb(139, 69, 19));
        using var pink = new SolidBrush(Color.FromArgb(255, 182, 193));
        using var tailOrange = new SolidBrush(Color.FromArgb(255, 140, 0));

        bool isHappy = state == PetState.Happy || state == PetState.Petted;
        bool isWalking = state == PetState.Walking;
        int f = _frameIndex % 4;

        // === Tail (behind the body) ===
        float tailWag = isHappy ? (float)Math.Sin(f * 1.2) * 6 : (isWalking ? (float)Math.Sin(f * 0.8) * 4 : 0);
        using var tailPen = new Pen(tailOrange, 9);
        tailPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
        tailPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
        // Tail curves from body
        g.DrawCurve(tailPen, new[]
        {
            new PointF(cx + 18, baseY - 8),
            new PointF(cx + 32 + tailWag * 0.3f, baseY - 18),
            new PointF(cx + 38 + tailWag, baseY - 5 + tailWag * 0.2f)
        });

        // === Body (chonky Shiba torso) ===
        g.FillEllipse(orange, cx - 26, baseY - 22, 52, 38);
        // Lighter belly
        g.FillEllipse(cream, cx - 18, baseY - 12, 36, 24);

        // === Hind legs ===
        g.FillEllipse(orange, cx - 20, baseY + 8, 14, 18);
        g.FillEllipse(orange, cx + 6, baseY + 8, 14, 18);

        // === Front legs (animated when walking) ===
        float legOffset = 0;
        if (isWalking)
        {
            legOffset = (f == 0 || f == 2) ? -2 : 2;
        }

        // Left front leg
        g.FillEllipse(orange, cx - 22 + legOffset, baseY + 5, 11, 20);
        // Right front leg
        g.FillEllipse(orange, cx + 11 - legOffset, baseY + 5, 11, 20);

        // === Head (on top of body) ===
        float headY = baseY - 32;
        float headBob = isWalking ? (f % 2 == 0 ? -1.5f : 1.5f) : 0;

        g.FillEllipse(orange, cx - 23, headY + headBob - 18, 46, 42);

        // Snout
        g.FillEllipse(cream, cx - 12, headY + headBob + 2, 24, 18);

        // Ears
        PointF[] leftEar = { new(cx - 21, headY + headBob - 12), new(cx - 33, headY + headBob - 32), new(cx - 10, headY + headBob - 14) };
        PointF[] rightEar = { new(cx + 21, headY + headBob - 12), new(cx + 33, headY + headBob - 32), new(cx + 10, headY + headBob - 14) };
        g.FillPolygon(orange, leftEar);
        g.FillPolygon(orange, rightEar);

        // === Face ===
        if (state == PetState.Blink)
        {
            g.DrawLine(Pens.Black, cx - 10, headY + headBob - 6, cx - 3, headY + headBob - 6);
            g.DrawLine(Pens.Black, cx + 4, headY + headBob - 6, cx + 11, headY + headBob - 6);
        }
        else if (isHappy)
        {
            // Happy squinty eyes
            g.DrawArc(Pens.Black, cx - 12, headY + headBob - 10, 9, 6, 0, 180);
            g.DrawArc(Pens.Black, cx + 3, headY + headBob - 10, 9, 6, 0, 180);

            // Blush
            g.FillEllipse(pink, cx - 18, headY + headBob + 2, 8, 5);
            g.FillEllipse(pink, cx + 10, headY + headBob + 2, 8, 5);

            // Big smile + tongue
            g.DrawArc(Pens.Black, cx - 6, headY + headBob + 8, 12, 8, 0, 180);
            g.FillEllipse(new SolidBrush(Color.FromArgb(255, 140, 150)), cx - 2, headY + headBob + 12, 5, 4);
        }
        else
        {
            // Normal eyes
            g.FillEllipse(black, cx - 11, headY + headBob - 9, 7, 9);
            g.FillEllipse(black, cx + 4, headY + headBob - 9, 7, 9);

            g.FillEllipse(cream, cx - 9, headY + headBob - 8, 3, 3);
            g.FillEllipse(cream, cx + 6, headY + headBob - 8, 3, 3);

            g.DrawArc(Pens.Black, cx - 5, headY + headBob + 7, 10, 6, 0, 180);
        }

        // Nose
        g.FillEllipse(darkBrown, cx - 4, headY + headBob + 5, 8, 6);
    }

    // ==================== SIDE PROFILES ====================

    private void DrawLeftProfile(Graphics g, PetState state)
    {
        // Dog facing left (head on the left side)
        float cx = ClientSize.Width / 2f + 8;
        float baseY = ClientSize.Height * 0.58f;

        using var orange = new SolidBrush(Color.FromArgb(255, 165, 0));
        using var cream = new SolidBrush(Color.FromArgb(255, 250, 240));
        using var black = new SolidBrush(Color.Black);
        using var darkBrown = new SolidBrush(Color.FromArgb(139, 69, 19));

        bool isHappy = state == PetState.Happy || state == PetState.Petted;
        bool isWalking = state == PetState.Walking;
        int f = _frameIndex % 4;

        float bob = isWalking ? (f % 2 == 0 ? -1.2f : 1.2f) : 0;

        // Tail (right side, behind)
        float tailWag = isHappy ? (float)Math.Sin(f * 1.1) * 5 : 0;
        g.FillEllipse(orange, cx + 12, baseY - 6 + tailWag * 0.3f, 22, 14);

        // Body (side view)
        g.FillEllipse(orange, cx - 8, baseY - 18, 42, 30);
        g.FillEllipse(cream, cx + 2, baseY - 10, 26, 18); // belly

        // Legs (side view - two legs)
        float legPhase = isWalking ? (f < 2 ? -2 : 2) : 0;
        g.FillEllipse(orange, cx + 2 + legPhase, baseY + 6, 9, 16);
        g.FillEllipse(orange, cx + 14 - legPhase * 0.6f, baseY + 7, 9, 15);

        // Head (facing left)
        g.FillEllipse(orange, cx - 32, baseY - 26 + bob, 28, 26);

        // Snout (pointing left)
        g.FillEllipse(cream, cx - 38, baseY - 18 + bob, 14, 12);

        // Ear
        g.FillPolygon(orange, new[]
        {
            new PointF(cx - 26, baseY - 24 + bob),
            new PointF(cx - 38, baseY - 38 + bob),
            new PointF(cx - 18, baseY - 22 + bob)
        });

        // Eye (simple)
        if (state == PetState.Blink || isHappy)
            g.DrawLine(Pens.Black, cx - 22, baseY - 18 + bob, cx - 15, baseY - 18 + bob);
        else
            g.FillEllipse(black, cx - 23, baseY - 20 + bob, 5, 6);

        // Nose
        g.FillEllipse(darkBrown, cx - 36, baseY - 14 + bob, 6, 5);
    }

    private void DrawRightProfile(Graphics g, PetState state)
    {
        // Dog facing right (head on the right side) - mirrored logic
        float cx = ClientSize.Width / 2f - 8;
        float baseY = ClientSize.Height * 0.58f;

        using var orange = new SolidBrush(Color.FromArgb(255, 165, 0));
        using var cream = new SolidBrush(Color.FromArgb(255, 250, 240));
        using var black = new SolidBrush(Color.Black);
        using var darkBrown = new SolidBrush(Color.FromArgb(139, 69, 19));

        bool isHappy = state == PetState.Happy || state == PetState.Petted;
        bool isWalking = state == PetState.Walking;
        int f = _frameIndex % 4;

        float bob = isWalking ? (f % 2 == 0 ? -1.2f : 1.2f) : 0;

        // Tail (left side, behind)
        float tailWag = isHappy ? (float)Math.Sin(f * 1.1) * 5 : 0;
        g.FillEllipse(orange, cx - 34, baseY - 6 + tailWag * 0.3f, 22, 14);

        // Body
        g.FillEllipse(orange, cx - 34, baseY - 18, 42, 30);
        g.FillEllipse(cream, cx - 28, baseY - 10, 26, 18);

        // Legs
        float legPhase = isWalking ? (f < 2 ? -2 : 2) : 0;
        g.FillEllipse(orange, cx - 11 + legPhase, baseY + 6, 9, 16);
        g.FillEllipse(orange, cx - 23 - legPhase * 0.6f, baseY + 7, 9, 15);

        // Head (facing right)
        g.FillEllipse(orange, cx + 4, baseY - 26 + bob, 28, 26);

        // Snout
        g.FillEllipse(cream, cx + 24, baseY - 18 + bob, 14, 12);

        // Ear
        g.FillPolygon(orange, new[]
        {
            new PointF(cx + 26, baseY - 24 + bob),
            new PointF(cx + 38, baseY - 38 + bob),
            new PointF(cx + 18, baseY - 22 + bob)
        });

        // Eye
        if (state == PetState.Blink || isHappy)
            g.DrawLine(Pens.Black, cx + 15, baseY - 18 + bob, cx + 22, baseY - 18 + bob);
        else
            g.FillEllipse(black, cx + 18, baseY - 20 + bob, 5, 6);

        // Nose
        g.FillEllipse(darkBrown, cx + 30, baseY - 14 + bob, 6, 5);
    }

    #region Interaction

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = false;
            _dragStartScreenPos = PointToScreen(e.Location);
            _dragStartFormPos = Location;
            Capture = true;
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Capture)
        {
            var currentScreen = PointToScreen(e.Location);
            int dx = currentScreen.X - _dragStartScreenPos.X;
            int dy = currentScreen.Y - _dragStartScreenPos.Y;

            if (! _isDragging && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
            {
                _isDragging = true;   // now it's a real drag
            }

            if (_isDragging)
            {
                Location = new Point(_dragStartFormPos.X + dx, _dragStartFormPos.Y + dy);
            }
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Capture = false;

            if (!_isDragging)
            {
                // It was a clean click / pat, not a drag
                TriggerHappy(25);                    // ~3 seconds of happy face
                _happiness = Math.Min(100, _happiness + 22);
            }

            _isDragging = false;
        }
    }

    private void TriggerHappy(int durationTicks)
    {
        _walkTarget = null;                    // Stop roaming when being petted
        _facing = Facing.Front;                // Turn to face the player for the happy reaction

        // If roaming was enabled, keep the menu item checked but pause roaming temporarily
        if (_roamingMenuItem != null)
            _roamingMenuItem.Checked = _roamingEnabled;

        _currentState = PetState.Petted;
        _stateTimeLeft = durationTicks;
        _frameIndex = 0;
        Invalidate();
    }

    private void ResetPosition(object? sender, EventArgs e)
    {
        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(screen.Right - PetSize - 40, screen.Bottom - PetSize - 60);
    }

    private float GetMovementSpeed()
    {
        return _currentSpeed switch
        {
            RoamingSpeed.Slow      => 1.2f,
            RoamingSpeed.Medium    => 3.0f,
            RoamingSpeed.Fast      => 13.6f,
            RoamingSpeed.Ludicrous => 120f,
            _ => 3.0f
        };
    }

    private float GetArrivalThreshold(bool followingCursor)
    {
        if (followingCursor)
            return 8f;

        return Math.Max(14f, GetMovementSpeed() * 0.75f);
    }

    private void CompleteFollowArrival(Point target)
    {
        Location = target;
        _reachedFollowTarget = true;
        _roamAllowedAfter = DateTime.UtcNow.AddSeconds(FollowArrivalPauseSeconds);
        _facing = Facing.Front;
        _currentState = PetState.Idle;
    }

    private void RegisterMouseMovement(Point mousePos, DateTime now)
    {
        int dx = mousePos.X - _lastMousePosition.X;
        int dy = mousePos.Y - _lastMousePosition.Y;
        if (dx * dx + dy * dy < FollowMouseMoveThreshold * FollowMouseMoveThreshold)
            return;

        _lastMousePosition = mousePos;
        _lastMouseActivityTime = now;

        if (_followEnabled)
        {
            _walkTarget = null;
            _reachedFollowTarget = false;
            _roamAllowedAfter = DateTime.MinValue;
        }
    }

    private bool CanStartRoaming(DateTime now)
    {
        if (!_roamingEnabled || IsFollowingCursor() || _fetchEnabled)
            return false;

        if (_reachedFollowTarget)
            return now >= _roamAllowedAfter;

        return !IsCursorLeadActive();
    }

    private bool IsCursorLeadActive()
    {
        return (DateTime.UtcNow - _lastMouseActivityTime).TotalSeconds < FollowInactivitySeconds;
    }

    private Point GetFollowTarget(Point mousePos)
    {
        var ideal = new Point(
            mousePos.X - ClientSize.Width / 2,
            mousePos.Y - ClientSize.Height / 2);
        return ClampToBounds(ideal, GetCombinedWorkingArea(), ClientSize);
    }

    private static Point MoveToward(Point current, Point target, float speed, bool ensureProgress)
    {
        int dx = target.X - current.X;
        int dy = target.Y - current.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < 0.001)
            return current;

        float moveDistance = Math.Min(speed, (float)distance);
        float moveX = (float)(dx / distance * moveDistance);
        float moveY = (float)(dy / distance * moveDistance);

        int newX = (int)Math.Round(current.X + moveX);
        int newY = (int)Math.Round(current.Y + moveY);

        if (ensureProgress && newX == current.X && newY == current.Y)
        {
            if (dx != 0) newX = current.X + Math.Sign(dx);
            if (dy != 0) newY = current.Y + Math.Sign(dy);
        }

        return new Point(newX, newY);
    }

    private static Rectangle GetCombinedWorkingArea()
    {
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;

        foreach (var screen in Screen.AllScreens)
        {
            var area = screen.WorkingArea;
            left = Math.Min(left, area.Left);
            top = Math.Min(top, area.Top);
            right = Math.Max(right, area.Right);
            bottom = Math.Max(bottom, area.Bottom);
        }

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Point ClampToBounds(Point location, Rectangle bounds, Size formSize)
    {
        int maxX = bounds.Right - formSize.Width;
        int maxY = bounds.Bottom - formSize.Height;
        return new Point(
            Math.Clamp(location.X, bounds.Left, Math.Max(bounds.Left, maxX)),
            Math.Clamp(location.Y, bounds.Top, Math.Max(bounds.Top, maxY)));
    }

    private bool IsFollowingCursor()
    {
        return _followEnabled && !_reachedFollowTarget;
    }

    private double GetDistanceToFollowTarget(Point mousePos)
    {
        var target = GetFollowTarget(mousePos);
        int dx = target.X - Location.X;
        int dy = target.Y - Location.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private bool ShouldBeWalking(Point mousePos)
    {
        if (_walkTarget.HasValue) return true;
        if (IsFetchingCoin()) return true;
        if (_stateTimeLeft > 0 || _isDragging) return false;
        return IsFollowingCursor();
    }

    private bool IsFetchingCoin() => _fetchEnabled && _coinCenter.HasValue && !IsFollowingCursor();

    private Point GetCoinWalkTarget()
    {
        var center = _coinCenter!.Value;
        return new Point(
            center.X - ClientSize.Width / 2,
            center.Y - ClientSize.Height / 2);
    }

    private Point PickRandomCoinCenter()
    {
        var bounds = GetCombinedWorkingArea();
        const int margin = 90;
        const int minDistanceFromDoge = 140;
        var dogeCenter = new Point(
            Location.X + ClientSize.Width / 2,
            Location.Y + ClientSize.Height / 2);

        int minX = bounds.Left + margin;
        int maxX = bounds.Right - margin;
        int minY = bounds.Top + margin;
        int maxY = bounds.Bottom - margin;

        if (maxX <= minX) maxX = minX + 1;
        if (maxY <= minY) maxY = minY + 1;

        for (int attempt = 0; attempt < 24; attempt++)
        {
            var candidate = new Point(_rng.Next(minX, maxX), _rng.Next(minY, maxY));
            int dx = candidate.X - dogeCenter.X;
            int dy = candidate.Y - dogeCenter.Y;

            if (dx * dx + dy * dy >= minDistanceFromDoge * minDistanceFromDoge)
                return candidate;
        }

        return new Point(_rng.Next(minX, maxX), _rng.Next(minY, maxY));
    }

    private void SpawnNextCoin()
    {
        if (_coinOverlay == null)
            return;

        _coinCenter = PickRandomCoinCenter();
        _coinOverlay.ShowAt(_coinCenter.Value);
        _walkTarget = GetCoinWalkTarget();

        int dx = _coinCenter.Value.X - (Location.X + ClientSize.Width / 2);
        _facing = Math.Abs(dx) > 20
            ? (dx > 0 ? Facing.Right : Facing.Left)
            : Facing.Front;

        _currentState = PetState.Walking;
    }

    private void CompleteCoinFetch()
    {
        _happiness = Math.Min(100, _happiness + 12);
        SpawnNextCoin();
    }

    private void HideCoin()
    {
        _coinCenter = null;
        _walkTarget = null;
        _coinOverlay?.Hide();
    }

    private Point ClampToWorkingArea(Point location)
        => ClampToBounds(location, GetCombinedWorkingArea(), ClientSize);

    private void FetchMenuItem_CheckedChanged(object? sender, EventArgs e)
    {
        if (_fetchMenuItem == null || _syncingFollowFetchMenus)
            return;

        if (_fetchMenuItem.Checked)
            SetExclusiveMode(fetch: true, follow: false);
        else
            SetFetchMode(false);
    }

    private void FollowMenuItem_CheckedChanged(object? sender, EventArgs e)
    {
        if (_followMenuItem == null || _syncingFollowFetchMenus)
            return;

        if (_followMenuItem.Checked)
            SetExclusiveMode(fetch: false, follow: true);
        else
            SetFollowMode(false);
    }

    private void SetExclusiveMode(bool fetch, bool follow)
    {
        _syncingFollowFetchMenus = true;
        try
        {
            if (_fetchMenuItem != null)
                _fetchMenuItem.Checked = fetch;

            if (_followMenuItem != null)
                _followMenuItem.Checked = follow;

            _fetchEnabled = fetch;
            _followEnabled = follow;

            if (fetch)
            {
                _reachedFollowTarget = false;
                _roamAllowedAfter = DateTime.MinValue;
                _walkTarget = null;
                SpawnNextCoin();
            }
            else
            {
                HideCoin();
            }

            if (follow)
            {
                _lastMousePosition = Cursor.Position;
                _lastMouseActivityTime = DateTime.UtcNow;
                _walkTarget = null;
                _reachedFollowTarget = false;
                _roamAllowedAfter = DateTime.MinValue;
            }
            else if (!fetch)
            {
                _reachedFollowTarget = false;
                _roamAllowedAfter = DateTime.MinValue;
            }
        }
        finally
        {
            _syncingFollowFetchMenus = false;
        }
    }

    private void SetFetchMode(bool enabled)
    {
        _syncingFollowFetchMenus = true;
        try
        {
            if (_fetchMenuItem != null)
                _fetchMenuItem.Checked = enabled;

            _fetchEnabled = enabled;

            if (enabled)
            {
                _walkTarget = null;
                SpawnNextCoin();
            }
            else
            {
                HideCoin();

                if (_currentState == PetState.Walking && !_followEnabled)
                {
                    _currentState = PetState.Idle;
                    _facing = Facing.Front;
                }
            }
        }
        finally
        {
            _syncingFollowFetchMenus = false;
        }
    }

    private void SetFollowMode(bool enabled)
    {
        _syncingFollowFetchMenus = true;
        try
        {
            if (_followMenuItem != null)
                _followMenuItem.Checked = enabled;

            _followEnabled = enabled;

            if (enabled)
            {
                _lastMousePosition = Cursor.Position;
                _lastMouseActivityTime = DateTime.UtcNow;
                _walkTarget = null;
                _reachedFollowTarget = false;
                _roamAllowedAfter = DateTime.MinValue;
            }
            else
            {
                _reachedFollowTarget = false;
                _roamAllowedAfter = DateTime.MinValue;

                if (_currentState == PetState.Walking && !_fetchEnabled)
                {
                    _currentState = PetState.Idle;
                    _facing = Facing.Front;
                }
            }
        }
        finally
        {
            _syncingFollowFetchMenus = false;
        }
    }

    private void RoamingMenuItem_CheckedChanged(object? sender, EventArgs e)
    {
        if (_roamingMenuItem == null) return;

        _roamingEnabled = _roamingMenuItem.Checked;

        if (!_roamingEnabled)
        {
            _walkTarget = null;
            if (_currentState == PetState.Walking)
                _currentState = PetState.Idle;
        }
        else
        {
            // Encourage it to start roaming again soon
            _nextWalkTime = DateTime.UtcNow.AddSeconds(2);
        }
    }



    private void SetRoamingSpeed(RoamingSpeed speed, bool updateMenuOnly = false)
    {
        _currentSpeed = speed;

        // Update menu checked states
        if (_speedSlow != null)      _speedSlow.Checked      = speed == RoamingSpeed.Slow;
        if (_speedMedium != null)    _speedMedium.Checked    = speed == RoamingSpeed.Medium;
        if (_speedFast != null)      _speedFast.Checked      = speed == RoamingSpeed.Fast;
        if (_speedLudicrous != null) _speedLudicrous.Checked = speed == RoamingSpeed.Ludicrous;

        if (!updateMenuOnly)
        {
            // Optionally give visual feedback when speed changes
            // For now we just change the internal speed
        }
    }

    private void ShowAbout()
    {
        string aboutText =
@"DogePet

A cute little Doge-style Shiba Inu that lives on your desktop.

Created by David Mouton

Roaming • Follow Cursor • Fetch Coin • Petting • Multiple Speeds

Thank you for playing with Doge!";

        MessageBox.Show(aboutText, "About DogePet", 
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    #endregion

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _animationTimer?.Stop();
        _animationTimer?.Dispose();

        if (_coinOverlay != null)
        {
            _coinOverlay.Hide();
            _coinOverlay.Close();
            _coinOverlay.Dispose();
            _coinOverlay = null;
        }

        base.OnFormClosing(e);
    }
}

enum PetState
{
    Idle,
    Blink,
    Happy,
    Petted,
    Walking
}

enum Facing
{
    Front,
    Left,
    Right
}
