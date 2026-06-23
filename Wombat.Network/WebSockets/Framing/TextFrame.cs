using System;

namespace Wombat.Network.WebSockets
{
    internal sealed class TextFrame : DataFrame
    {
        public TextFrame(string text, bool isMasked = true)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentNullException(nameof(text));

            Text = text;
            IsMasked = isMasked;
        }

        public string Text { get; }
        public bool IsMasked { get; }
        public override OpCode OpCode => OpCode.Text;
    }
}
