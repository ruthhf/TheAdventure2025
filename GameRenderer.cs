using Silk.NET.Maths;
using Silk.NET.SDL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TheAdventure.Models;
using Point = Silk.NET.SDL.Point;

namespace TheAdventure;

public unsafe class GameRenderer
{
    private Sdl _sdl;
    private Renderer* _renderer;
    private GameWindow _window;
    public GameWindow Window => _window;
    private Camera _camera;

    private Dictionary<int, IntPtr> _texturePointers = new();
    private Dictionary<int, TextureData> _textureData = new();
    private int _textureId;
    private int _heartTextureId = -1;
    private TextureData _heartTextureData;
    private int _gameOverTextureId = -1;
    private TextureData _gameOverTextureData;
    public GameRenderer(Sdl sdl, GameWindow window)
    {
        _sdl = sdl;

        _renderer = (Renderer*)window.CreateRenderer();
        _sdl.SetRenderDrawBlendMode(_renderer, BlendMode.Blend);

        _window = window;
        var windowSize = window.Size;
        _camera = new Camera(windowSize.Width, windowSize.Height);
    }

    public void SetWorldBounds(Rectangle<int> bounds)
    {
        _camera.SetWorldBounds(bounds);
    }

    public void CameraLookAt(int x, int y)
    {
        _camera.LookAt(x, y);
    }

    public int LoadTexture(string fileName, out TextureData textureInfo)
    {
        using var fStream = new FileStream(fileName, FileMode.Open);
        var image = Image.Load<Rgba32>(fStream);
        textureInfo = new TextureData()
        {
            Width = image.Width,
            Height = image.Height
        };
        var imageRAWData = new byte[textureInfo.Width * textureInfo.Height * 4];
        image.CopyPixelDataTo(imageRAWData.AsSpan());

        fixed (byte* data = imageRAWData)
        {
            var imageSurface = _sdl.CreateRGBSurfaceWithFormatFrom(data, textureInfo.Width,
                textureInfo.Height, 8, textureInfo.Width * 4, (uint)PixelFormatEnum.Rgba32);
            if (imageSurface == null)
                throw new Exception("Failed to create surface from image data.");

            var imageTexture = _sdl.CreateTextureFromSurface(_renderer, imageSurface);
            if (imageTexture == null)
            {
                _sdl.FreeSurface(imageSurface);
                throw new Exception("Failed to create texture from surface.");
            }

            _sdl.FreeSurface(imageSurface);

            _textureData[_textureId] = textureInfo;
            _texturePointers[_textureId] = (IntPtr)imageTexture;
        }

        return _textureId++;
    }

    public void RenderTexture(int textureId, Rectangle<int> src, Rectangle<int> dst,
        RendererFlip flip = RendererFlip.None, double angle = 0.0, Point center = default)
    {
        if (_texturePointers.TryGetValue(textureId, out var imageTexture))
        {
            var translatedDst = _camera.ToScreenCoordinates(dst);
            _sdl.RenderCopyEx(_renderer, (Texture*)imageTexture, in src,
                in translatedDst,
                angle,
                in center, flip);
        }
    }

    public Vector2D<int> ToWorldCoordinates(int x, int y)
    {
        return _camera.ToWorldCoordinates(new Vector2D<int>(x, y));
    }

    public void SetDrawColor(byte r, byte g, byte b, byte a)
    {
        _sdl.SetRenderDrawColor(_renderer, r, g, b, a);
    }

    public void ClearScreen()
    {
        _sdl.RenderClear(_renderer);
    }

    public void PresentFrame()
    {
        _sdl.RenderPresent(_renderer);
    }

    // Load heart texture
    public void LoadHeartTexture()
    {
        _heartTextureId = LoadTexture("Assets/heart.png", out _heartTextureData);
    }

    // Draw a single heart at (x, y) with optional size
    public void DrawHeart(int x, int y, int size = 30)
    {
        if (_heartTextureId == -1)
            return; // Heart texture not loaded

        var srcRect = new Rectangle<int>(0, 0, _heartTextureData.Width, _heartTextureData.Height);
        var dstRect = new Rectangle<int>(x, y, size, size);

        _sdl.RenderCopy(_renderer, (Texture*)_texturePointers[_heartTextureId], in srcRect, in dstRect);
    }


// Draw multiple hearts in a row for the number of lives, spaced by 10 px plus heart size
    public void DrawHearts(int lives, int size = 30)
    {
        for (int i = 0; i < lives; i++)
        {
            int x = 10 + i * (size + 10);
            int y = 10;
            DrawHeart(x, y, size);
        }
    }
    public void LoadGameOverTexture()
    {
        _gameOverTextureId = LoadTexture("Assets/gameover.png", out _gameOverTextureData);
    }
    public Vector2D<int> WindowSize => new Vector2D<int>(_window.Size.Width, _window.Size.Height);

public void DrawGameOverScreen()
{
    if (_gameOverTextureId == -1)
    {
        LoadGameOverTexture();
    }

    int imgWidth = _gameOverTextureData.Width;
    int imgHeight = _gameOverTextureData.Height;

    // Calculate scale to fit within window
    float scaleX = (float)WindowSize.X / imgWidth;
    float scaleY = (float)WindowSize.Y / imgHeight;
    float scale = Math.Min(scaleX, scaleY); // Preserve aspect ratio

    int scaledWidth = (int)(imgWidth * scale);
    int scaledHeight = (int)(imgHeight * scale);

    int dstX = (WindowSize.X - scaledWidth) / 2;
    int dstY = (WindowSize.Y - scaledHeight) / 2;

    var srcRect = new Rectangle<int>(0, 0, imgWidth, imgHeight);
    var dstRect = new Rectangle<int>(dstX, dstY, scaledWidth, scaledHeight);

    _sdl.RenderCopy(_renderer, (Texture*)_texturePointers[_gameOverTextureId], in srcRect, in dstRect);
}

}