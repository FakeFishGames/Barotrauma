using System;

namespace Barotrauma
{
    class UnimplementedScreen : Screen
    {
        public static readonly UnimplementedScreen Instance = new UnimplementedScreen();

        public override void Select()
        {
            throw new Exception("Tried to select unimplemented screen");
        }
    }
}
