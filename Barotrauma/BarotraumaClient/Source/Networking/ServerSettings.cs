using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma.Networking
{
    partial class ServerSettings : ISerializableEntity
    {
        partial class NetPropertyData
        {
            public void Read(NetIncomingMessage msg)
            {
                long oldPos = msg.Position;
                UInt32 size = msg.ReadVariableUInt32();

                float x; float y; float z; float w;
                byte r; byte g; byte b; byte a;
                int ix; int iy; int width; int height;

                switch (typeString)
                {
                    case "float":
                        if (size != 4) break;
                        property.SetValue(msg.ReadFloat());
                        return;
                    case "vector2":
                        if (size != 8) break;
                        x = msg.ReadFloat();
                        y = msg.ReadFloat();
                        property.SetValue(new Vector2(x, y));
                        return;
                    case "vector3":
                        if (size != 12) break;
                        x = msg.ReadFloat();
                        y = msg.ReadFloat();
                        z = msg.ReadFloat();
                        property.SetValue(new Vector3(x, y, z));
                        return;
                    case "vector4":
                        if (size != 16) break;
                        x = msg.ReadFloat();
                        y = msg.ReadFloat();
                        z = msg.ReadFloat();
                        w = msg.ReadFloat();
                        property.SetValue(new Vector4(x, y, z, w));
                        return;
                    case "color":
                        if (size != 4) break;
                        r = msg.ReadByte();
                        g = msg.ReadByte();
                        b = msg.ReadByte();
                        a = msg.ReadByte();
                        property.SetValue(new Color(r, g, b, a));
                        return;
                    case "rectangle":
                        if (size != 16) break;
                        ix = msg.ReadInt32();
                        iy = msg.ReadInt32();
                        width = msg.ReadInt32();
                        height = msg.ReadInt32();
                        property.SetValue(new Rectangle(ix, iy, width, height));
                        return;
                    default:
                        msg.Position = oldPos; //reset position to properly read the string
                        string incVal = msg.ReadString();
                        property.TrySetValue(incVal);
                        return;
                }

                //size didn't match: skip this
                msg.Position += 8 * size;
            }
        }

        public void ClientRead(NetIncomingMessage incMsg)
        {
            SharedRead(incMsg);

            Voting.ClientRead(incMsg);

            if (incMsg.ReadBoolean())
            {
                isPublic = incMsg.ReadBoolean();
                EnableUPnP = incMsg.ReadBoolean();
                incMsg.ReadPadBits();
                QueryPort = incMsg.ReadUInt16();

                int count = incMsg.ReadUInt16();
                for (int i=0;i<count;i++)
                {
                    UInt32 key = incMsg.ReadUInt32();
                    if (netProperties.ContainsKey(key))
                    {
                        netProperties[key].Read(incMsg);
                    }
                    else
                    {
                        UInt32 size = incMsg.ReadVariableUInt32();
                        incMsg.Position += 8 * size;
                    }
                }
            }
        }
    }
}