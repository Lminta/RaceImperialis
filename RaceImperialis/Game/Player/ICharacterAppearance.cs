using System.Numerics;
using WolfSlop.Engine.Graphics;

namespace RaceImperialis.Game.Player;

public interface ICharacterAppearance
{
    TextureCatalog AddTextures(TextureCatalog scene);
    Sprite CreateSprite(IControlledActor actor, Vector2 observer);
}
