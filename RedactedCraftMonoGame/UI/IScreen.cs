using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RedactedCraftMonoGame.Core;

namespace RedactedCraftMonoGame.UI;

public interface IScreen
{
    void OnResize(Rectangle viewport);
    void Update(GameTime gameTime, InputState input);
    void Draw(SpriteBatch sb, Rectangle viewport);
    void OnClose() {}
}
