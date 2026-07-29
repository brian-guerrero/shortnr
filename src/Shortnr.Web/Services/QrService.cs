using QRCoder;

namespace Shortnr.Web.Services;

public sealed class QrService
{
    public byte[] GeneratePng(string content, int pixelsPerModule = 3)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }

    public string GenerateDataUri(string content, int pixelsPerModule = 3)
    {
        var bytes = GeneratePng(content, pixelsPerModule);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
}
