using QRCoder;

namespace Shortnr.Web.Services;

public sealed class QrService
{
    public string GenerateDataUri(string content, int pixelsPerModule = 3)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(pixelsPerModule);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
}
