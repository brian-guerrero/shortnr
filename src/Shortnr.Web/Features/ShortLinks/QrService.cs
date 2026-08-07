using QRCoder;
using Microsoft.Extensions.Logging;

namespace Shortnr.Web.Features.ShortLinks;

public sealed class QrService
{
    private readonly ILogger<QrService> _logger;

    public QrService(ILogger<QrService> logger)
    {
        _logger = logger;
    }

    public byte[] GeneratePng(string content, int pixelsPerModule = 3)
    {
        _logger.LogDebug("Generating PNG QR code for: {Content}", content);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(pixelsPerModule);
        _logger.LogDebug("QR code generated {Size} bytes", bytes.Length);
        return bytes;
    }

    public string GenerateDataUri(string content, int pixelsPerModule = 3)
    {
        var bytes = GeneratePng(content, pixelsPerModule);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
}
