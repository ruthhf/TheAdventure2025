using Silk.NET.Maths;

namespace TheAdventure.Models;

public class CoinObject : RenderableGameObject
{
    private readonly GameRenderer _renderer;

    public CoinObject(GameRenderer renderer, int x, int y)
        : base(null, (x, y)) // Passing null for SpriteSheet since no animation
    {
        _renderer = renderer;
    }

    public override void Render(GameRenderer renderer)
    {

    // Position is in world space, so apply camera transform
 var dstRect = renderer.ToScreenCoordinates(
    new Rectangle<int>(Position.X, Position.Y, 32, 32));

    var srcRect = new Silk.NET.Maths.Rectangle<int>(0, 0,
        renderer.CoinTextureWidth,
        renderer.CoinTextureHeight);

    renderer.RenderCoinTexture(srcRect, dstRect);
}
}

