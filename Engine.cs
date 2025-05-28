using System.Reflection;
using System.Text.Json;
using Silk.NET.Maths;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;

namespace TheAdventure;

public class Engine
{
    private GameState _gameState = GameState.Playing;
    private int lives = 3;
    private int _score = 0;
    private int _highScore = 0;
    private const string HighScoreFile = "highscore.txt";

    private readonly GameRenderer _renderer;
    private readonly Input _input;
    private readonly ScriptEngine _scriptEngine = new();

    private readonly Dictionary<int, GameObject> _gameObjects = new();
    private readonly Dictionary<string, TileSet> _loadedTileSets = new();
    private readonly Dictionary<int, Tile> _tileIdMap = new();

    private Level _currentLevel = new();
    private PlayerObject? _player;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;

        _input.OnMouseClick += (_, coords) => AddBomb(coords.x, coords.y);
        LoadHighScore();
        _renderer.LoadCoinTexture();
    }

    public void SetupWorld()
    {
        _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent);
        if (level == null)
            throw new Exception("Failed to load level");

        foreach (var tileSetRef in level.TileSets)
        {
            var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
            var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent);
            if (tileSet == null)
                throw new Exception("Failed to load tile set");

            foreach (var tile in tileSet.Tiles)
            {
                tile.TextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                _tileIdMap.Add(tile.Id!.Value, tile);
            }

            _loadedTileSets.Add(tileSet.Name, tileSet);
        }

        if (level.Width == null || level.Height == null || level.TileWidth == null || level.TileHeight == null)
            throw new Exception("Invalid level dimensions");

        _renderer.SetWorldBounds(new Rectangle<int>(0, 0,
            level.Width.Value * level.TileWidth.Value,
            level.Height.Value * level.TileHeight.Value));

        _currentLevel = level;

        _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));

        var rand = new Random();
        for (int i = 0; i < 5; i++)
        {
            int x = rand.Next(0, level.Width.Value * level.TileWidth.Value);
            int y = rand.Next(0, level.Height.Value * level.TileHeight.Value);
            AddCoin(x, y);
        }
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;
        _lastUpdate = currentTime;

        if (_gameState == GameState.GameOver)
        {
            if (_input.IsKeyRPressed())
                RestartGame();
            return;
        }
        if (_player == null)
            return;

        double up = _input.IsUpPressed() ? 1.0 : 0.0;
        double down = _input.IsDownPressed() ? 1.0 : 0.0;
        double left = _input.IsLeftPressed() ? 1.0 : 0.0;
        double right = _input.IsRightPressed() ? 1.0 : 0.0;
        bool isAttacking = _input.IsKeyAPressed() && (up + down + left + right <= 1);
        bool addBomb = _input.IsKeyBPressed();

        _player.UpdatePosition(up, down, left, right, 48, 48, msSinceLastFrame);
        CollectCoins();

        if (isAttacking)
            _player.Attack();

        _scriptEngine.ExecuteAll(this);

        if (addBomb)
            AddBomb(_player.Position.X, _player.Position.Y, false);
    }

    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        if (_gameState == GameState.Playing)
        {
            var playerPosition = _player!.Position;
            _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);

            RenderTerrain();
            RenderAllObjects();

            _renderer.LoadHeartTexture();
            _renderer.DrawHearts(lives);
        }
        else if (_gameState == GameState.GameOver)
        {
            _renderer.DrawGameOverScreen();
        }

        _renderer.DrawText($"Score: {_score}", 10, 50, 255, 255, 255);
        _renderer.DrawText($"High Score: {_highScore}", 10, 70, 255, 255, 0);

        _renderer.PresentFrame();
    }

    public void RenderAllObjects()
    {
        var toRemove = new List<int>();
        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);
            if (gameObject is TemporaryGameObject { IsExpired: true } tempGameObject)
                toRemove.Add(tempGameObject.Id);
        }

        foreach (var id in toRemove)
        {
            if (_gameObjects.TryGetValue(id, out var gameObject))
            {
                _gameObjects.Remove(id);
                if (_player != null && gameObject is TemporaryGameObject tempGameObject)
                {
                    var deltaX = Math.Abs(_player.Position.X - tempGameObject.Position.X);
                    var deltaY = Math.Abs(_player.Position.Y - tempGameObject.Position.Y);
                    if (deltaX < 32 && deltaY < 32)
                    {
                        lives--;
                        if (lives <= 0)
                        {
                            _player.GameOver();
                            _gameState = GameState.GameOver;
                        }
                    }
                }
            }
        }

        _player?.Render(_renderer);
    }

    public void RenderTerrain()
    {
        foreach (var currentLayer in _currentLevel.Layers)
        {
            for (int i = 0; i < _currentLevel.Width; ++i)
            {
                for (int j = 0; j < _currentLevel.Height; ++j)
                {
                    int? dataIndex = j * currentLayer.Width + i;
                    if (dataIndex == null) continue;

                    var currentTileId = currentLayer.Data[dataIndex.Value] - 1;
                    if (currentTileId == null) continue;

                    var currentTile = _tileIdMap[currentTileId.Value];
                    var tileWidth = currentTile.ImageWidth ?? 0;
                    var tileHeight = currentTile.ImageHeight ?? 0;

                    var sourceRect = new Rectangle<int>(0, 0, tileWidth, tileHeight);
                    var destRect = new Rectangle<int>(i * tileWidth, j * tileHeight, tileWidth, tileHeight);
                    _renderer.RenderTexture(currentTile.TextureId, sourceRect, destRect);
                }
            }
        }
    }

    public IEnumerable<RenderableGameObject> GetRenderables()
    {
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is RenderableGameObject renderableGameObject)
                yield return renderableGameObject;
        }
    }

    public (int X, int Y) GetPlayerPosition() => _player!.Position;

    public void AddBomb(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);
        var spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
        spriteSheet.ActivateAnimation("Explode");
        var bomb = new TemporaryGameObject(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y));
        _gameObjects.Add(bomb.Id, bomb);
    }

    public int GetLives() => lives;

    private void RestartGame()
{
    if (_score > _highScore)
    {
        _highScore = _score;
        SaveHighScore();
    }

    _score = 0;
    lives = 3;
    _gameState = GameState.Playing;

    _player?.ResetPosition();
   // _gameObjects.Clear();

    // Respawn coins after clearing objects

}


    public enum GameState
    {
        Playing,
        GameOver
    }

    private void LoadHighScore()
    {
        if (File.Exists(HighScoreFile))
        {
            var text = File.ReadAllText(HighScoreFile);
            if (int.TryParse(text, out var savedScore))
                _highScore = savedScore;
        }
    }

    private void SaveHighScore() => File.WriteAllText(HighScoreFile, _highScore.ToString());

    public void AddCoin(int x, int y)
    {
        var coin = new CoinObject(_renderer, x, y);
        _gameObjects.Add(coin.Id, coin);
    }

    private void CollectCoins()
    {
        var coinsToRemove = new List<int>();
        var coinsToRespawn = new List<(int x, int y)>();

        foreach (var obj in _gameObjects.Values)
        {
            if (obj is CoinObject coin && PlayerCollidesWith(coin))
            {
                coinsToRemove.Add(coin.Id);
                _score++;
                if (_score > _highScore)
                    _highScore = _score;

                var rand = new Random();
                int newX = rand.Next(0, _currentLevel.Width!.Value * _currentLevel.TileWidth!.Value);
                int newY = rand.Next(0, _currentLevel.Height!.Value * _currentLevel.TileHeight!.Value);
                coinsToRespawn.Add((newX, newY));
            }
        }

        foreach (var id in coinsToRemove)
            _gameObjects.Remove(id);

        foreach (var (x, y) in coinsToRespawn)
            AddCoin(x, y);
    }

    private bool PlayerCollidesWith(GameObject obj)
    {
        if (obj is not RenderableGameObject renderable)
            return false;

        var playerPos = _player!.Position;
        var objPos = renderable.Position;

        const int collisionDistance = 32;

        return Math.Abs(playerPos.X - objPos.X) < collisionDistance &&
               Math.Abs(playerPos.Y - objPos.Y) < collisionDistance;
    }
}
