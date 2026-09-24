namespace Street_Rod_AC.Parts;

/// <summary>
/// Textures a KN5 model can carry without an image file behind them. Plain BCL on purpose: the parts converter
/// compiles it by link for the same textures it writes into converted parts.
/// </summary>
public static class Kn5Bitmap
{
    /// <summary>Smallest BMP the texture loader accepts, in one colour: 2x2 pixels, 24 bpp, rows padded to 4 bytes</summary>
    public static byte[] Solid(byte r, byte g, byte b)
    {
        const int headerSize = 54;
        const int rowSize = 8;

        var data = new byte[headerSize + rowSize * 2];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BitConverter.GetBytes(data.Length).CopyTo(data, 2);
        BitConverter.GetBytes(headerSize).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(2).CopyTo(data, 18);
        BitConverter.GetBytes(2).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        BitConverter.GetBytes(rowSize * 2).CopyTo(data, 34);

        for (var row = 0; row < 2; row++)
        {
            for (var pixel = 0; pixel < 2; pixel++)
            {
                var offset = headerSize + row * rowSize + pixel * 3;
                data[offset] = b;
                data[offset + 1] = g;
                data[offset + 2] = r;
            }
        }

        return data;
    }
}
