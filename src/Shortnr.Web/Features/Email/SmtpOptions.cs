using System.ComponentModel.DataAnnotations;

namespace Shortnr.Web.Features.Email;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;
}