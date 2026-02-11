namespace Sharc.Benchmarks.Helpers;

/// <summary>
/// Constructs valid SQLite file headers and page headers for benchmark data setup.
/// </summary>
internal static class ValidHeaderFactory
{
    public static byte[] CreateDatabaseHeader(
        int pageSize = 4096,
        int pageCount = 100,
        int textEncoding = 1,
        byte writeVersion = 1,
        byte readVersion = 1)
    {
        var header = new byte[100];
        "SQLite format 3\0"u8.CopyTo(header);
        header[16] = (byte)(pageSize >> 8);
        header[17] = (byte)(pageSize & 0xFF);
        header[18] = writeVersion;
        header[19] = readVersion;
        header[20] = 0;  // reserved bytes
        header[21] = 64; // max embedded payload fraction
        header[22] = 32; // min embedded payload fraction
        header[23] = 32; // leaf payload fraction
        header[28] = (byte)(pageCount >> 24);
        header[29] = (byte)(pageCount >> 16);
        header[30] = (byte)(pageCount >> 8);
        header[31] = (byte)(pageCount & 0xFF);
        header[47] = 4;  // schema format
        header[56] = (byte)(textEncoding >> 24);
        header[57] = (byte)(textEncoding >> 16);
        header[58] = (byte)(textEncoding >> 8);
        header[59] = (byte)(textEncoding & 0xFF);
        return header;
    }

    public static byte[] CreateLeafTablePage(ushort cellCount, int pageSize = 4096)
    {
        var data = new byte[pageSize];
        data[0] = 0x0D; // leaf table
        data[3] = (byte)(cellCount >> 8);
        data[4] = (byte)(cellCount & 0xFF);
        ushort contentOffset = (ushort)(pageSize / 2);
        data[5] = (byte)(contentOffset >> 8);
        data[6] = (byte)(contentOffset & 0xFF);

        int offset = 8; // leaf header size
        for (int i = 0; i < cellCount; i++)
        {
            ushort cellOffset = (ushort)(contentOffset + i * 20);
            data[offset] = (byte)(cellOffset >> 8);
            data[offset + 1] = (byte)(cellOffset & 0xFF);
            offset += 2;
        }
        return data;
    }

    public static byte[] CreateInteriorTablePage(ushort cellCount, uint rightChild = 42, int pageSize = 4096)
    {
        var data = new byte[pageSize];
        data[0] = 0x05; // interior table
        data[3] = (byte)(cellCount >> 8);
        data[4] = (byte)(cellCount & 0xFF);
        ushort contentOffset = (ushort)(pageSize / 2);
        data[5] = (byte)(contentOffset >> 8);
        data[6] = (byte)(contentOffset & 0xFF);
        data[8] = (byte)(rightChild >> 24);
        data[9] = (byte)(rightChild >> 16);
        data[10] = (byte)(rightChild >> 8);
        data[11] = (byte)(rightChild & 0xFF);

        int offset = 12; // interior header size
        for (int i = 0; i < cellCount; i++)
        {
            ushort cellOffset = (ushort)(contentOffset + i * 20);
            data[offset] = (byte)(cellOffset >> 8);
            data[offset + 1] = (byte)(cellOffset & 0xFF);
            offset += 2;
        }
        return data;
    }
}
